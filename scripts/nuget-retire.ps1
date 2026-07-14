<#
.SYNOPSIS
Bulk-retire old 2dog package versions on nuget.org.

.DESCRIPTION
Retiring = unlist (hide from search/UI; pinned restores keep working) and/or
deprecate (clients show a warning via `dotnet list package --deprecated` and
the VS package manager). Neither breaks existing consumers.

Three actions, all driven by the same plan (newest -Keep versions of every
package stay active; everything older is retired; dead packages are retired
entirely and deprecated with an alternate):

  plan       print what would be retired (default; no credentials needed)
  unlist     DELETE /api/v2/package/{id}/{version} for every retired version.
             Official, API-key auth. nuget.org rate-limits unlisting to
             250/hour per key - the script sleeps and retries on 429, so a
             large run may take over an hour.
  deprecate  POST /json/deprecation/deprecate per package (all retired
             versions in one call). UNOFFICIAL: this is the endpoint the
             nuget.org website itself uses; it needs a logged-in browser
             session's Cookie header and may break without notice. The
             officially supported alternative is the web UI, which has a
             "Select all versions" option (one pass per package).

.EXAMPLE
pwsh scripts/nuget-retire.ps1
# print the plan

.EXAMPLE
pwsh scripts/nuget-retire.ps1 -Action unlist -ApiKey $env:NUGET_UNLIST_KEY
# needs a classic API key with the "Unlist package" scope on 2dog*
# (Trusted Publishing OIDC keys are push-only and will not work)

.EXAMPLE
pwsh scripts/nuget-retire.ps1 -Action deprecate -Cookie $env:NUGET_COOKIE -PackageIds 2dog.osx-x64
# smoke-test the unofficial endpoint on the dead package first;
# Cookie = the full "Cookie:" request-header value copied from your
# browser's devtools on a logged-in nuget.org page
#>
[CmdletBinding()]
param(
    [ValidateSet('plan', 'unlist', 'deprecate')]
    [string]$Action = 'plan',

    # unlist: classic nuget.org API key with the Unlist scope for 2dog*
    [string]$ApiKey,

    # deprecate: full Cookie header of a logged-in nuget.org browser session
    [string]$Cookie,

    # newest N versions of each package to keep active
    [int]$Keep = 2,

    [string[]]$PackageIds = @(
        '2dog', '2dog.xunit', '2dog.cli', '2dog.Templates', '2dog.tools',
        '2dog.win-x64', '2dog.win-x64.debug', '2dog.win-x64.editor', '2dog.win-x64.release',
        '2dog.linux-x64', '2dog.linux-x64.debug', '2dog.linux-x64.editor', '2dog.linux-x64.release',
        '2dog.osx-arm64', '2dog.osx-arm64.debug', '2dog.osx-arm64.editor', '2dog.osx-arm64.release',
        '2dog.browser-wasm', '2dog.browser-wasm.debug', '2dog.browser-wasm.release',
        '2dog.osx-x64'
    ),

    [string]$Message = 'Superseded 2dog iteration - use the latest version. https://2dog.dev'
)

$ErrorActionPreference = 'Stop'

# Dead package ids -> the package that replaced them: ALL versions retire,
# and deprecation points consumers at the replacement.
$DeadPackages = @{ '2dog.osx-x64' = '2dog.osx-arm64' }

function Get-SortedVersions([string]$id) {
    # Flat-container gives every published version (listed or not), oldest
    # semantics unspecified - sort ourselves: numeric part, stable > pre.
    $index = Invoke-RestMethod "https://api.nuget.org/v3-flatcontainer/$($id.ToLowerInvariant())/index.json"
    $index.versions | ForEach-Object {
        $numeric, $pre = $_.Split('-', 2)
        $parts = @($numeric.Split('.') | ForEach-Object { [int]$_ })
        while ($parts.Count -lt 4) { $parts += 0 }
        [pscustomobject]@{
            Raw     = $_
            Numeric = [version]::new($parts[0], $parts[1], $parts[2], $parts[3])
            Stable  = -not $pre
            Pre     = $pre
        }
    } | Sort-Object Numeric, Stable, Pre | ForEach-Object Raw
}

function Get-Plan {
    foreach ($id in $PackageIds) {
        $versions = @(Get-SortedVersions $id)
        $dead = $DeadPackages.ContainsKey($id)
        $keepCount = if ($dead) { 0 } else { [Math]::Min($Keep, $versions.Count) }
        [pscustomobject]@{
            Id        = $id
            Dead      = $dead
            Alternate = if ($dead) { $DeadPackages[$id] } else { $null }
            Keep      = if ($keepCount) { $versions[-$keepCount..-1] } else { @() }
            Retire    = if ($versions.Count -gt $keepCount) { $versions[0..($versions.Count - $keepCount - 1)] } else { @() }
        }
    }
}

function Show-Plan($plan) {
    foreach ($p in $plan) {
        $note = if ($p.Dead) { " [dead -> $($p.Alternate)]" } else { '' }
        Write-Host ("{0}{1}: retire {2}, keep {3}" -f $p.Id, $note, $p.Retire.Count, ($p.Keep -join ', '))
        if ($VerbosePreference -eq 'Continue') { Write-Host ("    " + ($p.Retire -join ' ')) }
    }
    $total = ($plan | Measure-Object -Sum { $_.Retire.Count }).Sum
    Write-Host "`nTotal versions to retire: $total (unlist rate limit is 250/hour - the script waits on 429)"
}

function Invoke-Unlist($plan) {
    if (-not $ApiKey) { throw "unlist needs -ApiKey (classic key with the Unlist scope; Trusted Publishing keys are push-only)" }
    $done = 0
    $failed = @()
    foreach ($p in $plan) {
        foreach ($v in $p.Retire) {
            $attempt = 0
            while ($true) {
                $attempt++
                $r = Invoke-WebRequest -Method Delete -Uri "https://www.nuget.org/api/v2/package/$($p.Id)/$v" `
                    -Headers @{ 'X-NuGet-ApiKey' = $ApiKey } -SkipHttpErrorCheck
                if ($r.StatusCode -lt 300) { $done++; Write-Host "unlisted $($p.Id) $v"; break }
                if ($r.StatusCode -eq 429 -and $attempt -le 12) {
                    $wait = [int]($r.Headers['Retry-After'] | Select-Object -First 1)
                    if (-not $wait) { $wait = 300 }
                    Write-Host "rate-limited; sleeping ${wait}s ($done done so far)"
                    Start-Sleep -Seconds $wait
                    continue
                }
                $failed += "$($p.Id) $v -> HTTP $($r.StatusCode)"
                Write-Warning "failed: $($p.Id) $v -> HTTP $($r.StatusCode)"
                break
            }
            Start-Sleep -Milliseconds 500
        }
    }
    Write-Host "`nUnlisted $done version(s)."
    if ($failed) { Write-Warning ("Failed:`n" + ($failed -join "`n")) }
}

function Invoke-Deprecate($plan) {
    if (-not $Cookie) { throw "deprecate needs -Cookie (full Cookie header from a logged-in nuget.org browser session)" }
    Write-Warning "Using the nuget.org website's internal endpoint (no official API exists, see NuGet/NuGetGallery#8873). It may break without notice."

    foreach ($p in $plan) {
        if ($p.Retire.Count -eq 0) { continue }

        # The antiforgery form token comes from any Manage page of the package.
        $anyVersion = if ($p.Keep) { $p.Keep[-1] } else { $p.Retire[-1] }
        $manageUrl = "https://www.nuget.org/packages/$($p.Id)/$anyVersion/Manage"
        $page = Invoke-WebRequest -Uri $manageUrl -Headers @{ Cookie = $Cookie } -SkipHttpErrorCheck
        if ($page.StatusCode -ne 200) {
            Write-Warning "$($p.Id): Manage page returned HTTP $($page.StatusCode) - cookie expired or not an owner? Skipping."
            continue
        }
        if ($page.Content -notmatch 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') {
            Write-Warning "$($p.Id): no antiforgery token found on $manageUrl - skipping."
            continue
        }
        $token = $Matches[1]

        $fields = [System.Collections.Generic.List[string]]::new()
        $add = { param($k, $v) $fields.Add("$k=$([uri]::EscapeDataString($v))") }
        & $add '__RequestVerificationToken' $token
        & $add 'id' $p.Id
        foreach ($v in $p.Retire) { & $add 'versions' $v }
        & $add 'isLegacy' 'true'
        & $add 'hasCriticalBugs' 'false'
        & $add 'isOther' 'false'
        & $add 'customMessage' $Message
        if ($p.Alternate) { & $add 'alternatePackageId' $p.Alternate }

        $r = Invoke-WebRequest -Method Post -Uri 'https://www.nuget.org/json/deprecation/deprecate' `
            -Headers @{ Cookie = $Cookie; Referer = $manageUrl } `
            -ContentType 'application/x-www-form-urlencoded' `
            -Body ($fields -join '&') -SkipHttpErrorCheck
        if ($r.StatusCode -eq 200) {
            Write-Host "deprecated $($p.Id): $($p.Retire.Count) version(s)"
        }
        else {
            Write-Warning "$($p.Id): deprecation POST returned HTTP $($r.StatusCode): $($r.Content | Select-Object -First 1)"
        }
        Start-Sleep -Seconds 2
    }
}

$plan = @(Get-Plan)
Show-Plan $plan
switch ($Action) {
    'unlist' { Invoke-Unlist $plan }
    'deprecate' { Invoke-Deprecate $plan }
}

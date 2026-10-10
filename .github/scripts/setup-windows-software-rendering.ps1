param(
    [string] $Destination = (Join-Path $env:RUNNER_TEMP 'software-vulkan'),
    [switch] $RegisterDriver
)

$ErrorActionPreference = 'Stop'
if ($RegisterDriver -and $env:GITHUB_ACTIONS -ne 'true') {
    throw 'Driver registration is only for disposable CI VMs.'
}

# These CI-only DLLs stay in the job temp directory and never enter our packages.
# Pin both archives and verify the publishers' SHA256 values before extracting DLLs.
function Get-VerifiedArchive([string] $Uri, [string] $Path, [string] $Sha256) {
    Invoke-WebRequest -Uri $Uri -OutFile $Path
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) {
        throw "SHA256 mismatch for $Uri"
    }
}

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$Destination = (Resolve-Path -LiteralPath $Destination).Path
$mesaArchive = Join-Path $Destination 'mesa.7z'
Get-VerifiedArchive `
    'https://github.com/pal1000/mesa-dist-win/releases/download/26.2.4/mesa3d-26.2.4-release-msvc.7z' `
    $mesaArchive '351fc8c8b695878ffb3eaa044b3ead08672a48b1a045e3c3e3975811df0f6695'
# Windows' built-in bsdtar supports the archive's BCJ2 compression.
& tar -xf $mesaArchive -C $Destination x64/vulkan_lvp.dll x64/lvp_icd.x86_64.json
if ($LASTEXITCODE -ne 0) { throw 'Could not extract Mesa lavapipe.' }

$runtimeArchive = Join-Path $Destination 'vulkan-runtime.zip'
Get-VerifiedArchive `
    'https://sdk.lunarg.com/sdk/download/1.4.363.0/windows/VulkanRT-X64-1.4.363.0-Components.zip' `
    $runtimeArchive 'a25a927aa8b9f0371048f1861cf88ac3b9bc9b1fb332c42d897c8ab32695769a'
Expand-Archive -LiteralPath $runtimeArchive -DestinationPath $Destination -Force
$loaderDirectory = Join-Path $Destination 'VulkanRT-X64-1.4.363.0-Components/x64'

# Force the CPU driver even when a runner happens to expose a hardware GPU.
$env:VK_DRIVER_FILES = Join-Path $Destination 'x64/lvp_icd.x86_64.json'
$env:TWODOG_TEST_VULKAN_LOADER = Join-Path $loaderDirectory 'vulkan-1.dll'
$env:VK_LOADER_DRIVERS_SELECT = 'lvp_icd.x86_64.json'
if ($RegisterDriver) {
    # Elevated runners ignore VK_DRIVER_FILES. Register the verified software ICD
    # through Vulkan's standard machine registry location on this disposable VM.
    # https://github.com/KhronosGroup/Vulkan-Loader/blob/main/docs/LoaderDriverInterface.md#driver-discovery-on-windows
    $driverKey = 'HKLM:\SOFTWARE\Khronos\Vulkan\Drivers'
    if (-not (Test-Path -LiteralPath $driverKey)) { New-Item -Path $driverKey -Force | Out-Null }
    New-ItemProperty -Path $driverKey -Name $env:VK_DRIVER_FILES -PropertyType DWord -Value 0 -Force | Out-Null
}
# Diagnostic: lavapipe presents through GDI StretchBlt, which fails without an interactive window station.
function Write-SessionDiagnostics {
    Add-Type -Namespace TwoDog -Name WinSta -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetProcessWindowStation();
[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern bool GetUserObjectInformation(IntPtr obj, int index, byte[] info, int length, out int needed);
'@
    function Get-ObjectInfo([int] $Index) {
        $buffer = [byte[]]::new(512)
        $needed = 0
        if (-not [TwoDog.WinSta]::GetUserObjectInformation([TwoDog.WinSta]::GetProcessWindowStation(), $Index, $buffer, $buffer.Length, [ref] $needed)) {
            return $null
        }
        return $buffer[0..($needed - 1)]
    }
    $name = [Text.Encoding]::Unicode.GetString((Get-ObjectInfo 2)).TrimEnd([char] 0)
    $flags = [BitConverter]::ToInt32([byte[]] (Get-ObjectInfo 1), 8)
    "Session $((Get-Process -Id $PID).SessionId), UserInteractive=$([Environment]::UserInteractive), " +
        "window station '$name' visible=$(($flags -band 1) -ne 0)"
    $ancestors = @()
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $PID"
    while ($process -and $ancestors.Count -lt 12) {
        $ancestors += "$($process.Name)[$($process.ProcessId)]"
        if ($process.ParentProcessId -eq $process.ProcessId) { break }
        $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($process.ParentProcessId)"
    }
    "Process ancestry: $($ancestors -join ' <- ')"
    Get-CimInstance Win32_Service | Where-Object { $_.PathName -match 'Runner' } |
        ForEach-Object { "Runner service: $($_.Name) as $($_.StartName), $($_.State), $($_.PathName)" }
    Get-Process explorer -ErrorAction SilentlyContinue | ForEach-Object { "explorer.exe in session $($_.SessionId)" }
    Get-CimInstance Win32_VideoController |
        ForEach-Object { "Display adapter: $($_.Name), $($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)" }
    & query session 2>&1
}
try { Write-SessionDiagnostics } catch { "Session diagnostics failed: $_" }

& (Join-Path $loaderDirectory 'vulkaninfo.exe') --summary
if ($LASTEXITCODE -ne 0) { throw 'Mesa lavapipe could not initialize Vulkan.' }

if ($env:GITHUB_ENV) {
    @(
        "VK_DRIVER_FILES=$env:VK_DRIVER_FILES"
        "VK_LOADER_DRIVERS_SELECT=$env:VK_LOADER_DRIVERS_SELECT"
    ) | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
    "TWODOG_TEST_VULKAN_LOADER=$env:TWODOG_TEST_VULKAN_LOADER" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
}

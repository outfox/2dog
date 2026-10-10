param(
    [string] $Destination = (Join-Path $env:RUNNER_TEMP 'software-vulkan')
)

$ErrorActionPreference = 'Stop'

# These CI-only downloads do not install a system-wide driver or enter our packages.
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
& (Join-Path $loaderDirectory 'vulkaninfo.exe') --summary
if ($LASTEXITCODE -ne 0) { throw 'Mesa lavapipe could not initialize Vulkan.' }

if ($env:GITHUB_ENV) {
    "VK_DRIVER_FILES=$env:VK_DRIVER_FILES" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
    "TWODOG_TEST_VULKAN_LOADER=$env:TWODOG_TEST_VULKAN_LOADER" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
}

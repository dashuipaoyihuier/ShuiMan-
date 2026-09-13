[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'windows\ShuiMan.Windows\ShuiMan.Windows.csproj'
$outputRoot = Join-Path $repository 'build\windows'
$output = Join-Path $outputRoot 'ShuiMan'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 10 SDK, then open a new PowerShell window.'
}
& dotnet --version
if ($LASTEXITCODE -ne 0) { throw '.NET SDK is unavailable.' }
if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'windows-test.ps1') -Configuration $Configuration
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
& dotnet publish $project --configuration $Configuration --runtime win-x64 --self-contained true --output $output -p:PublishSingleFile=false -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { throw "Windows publish exited with code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath (Join-Path $output 'ShuiMan.exe'))) {
    throw 'Publish did not produce ShuiMan.exe.'
}
foreach ($requiredRuntime in @('coreclr.dll', 'hostfxr.dll', 'Magick.Native-Q8-x64.dll', 'pdfium.dll', 'WebView2Loader.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $output $requiredRuntime))) {
        throw "Publish is missing the required runtime file $requiredRuntime."
    }
}
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination (Join-Path $output 'LICENSE.txt') -Force
Copy-Item -Path (Join-Path $repository 'windows\distribution\*') -Destination $output -Force
$noticeDirectory = Join-Path $output 'licenses'
New-Item -ItemType Directory -Path $noticeDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'windows\ThirdPartyNotices.txt') -Destination (Join-Path $output 'ShuiMan-ThirdPartyNotices.txt') -Force
Copy-Item -Path (Join-Path $repository 'windows\licenses\*') -Destination $noticeDirectory -Force
$assetsPath = Join-Path $repository 'windows\ShuiMan.Windows\obj\project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
foreach ($packageRoot in $assets.packageFolders.PSObject.Properties.Name) {
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        $packageDirectory = Join-Path $packageRoot $library.Value.path
        if (-not (Test-Path -LiteralPath $packageDirectory)) { continue }
        foreach ($notice in Get-ChildItem -LiteralPath $packageDirectory -File) {
            if ($notice.Name -match '^(LICENSE|LICENCE|NOTICE|ThirdPartyNotices)(\..*)?$') {
                $noticeName = $library.Name.Replace('/', '-') + '-' + $notice.Name
                Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $noticeDirectory $noticeName) -Force
            }
        }
    }
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $output 'ShuiMan.runtimeconfig.json') -Raw | ConvertFrom-Json
    foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
        $runtimePackage = ($framework.name + '.Runtime.win-x64').ToLowerInvariant()
        $runtimeDirectory = Join-Path $packageRoot ($runtimePackage + '\' + $framework.version)
        if (-not (Test-Path -LiteralPath $runtimeDirectory)) { continue }
        foreach ($notice in Get-ChildItem -LiteralPath $runtimeDirectory -File) {
            if ($notice.Name -match '^(LICENSE|LICENCE|NOTICE|THIRD-PARTY-NOTICES)(\..*)?$') {
                Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $noticeDirectory ($runtimePackage + '-' + $framework.version + '-' + $notice.Name)) -Force
            }
        }
    }
}
if (-not (Get-ChildItem -LiteralPath $noticeDirectory -File | Where-Object { $_.Name -like 'microsoft.netcore.app.runtime.win-x64-*-LICENSE.TXT' })) {
    throw 'The portable distribution is missing the .NET runtime license.'
}
Set-Content -LiteralPath (Join-Path $output '.shuiman-install') -Value 'ShuiMan Windows portable distribution' -Encoding utf8
$archive = Join-Path $outputRoot 'ShuiMan-Windows-x64.zip'
Compress-Archive -LiteralPath $output -DestinationPath $archive -CompressionLevel Optimal -Force
$checksum = Get-FileHash -LiteralPath $archive -Algorithm SHA256
Set-Content -LiteralPath "$archive.sha256" -Value "$($checksum.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($archive))" -Encoding ascii
Write-Output "Portable app: $output"
Write-Output "Archive: $archive"
Write-Output "SHA256: $($checksum.Hash.ToLowerInvariant())"

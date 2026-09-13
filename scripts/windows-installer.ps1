[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.7.0',
    [string]$PayloadDirectory,
    [string]$CompilerPath,
    [switch]$TestMode
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (-not $PayloadDirectory) { $PayloadDirectory = Join-Path $repository 'build\windows\ShuiMan' }
$PayloadDirectory = [IO.Path]::GetFullPath($PayloadDirectory)
foreach ($required in @('ShuiMan.exe', 'ShuiMan.dll', 'coreclr.dll', 'hostfxr.dll', 'Magick.Native-Q8-x64.dll', 'pdfium.dll', 'LICENSE.txt', 'ShuiMan-ThirdPartyNotices.txt', 'licenses')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PayloadDirectory $required))) { throw "Missing payload item $required. Run scripts/windows-build.ps1 first." }
}
$payloadVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PayloadDirectory 'ShuiMan.exe')).ProductVersion.Split('+')[0]
if (-not $TestMode -and [version]$payloadVersion -ne [version]$Version) {
    throw "Payload version $payloadVersion does not match installer version $Version. Publish the final app before packaging. -TestMode only creates isolated test installers."
}
if (-not $CompilerPath) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $compilerCandidates = @(
        $env:INNO_SETUP_COMPILER,
        $(if ($compilerCommand) { $compilerCommand.Source }),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path (Split-Path -Parent $repository) 'tools\InnoSetup6\ISCC.exe')
    )
    $CompilerPath = $compilerCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Install official Inno Setup 6.7+ from https://jrsoftware.org/isdl.php, or pass -CompilerPath / INNO_SETUP_COMPILER.'
}
$outputDirectory = Join-Path $repository 'build\windows'
$compilerArguments = @('/Qp', "/DAppVersion=$Version", "/DPayloadDir=$PayloadDirectory")
if ($TestMode) {
    $outputDirectory = Join-Path $outputDirectory 'installer-native-smoke\artifacts'
    $testInstallDirectory = Join-Path $repository 'build\windows\installer-native-smoke\installed'
    $compilerArguments += @('/DTestMode', "/DTestInstallDir=$testInstallDirectory")
    $installerName = "ShuiMan-Installer-Test-$Version-x64.exe"
    Write-Output "TEST INSTALLER: isolated identity; payload $payloadVersion, test installer $Version."
} else {
    $installerName = "ShuiMan-Setup-$Version-x64.exe"
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$compilerArguments += @("/DOutputDir=$outputDirectory", (Join-Path $repository 'windows\installer\ShuiMan.iss'))
& $CompilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }
$installerPath = Join-Path $outputDirectory $installerName
$checksum = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
Set-Content -LiteralPath "$installerPath.sha256" -Value "$($checksum.Hash.ToLowerInvariant())  $installerName" -Encoding ascii
Write-Output "Installer: $installerPath"
Write-Output "SHA256: $($checksum.Hash.ToLowerInvariant())"

[CmdletBinding()]
param(
    [string]$CompilerPath,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$testRoot = [IO.Path]::GetFullPath((Join-Path $repository 'build\windows\installer-native-smoke'))
$installed = [IO.Path]::GetFullPath((Join-Path $testRoot 'installed'))
if (-not $installed.StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test directory.' }
$testId = 'ShuiMan.Installer.NativeSmoke.8BB2BDE2'
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${testId}_is1"
$progIdKey = 'HKCU:\Software\Classes\ShuiMan.NativeInstallerTest.Book'
$associationKey = 'HKCU:\Software\Classes\.shuiman-native-install-test\OpenWithProgids'
$startShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'ShuiMan Native Installer Test.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ShuiMan Native Installer Test.lnk'
$testResults = [Collections.Generic.List[string]]::new()
function Confirm-Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $testResults.Add($Name)
    Write-Output "PASS: $Name"
}
function Start-TestInstaller([string]$Executable, [string[]]$Arguments) {
    $resolved = [IO.Path]::GetFullPath($Executable)
    if (-not $resolved.StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Only isolated test binaries may be executed.' }
    $process = Start-Process -FilePath $resolved -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(180000)) { throw "Installer test timed out (PID $($process.Id)); inspect the test process before retrying." }
    if ($process.ExitCode -ne 0) { throw "Installer test exited with $($process.ExitCode). See logs in $testRoot." }
}
$buildParameters = @{ TestMode = $true; CompilerPath = $CompilerPath }
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'windows-installer.ps1') @buildParameters -Version '0.7.0'
    & (Join-Path $PSScriptRoot 'windows-installer.ps1') @buildParameters -Version '0.7.1'
}
foreach ($version in @('0.7.0', '0.7.1')) {
    $artifact = Join-Path $testRoot "artifacts\ShuiMan-Installer-Test-$version-x64.exe"
    Confirm-Check (Test-Path -LiteralPath $artifact) "Test installer $version exists"
    $actual = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    $expected = (Get-Content -LiteralPath "$artifact.sha256" -Raw).Split(' ')[0]
    Confirm-Check ($actual -eq $expected) "Test installer $version checksum"
}
if (Test-Path -LiteralPath $uninstallKey) { throw 'An earlier isolated test installation remains. Run its uninstaller before repeating the test.' }
if ((Test-Path -LiteralPath $startShortcut) -or (Test-Path -LiteralPath $desktopShortcut) -or (Test-Path -LiteralPath $progIdKey)) {
    throw 'An earlier isolated test shortcut or association remains. Inspect it before repeating the test.'
}
New-Item -ItemType Directory -Path (Join-Path $installed 'test-library') -Force | Out-Null
$bookMarker = Join-Path $installed 'original-comic.cbz'
$libraryMarker = Join-Path $installed 'test-library\library.json'
if ((Test-Path -LiteralPath $libraryMarker) -and (Get-Content -LiteralPath $libraryMarker -Raw).Trim() -ne '{"installerFixture":true,"page":7}') {
    throw 'The isolated library was changed outside this smoke test. It will not be overwritten. Preserve that data before preparing another test directory.'
}
if ((Test-Path -LiteralPath $bookMarker) -and (Get-Content -LiteralPath $bookMarker -Raw).Trim() -ne 'Generated installer preservation fixture; not a real book.') {
    throw 'The book preservation fixture was changed outside this smoke test. It will not be overwritten.'
}
Set-Content -LiteralPath $bookMarker -Value 'Generated installer preservation fixture; not a real book.' -Encoding utf8
Set-Content -LiteralPath $libraryMarker -Value '{"installerFixture":true,"page":7}' -Encoding utf8
$bookHash = (Get-FileHash -LiteralPath $bookMarker).Hash
$libraryHash = (Get-FileHash -LiteralPath $libraryMarker).Hash
# Simulate the prior portable install's helpers in the isolated destination only.
Set-Content -LiteralPath (Join-Path $installed '.shuiman-install') -Value 'ShuiMan Windows portable distribution' -Encoding utf8
Set-Content -LiteralPath (Join-Path $installed 'uninstall.ps1') -Value '# obsolete helper fixture' -Encoding utf8
Set-Content -LiteralPath (Join-Path $installed 'install.ps1') -Value '# obsolete helper fixture' -Encoding utf8
try {
    Start-TestInstaller (Join-Path $testRoot 'artifacts\ShuiMan-Installer-Test-0.7.0-x64.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/TASKS="desktopicon"', "/LOG=`"$testRoot\install.log`"")
    $browserPayload = @(Get-ChildItem -LiteralPath $installed -File -Filter '*WebView*')
    Confirm-Check ((Test-Path -LiteralPath (Join-Path $installed 'ShuiMan.exe')) -and $browserPayload.Count -eq 0) 'Offline native core installation without browser payload'
    Confirm-Check (Test-Path -LiteralPath (Join-Path $installed 'coreclr.dll')) 'Self-contained .NET installed'
    Confirm-Check (Test-Path -LiteralPath (Join-Path $installed 'pdfium.dll')) 'Offline PDF runtime installed'
    Confirm-Check (Test-Path -LiteralPath (Join-Path $installed 'licenses\LICENSE-Inno-Setup.txt')) 'Installer license installed'
    Confirm-Check (-not (Test-Path -LiteralPath (Join-Path $installed 'uninstall.ps1'))) 'Legacy uninstall helper removed'
    Confirm-Check ((Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion -eq '0.7.0') 'Windows installed-app registration'
    $shell = New-Object -ComObject WScript.Shell
    Confirm-Check ($shell.CreateShortcut($startShortcut).TargetPath -eq (Join-Path $installed 'ShuiMan.exe')) 'Start menu shortcut points to isolated app'
    Confirm-Check ($shell.CreateShortcut($desktopShortcut).TargetPath -eq (Join-Path $installed 'ShuiMan.exe')) 'Optional desktop shortcut'
    Confirm-Check (-not (Test-Path -LiteralPath $progIdKey)) 'File associations remain opt-in'
    Start-TestInstaller (Join-Path $testRoot 'artifacts\ShuiMan-Installer-Test-0.7.1-x64.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/TASKS="desktopicon,fileassociations"', "/LOG=`"$testRoot\upgrade.log`"")
    Confirm-Check ((Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion -eq '0.7.1') 'Upgrade updates the existing installed-app entry'
    Confirm-Check ((Get-Content -LiteralPath (Join-Path $installed 'install-version.txt') -Raw).Trim() -eq '0.7.1') 'Upgrade replaces installation metadata'
    Confirm-Check (Test-Path -LiteralPath $progIdKey) 'Opt-in Open With registration'
    Confirm-Check ((Get-Item -LiteralPath $associationKey).GetValueNames() -contains 'ShuiMan.NativeInstallerTest.Book') 'Opt-in association points to test ProgID'
    Confirm-Check ((Get-FileHash -LiteralPath $bookMarker).Hash -eq $bookHash -and (Get-FileHash -LiteralPath $libraryMarker).Hash -eq $libraryHash) 'Upgrade preserves original books and library'
    Start-TestInstaller (Join-Path $installed 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$testRoot\uninstall.log`"")
    Confirm-Check (-not (Test-Path -LiteralPath (Join-Path $installed 'ShuiMan.exe'))) 'Uninstaller removes installed application files'
    Confirm-Check (-not (Test-Path -LiteralPath $uninstallKey)) 'Uninstaller removes installed-app entry'
    Confirm-Check (-not (Test-Path -LiteralPath $startShortcut) -and -not (Test-Path -LiteralPath $desktopShortcut)) 'Uninstaller removes its shortcuts'
    Confirm-Check (-not (Test-Path -LiteralPath $progIdKey)) 'Uninstaller removes its Open With registration'
    Confirm-Check ((Get-FileHash -LiteralPath $bookMarker).Hash -eq $bookHash -and (Get-FileHash -LiteralPath $libraryMarker).Hash -eq $libraryHash) 'Uninstall preserves original books and library'
    $testResults | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'checks.json') -Encoding utf8
    Write-Output "Installer smoke: $($testResults.Count) passed. Logs: $testRoot"
} finally {
    if ((Test-Path -LiteralPath $uninstallKey) -and (Test-Path -LiteralPath (Join-Path $installed 'unins000.exe'))) {
        Write-Output 'Removing the remaining isolated test installation after a failed check.'
        Start-TestInstaller (Join-Path $installed 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    }
}

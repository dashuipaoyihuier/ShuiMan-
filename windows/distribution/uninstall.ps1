[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$programs = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
$destination = [IO.Path]::GetFullPath((Join-Path $programs 'ShuiMan'))
if ($destination -ne (Join-Path $programs 'ShuiMan') -or -not $destination.StartsWith($programs + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unexpected installation directory.'
}
if (-not (Test-Path -LiteralPath (Join-Path $destination '.shuiman-install'))) {
    throw 'This script only removes a marked ShuiMan installation in LocalAppData\Programs\ShuiMan.'
}
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'ShuiMan.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'ShuiMan.lnk')
)
$shell = New-Object -ComObject WScript.Shell
foreach ($shortcutPath in $shortcuts) {
    if (Test-Path -LiteralPath $shortcutPath) {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ($shortcut.TargetPath -eq (Join-Path $destination 'ShuiMan.exe')) { Remove-Item -LiteralPath $shortcutPath }
    }
}
Remove-Item -LiteralPath $destination -Recurse -Force
Write-Output 'ShuiMan removed. Your original books, library records and reading progress were preserved.'

[CmdletBinding()]
param([switch]$DesktopShortcut)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($PSScriptRoot)
$destination = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\ShuiMan'))
if (-not (Test-Path -LiteralPath (Join-Path $source 'ShuiMan.exe'))) { throw 'Keep install.ps1 beside ShuiMan.exe.' }
if ($source.TrimEnd('\') -ne $destination.TrimEnd('\')) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force
}
Set-Content -LiteralPath (Join-Path $destination '.shuiman-install') -Value 'ShuiMan Windows portable distribution' -Encoding utf8
$shell = New-Object -ComObject WScript.Shell
$startMenu = [Environment]::GetFolderPath('Programs')
$shortcutPaths = @((Join-Path $startMenu 'ShuiMan.lnk'))
if ($DesktopShortcut) { $shortcutPaths += Join-Path ([Environment]::GetFolderPath('Desktop')) 'ShuiMan.lnk' }
foreach ($shortcutPath in $shortcutPaths) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $destination 'ShuiMan.exe'
    $shortcut.WorkingDirectory = $destination
    $shortcut.Description = 'ShuiMan comic reader'
    $shortcut.Save()
}
Write-Output "Installed to $destination. Launch ShuiMan from the Start menu."

# Builds a Release copy of the overlay into its own folder and puts shortcuts on the desktop and
# Start menu. Run again after changes to update the installed copy:
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1
param([string]$Destination = "$env:LOCALAPPDATA\Programs\Run8 OCU II")

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $Destination 'Beltpack.Overlay.exe'

$running = Get-Process Beltpack.Overlay -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) {
    Write-Host 'The installed overlay is running. Exit it (right-click the case, Exit) and run this again.'
    exit 1
}

dotnet publish (Join-Path $root 'src\Beltpack.Overlay') -c Release -o $Destination --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item (Join-Path $root 'docs\OCU-II-Manual.md') $Destination -Force

$shell = New-Object -ComObject WScript.Shell
foreach ($folder in [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs')) {
    $link = $shell.CreateShortcut((Join-Path $folder 'Run8 OCU II.lnk'))
    $link.TargetPath = $exe
    $link.WorkingDirectory = $Destination
    $link.Description = 'Beltpack OCU II remote control for Run8'
    $link.Save()
}

$version = git -C $root describe --tags --always --dirty 2>$null
Write-Host "Installed $version to $Destination"

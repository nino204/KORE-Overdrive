[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\KORE Overdrive')
)
$ErrorActionPreference = 'Stop'
$taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$taskIdentity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Abra PowerShell como administrador, na conta que usará o aplicativo.'
}
$taskPackage = $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $taskPackage 'KOREOverdrive.exe'))) {
    $taskPackage = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'
}
foreach ($taskRequired in @('KOREOverdrive.exe','Tools\Backend\KOREOverdrive.Backend.exe','Tools\CpuTuner\KORECpuTuner.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskPackage $taskRequired))) { throw "Pacote incompleto: $taskRequired. Execute Build.ps1 primeiro." }
}
if (Get-ScheduledTask -TaskName 'KOREOverdriveBackend' -ErrorAction SilentlyContinue) {
    throw 'A tarefa KOREOverdriveBackend já existe. Preserve os baselines e revise a atualização manualmente.'
}
$taskDestination = [IO.Path]::GetFullPath($InstallDirectory)
if ($taskDestination.TrimEnd('\') -eq [IO.Path]::GetPathRoot($taskDestination).TrimEnd('\')) { throw 'Diretório de instalação inválido.' }
if (Test-Path -LiteralPath $taskDestination) { throw 'O diretório de destino já existe. O instalador não sobrescreve instalações.' }
New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
Get-ChildItem -LiteralPath $taskPackage | Copy-Item -Destination $taskDestination -Recurse
$taskCpuHelper = Join-Path $taskDestination 'Tools\CpuTuner\KORECpuTuner.exe'
& $taskCpuHelper capture-baseline
if ($LASTEXITCODE -ne 0) { throw 'Baseline não capturado. Nenhuma tarefa foi registrada; revise requisitos, modelo e estado Stock.' }

$taskBackend = Join-Path $taskDestination 'Tools\Backend\KOREOverdrive.Backend.exe'
$taskAction = New-ScheduledTaskAction -Execute $taskBackend -WorkingDirectory (Split-Path -Parent $taskBackend)
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $taskIdentity.Name -LogonType Interactive -RunLevel Highest
$taskSettings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName 'KOREOverdriveBackend' -Action $taskAction -Principal $taskPrincipal -Settings $taskSettings -Description 'KORE Overdrive: telemetria e proteção térmica local.' | Out-Null

$taskShell = New-Object -ComObject WScript.Shell
$taskShortcut = $taskShell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'KORE Overdrive.lnk'))
$taskShortcut.TargetPath = Join-Path $taskDestination 'KOREOverdrive.exe'
$taskShortcut.WorkingDirectory = $taskDestination
$taskShortcut.Description = 'KORE Overdrive — perfis e telemetria de CPU/GPU'
$taskShortcut.Save()
Write-Output 'Instalado. Abra o atalho KORE Overdrive na Área de Trabalho.'

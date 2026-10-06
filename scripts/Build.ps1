[CmdletBinding()]
param(
    [string]$LibreHardwareMonitorPath = 'C:\Program Files (x86)\FanControl\LibreHardwareMonitorLib.dll'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDist = Join-Path $taskRoot 'dist'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'SDK .NET 10 não encontrado.' }
if (-not (Test-Path -LiteralPath $LibreHardwareMonitorPath)) { throw 'Informe o caminho de LibreHardwareMonitorLib.dll com -LibreHardwareMonitorPath.' }
$taskLib = (Resolve-Path -LiteralPath $LibreHardwareMonitorPath).Path
New-Item -ItemType Directory -Path $taskDist -Force | Out-Null

& dotnet publish (Join-Path $taskRoot 'src\KOREOverdrive\KOREOverdrive.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $taskDist --nologo
if ($LASTEXITCODE -ne 0) { throw 'Compilação da interface falhou.' }
& dotnet publish (Join-Path $taskRoot 'src\KOREOverdrive\Backend\KOREOverdrive.Backend.csproj') -c Release -r win-x64 --self-contained false "-p:LibreHardwareMonitorPath=$taskLib" -o (Join-Path $taskDist 'Tools\Backend') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Compilação do backend falhou.' }
& dotnet publish (Join-Path $taskRoot 'src\KORECpuTuner\KORECpuTuner.csproj') -c Release -r win-x64 --self-contained false -o (Join-Path $taskDist 'Tools\CpuTuner') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Compilação do helper da CPU falhou.' }

& dotnet (Join-Path $taskDist 'Tools\Backend\KOREOverdrive.Backend.dll') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Verificações internas do backend falharam.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $taskDist -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md'),(Join-Path $taskRoot 'LICENSE'),(Join-Path $taskRoot 'THIRD_PARTY_NOTICES.md') -Destination $taskDist -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs') -Destination $taskDist -Recurse -Force
Write-Output "Compilação e verificações concluídas. Pacote em $taskDist"


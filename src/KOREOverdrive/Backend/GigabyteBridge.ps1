param([ValidateSet('read','max','auto','daemon')][string]$Action='read')
$ErrorActionPreference='Stop'
try {
  $taskDllDir='C:\Program Files (x86)\Gigabyte\EasyTuneEngineService'
  $taskCommon=[Reflection.Assembly]::LoadFrom((Join-Path $taskDllDir 'Gigabyte.EnvironmentControl.Common.dll'))
  $taskRem=[Reflection.Assembly]::LoadFrom((Join-Path $taskDllDir 'Gigabyte.EnvironmentControl.Remoting.dll'))
  $taskMonitorType=$taskRem.GetType('Gigabyte.EnvironmentControl.Remoting.HardwareMonitor.HardwareMonitorControlWrapper')
  $taskSourceType=$taskCommon.GetType('Gigabyte.EnvironmentControl.Common.HardwareMonitor.HardwareMonitorSourceTypes')
  $taskColType=$taskCommon.GetType('Gigabyte.EnvironmentControl.Common.HardwareMonitor.HardwareMonitoredDataCollection')
  $taskMonitor=[Activator]::CreateInstance($taskMonitorType)
  $taskInit=$taskMonitorType.GetMethods() | Where-Object {$_.Name -eq 'Initialize' -and $_.GetParameters().Count -eq 1} | Select-Object -First 1
  $taskInit.Invoke($taskMonitor,@([Enum]::Parse($taskSourceType,'HwRegister'))) | Out-Null
  $taskCoord=[Activator]::CreateInstance($taskRem.GetType('Gigabyte.EnvironmentControl.Remoting.CoolingDevice.Fan.SmartGuardianFanControlCoordinateSystemWrapper'))
  $taskGuardian=[Activator]::CreateInstance($taskRem.GetType('Gigabyte.EnvironmentControl.Remoting.CoolingDevice.Fan.SmartGuardianFanControlWrapper'))
  $taskPointType=$taskCommon.GetType('Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan.CoordinateSystem.SmartFanDataPointSet')
  $taskDataPointType=$taskCommon.GetType('Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan.CoordinateSystem.SmartFanDataPoint')
  $taskGetFan=$taskCoord.GetType().GetMethods() | Where-Object {$_.Name -eq 'Get'} | Select-Object -First 1
  $taskSetFan=$taskCoord.GetType().GetMethods() | Where-Object {$_.Name -eq 'Set'} | Select-Object -First 1
  $taskFanCount=$taskCoord.HardwareInfo.FanControlCount
  if(-not $taskCoord.IsSupported -or $taskFanCount -lt 1){throw ('Controle de fan Gigabyte indisponivel; count='+$taskFanCount+'; admin='+([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))}
} catch {
  @{Temperature=$null;Frequency=$null;Vcore=$null;FanRpm=$null;Fans=@();Error=$_.Exception.ToString()} | ConvertTo-Json -Compress
  exit 1
}
function Invoke-Bridge([string]$Action) {
try {
  $taskBaselinePath=Join-Path $env:LOCALAPPDATA 'KOREOverdrive\fan-baseline.json'
  $taskCurrent=@()
  for($taskIndex=0;$taskIndex -lt $taskFanCount;$taskIndex++) {
    $taskParams=New-Object object[] 2; $taskParams[0]=$taskIndex; $taskParams[1]=[Activator]::CreateInstance($taskPointType)
    $taskGetFan.Invoke($taskCoord,$taskParams) | Out-Null
    $taskCurrent+= ,$taskParams[1]
  }
  if(-not (Test-Path -LiteralPath $taskBaselinePath)) {
    if(@($taskCurrent | Where-Object {$_.Mode.ToString() -notin @('SmartFanMode','StairStepMode')}).Count){throw 'Baseline automatico de fans ausente'}
    New-Item -ItemType Directory -Path (Split-Path $taskBaselinePath) -Force | Out-Null
    ConvertTo-Json -InputObject @($taskCurrent) -Depth 10 | Set-Content -LiteralPath $taskBaselinePath -Encoding UTF8
  }
  $taskBaseline=Get-Content -LiteralPath $taskBaselinePath -Raw | ConvertFrom-Json
  if($taskBaseline.Count -ne $taskFanCount){throw 'Baseline de fans nao corresponde ao hardware'}
  if($Action -ne 'read') {
    for($taskIndex=0;$taskIndex -lt $taskFanCount;$taskIndex++) {
      $taskConfig=$taskCurrent[$taskIndex]
      if($Action -eq 'max') {
        $taskConfig.Mode=[Enum]::Parse($taskConfig.Mode.GetType(),'SmartFanMode')
        foreach($taskPoint in $taskConfig.DataPoints){$taskPoint.Y=255}
      } else {
        $taskBase=$taskBaseline[$taskIndex]
        $taskConfig.Mode=[Enum]::ToObject($taskConfig.Mode.GetType(),[int]$taskBase.Mode)
        $taskConfig.DeltaTemperature=[int]$taskBase.DeltaTemperature
        $taskConfig.FanSpeed=[int]$taskBase.FanSpeed
        $taskConfig.DataPoints.Clear()
        foreach($taskSaved in $taskBase.DataPoints) {
          $taskPoint=[Activator]::CreateInstance($taskDataPointType)
          $taskPoint.X=[double]$taskSaved.X; $taskPoint.Y=[double]$taskSaved.Y
          $taskConfig.DataPoints.Add($taskPoint)
        }
      }
      $taskSetFan.Invoke($taskCoord,[object[]]@([int]$taskIndex,$taskConfig)) | Out-Null
    }
  }
  $taskFans=@()
  for($taskIndex=0;$taskIndex -lt $taskFanCount;$taskIndex++) {
    $taskParams=New-Object object[] 2; $taskParams[0]=$taskIndex; $taskParams[1]=[Activator]::CreateInstance($taskPointType)
    $taskGetFan.Invoke($taskCoord,$taskParams) | Out-Null
    $taskRead=$taskParams[1]; $taskBase=$taskBaseline[$taskIndex]
    $taskMatches=($taskRead.Mode.ToString() -in @('SmartFanMode','StairStepMode') -and $taskRead.DataPoints.Count -eq $taskBase.DataPoints.Count)
    for($taskPointIndex=0;$taskPointIndex -lt $taskRead.DataPoints.Count -and $taskMatches;$taskPointIndex++) {
      $taskMatches=($taskRead.DataPoints[$taskPointIndex].X -eq $taskBase.DataPoints[$taskPointIndex].X -and $taskRead.DataPoints[$taskPointIndex].Y -eq $taskBase.DataPoints[$taskPointIndex].Y)
    }
    $taskFans+= [pscustomobject]@{Index=$taskIndex;Mode=$taskRead.Mode.ToString();Maximum=($taskRead.DataPoints.Count -gt 0 -and @($taskRead.DataPoints | Where-Object {$_.Y -ne 255}).Count -eq 0);BaselineMatches=[bool]$taskMatches}
  }
  $taskGet=$taskMonitorType.GetMethods() | Where-Object {$_.Name -eq 'GetCurrentMonitoredData' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.IsByRef} | Select-Object -First 1
  $taskParams=New-Object object[] 1; $taskParams[0]=[Activator]::CreateInstance($taskColType)
  $taskGet.Invoke($taskMonitor,$taskParams) | Out-Null
  $taskCol=$taskParams[0]
  $taskResult=@{Temperature=$null;Frequency=$null;Vcore=$null;FanRpm=$null;Fans=@($taskFans);Error=$null}
  for($taskIndex=0;$taskIndex -lt $taskCol.Count;$taskIndex++) {
    $taskItem=$taskCol[$taskIndex]; $taskType=$taskItem.Type.ToString(); $taskTitle=$taskItem.Title
    if($taskTitle -eq 'CPU') {
      if($taskType -eq 'Temperature' -and $taskItem.Value -gt 0){$taskResult.Temperature=[double]$taskItem.Value}
      if($taskType -eq 'Frequency'){$taskResult.Frequency=[double]$taskItem.Value}
      if($taskType -eq 'Fan'){$taskResult.FanRpm=[double]$taskItem.Value}
    }
    if($taskType -eq 'Voltage' -and $taskTitle -eq 'CPU Vcore'){$taskResult.Vcore=[double]$taskItem.Value}
  }
  ConvertTo-Json -InputObject $taskResult -Depth 5 -Compress
} catch {
  @{Temperature=$null;Frequency=$null;Vcore=$null;FanRpm=$null;Fans=@();Error=$_.Exception.ToString()} | ConvertTo-Json -Compress
  exit 1
}
}
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
if($Action -eq 'daemon') {
  while($null -ne ($taskInput=[Console]::ReadLine())) {
    if($taskInput -notin @('read','max','auto')){continue}
    $taskOutput=Invoke-Bridge $taskInput
    [Console]::WriteLine([string]$taskOutput)
    [Console]::Out.Flush()
  }
} else { Invoke-Bridge $Action }

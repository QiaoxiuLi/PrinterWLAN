$ErrorActionPreference = 'Stop'

$taskName = 'PrinterWLAN Management Console'
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($task) {
  Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
  Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Stop
}

$consoleProcesses = Get-CimInstance Win32_Process -Filter "Name='cmd.exe'" | Where-Object {
  $_.CommandLine -match '(?i)printerwlan-console\.cmd'
}
foreach ($consoleProcess in $consoleProcesses) {
  Invoke-CimMethod -InputObject $consoleProcess -MethodName Terminate | Out-Null
}

$service = Get-Service -Name 'PrinterWLAN' -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
  Stop-Service -Name 'PrinterWLAN' -Force -ErrorAction Stop
  $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(60))
}

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
$consoleProcessIds = @($consoleProcesses | Select-Object -ExpandProperty ProcessId)
foreach ($consoleProcess in $consoleProcesses) {
  Invoke-CimMethod -InputObject $consoleProcess -MethodName Terminate | Out-Null
}
if ($consoleProcessIds.Count -gt 0) {
  $consoleDeadline = (Get-Date).AddSeconds(15)
  do {
    $remainingConsoleProcesses = @(Get-Process -Id $consoleProcessIds -ErrorAction SilentlyContinue)
    if ($remainingConsoleProcesses.Count -eq 0) { break }
    Start-Sleep -Milliseconds 250
  } while ((Get-Date) -lt $consoleDeadline)
  if ($remainingConsoleProcesses.Count -gt 0) {
    throw "PrinterWLAN management console processes did not exit: $($remainingConsoleProcesses.Id -join ', ')."
  }
}

$service = Get-Service -Name 'PrinterWLAN' -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
  Stop-Service -Name 'PrinterWLAN' -Force -ErrorAction Stop
  $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(60))
}
$serviceProcessDeadline = (Get-Date).AddSeconds(15)
do {
  $remainingServiceProcesses = @(Get-Process -Name 'PrinterWLAN' -ErrorAction SilentlyContinue)
  if ($remainingServiceProcesses.Count -eq 0) { break }
  Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $serviceProcessDeadline)
if ($remainingServiceProcesses.Count -gt 0) {
  throw "PrinterWLAN service processes did not exit: $($remainingServiceProcesses.Id -join ', ')."
}

$libreOfficeRoot = Join-Path $PSScriptRoot 'third-party\LibreOffice'
if (Test-Path -LiteralPath $libreOfficeRoot) {
  Get-ChildItem -LiteralPath $libreOfficeRoot -Directory -Filter '__pycache__' -Recurse -Force -ErrorAction SilentlyContinue |
    Sort-Object { $_.FullName.Length } -Descending |
    Remove-Item -Recurse -Force -ErrorAction Stop
}

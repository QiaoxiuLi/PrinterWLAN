param(
  [Parameter(Mandatory=$true)][string]$InstallDirectory
)

$ErrorActionPreference = 'Stop'
$taskName = 'PrinterWLAN Management Console'
$launcher = Join-Path $InstallDirectory 'printerwlan-console.cmd'
if (-not (Test-Path -LiteralPath $launcher)) {
  throw "Management console launcher is missing: $launcher"
}

$arguments = '/d /k ""{0}""' -f $launcher
$action = New-ScheduledTaskAction `
  -Execute $env:ComSpec `
  -Argument $arguments `
  -WorkingDirectory $InstallDirectory
$trigger = New-ScheduledTaskTrigger -AtLogOn
$settings = New-ScheduledTaskSettingsSet `
  -AllowStartIfOnBatteries `
  -DontStopIfGoingOnBatteries

Register-ScheduledTask `
  -TaskName $taskName `
  -Action $action `
  -Trigger $trigger `
  -Settings $settings `
  -RunLevel Highest `
  -Force | Out-Null

$registered = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
if ($registered.Actions.Execute -notmatch '(?i)cmd\.exe$' -or
    $registered.Actions.Arguments -notmatch '(?i)(^|\s)/k(\s|$)' -or
    $registered.Actions.Arguments -notmatch 'printerwlan-console\.cmd') {
  throw 'The management console logon task was registered with an unexpected action.'
}

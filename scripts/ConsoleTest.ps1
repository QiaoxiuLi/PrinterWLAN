param(
  [Parameter(Mandatory=$true)][string]$AdminPassword
)
$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:ProgramFiles 'PrinterWLAN'
$launcher = Join-Path $installDirectory 'printerwlan-console.cmd'
$executable = Join-Path $installDirectory 'PrinterWLAN.exe'
if (-not (Test-Path $launcher) -or -not (Test-Path $executable)) { throw 'Management console files are missing.' }

$shortcutPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\PrinterWLAN\PrinterWLAN 管理控制台.lnk'
if (-not (Test-Path $shortcutPath)) { throw 'Management console Start Menu shortcut is missing.' }
$launcherText = Get-Content -LiteralPath $launcher -Raw
foreach ($expected in @(
  'set "PATH=%~dp0;%PATH%"',
  'PrinterWLAN.exe" status',
  'printerwlan doctor',
  'printerwlan pwd',
  '此窗口会保持打开'
)) {
  if ($launcherText -notlike "*$expected*") { throw "Management console launcher is missing '$expected'." }
}

function Test-PersistentLaunch([scriptblock]$Launch, [string]$Label) {
  $before=@(Get-CimInstance Win32_Process -Filter "Name='cmd.exe'" | Select-Object -ExpandProperty ProcessId)
  & $Launch
  Start-Sleep -Seconds 3
  $launched=@(Get-CimInstance Win32_Process -Filter "Name='cmd.exe'" | Where-Object {
    $before -notcontains $_.ProcessId -and $_.CommandLine -match '(?i)(^|\s)/k(\s|$)' -and $_.CommandLine -match 'printerwlan-console\.cmd'
  })
  if ($launched.Count -eq 0) { throw "$Label did not open a persistent cmd.exe /k management console." }
  foreach ($item in $launched) { Invoke-CimMethod -InputObject $item -MethodName Terminate | Out-Null }
}

Test-PersistentLaunch { Start-Process -FilePath $shortcutPath } 'Start Menu shortcut'

$taskXml = Export-ScheduledTask -TaskName 'PrinterWLAN Management Console'
if ($taskXml -notmatch '(?i)cmd\.exe' -or $taskXml -notmatch '(?i)(^|[\s&gt;])/k([\s&lt;]|$)' -or $taskXml -notmatch 'printerwlan-console\.cmd') {
  throw 'ONLOGON management task does not use a persistent cmd.exe /k console.'
}
Test-PersistentLaunch { Start-ScheduledTask -TaskName 'PrinterWLAN Management Console' } 'ONLOGON scheduled task'

$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $env:ComSpec
$start.Arguments = "/d /q /k `"`"$launcher`"`""
$start.WorkingDirectory = $installDirectory
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
if (-not $process.Start()) { throw 'Could not open management console.' }
try {
  Start-Sleep -Seconds 3
  if ($process.HasExited) { throw 'Management console exited instead of staying open.' }
  $process.StandardInput.WriteLine('exit')
  $process.StandardInput.Flush()
  if (-not $process.WaitForExit(30000)) { throw 'Management console did not close after the exit command.' }
} finally {
  if (-not $process.HasExited) { $process.Kill() }
}

& $executable status
if ($LASTEXITCODE -ne 0) { throw 'The status command failed from the installed management executable.' }
& $executable doctor
if ($LASTEXITCODE -ne 0) { throw 'The doctor command failed from the installed management executable.' }
& $executable pwd $AdminPassword
if ($LASTEXITCODE -ne 0) { throw 'The pwd command failed from the installed management executable.' }
Write-Host 'Start Menu, post-install-equivalent, ONLOGON, persistent console, status, doctor, and pwd checks passed.'

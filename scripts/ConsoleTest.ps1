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
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
if ([IO.Path]::GetFileName($shortcut.TargetPath) -ine 'cmd.exe' -or $shortcut.Arguments -notmatch '(?i)(^|\s)/k(\s|$)' -or $shortcut.Arguments -notmatch 'printerwlan-console\.cmd') {
  throw "Start Menu shortcut does not use a persistent cmd.exe /k console: $($shortcut.TargetPath) $($shortcut.Arguments)"
}

$taskXml = Export-ScheduledTask -TaskName 'PrinterWLAN Management Console'
if ($taskXml -notmatch '(?i)cmd\.exe' -or $taskXml -notmatch '(?i)(^|[\s&gt;])/k([\s&lt;]|$)' -or $taskXml -notmatch 'printerwlan-console\.cmd') {
  throw 'ONLOGON management task does not use a persistent cmd.exe /k console.'
}

$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $env:ComSpec
$start.Arguments = "/d /q /k `"`"$launcher`"`""
$start.WorkingDirectory = $installDirectory
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
if (-not $process.Start()) { throw 'Could not open management console.' }
Start-Sleep -Seconds 3
if ($process.HasExited) { throw 'Management console exited instead of staying open.' }

$process.StandardInput.WriteLine('printerwlan status')
$process.StandardInput.WriteLine('printerwlan doctor')
$process.StandardInput.WriteLine(('printerwlan pwd "{0}"' -f $AdminPassword.Replace('"','')))
$process.StandardInput.WriteLine('exit')
$process.StandardInput.Flush()
$outputTask = $process.StandardOutput.ReadToEndAsync()
$errorTask = $process.StandardError.ReadToEndAsync()
if (-not $process.WaitForExit(120000)) { $process.Kill($true); throw 'Management console commands timed out.' }
$output = $outputTask.Result + "`n" + $errorTask.Result
if ($process.ExitCode -ne 0) { throw "Management console failed with exit code $($process.ExitCode): $output" }
foreach ($expected in @('PrinterWLAN v1.2.0','兼容性检查','管理员密码已更新，立即生效。','此窗口会保持打开')) {
  if ($output -notlike "*$expected*") { throw "Management console output is missing '$expected'." }
}
Write-Host 'Start Menu, post-install-equivalent, ONLOGON, persistent console, status, doctor, and pwd checks passed.'

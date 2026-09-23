param(
  [Parameter(Mandatory=$true)][string]$InstallerPath
)
$ErrorActionPreference = 'Stop'
$appDirectory = Join-Path $env:ProgramFiles 'PrinterWLAN'
$dataDirectory = Join-Path $env:ProgramData 'PrinterWLAN'
$uninstaller = Join-Path $appDirectory 'unins000.exe'

function Assert-IntegrationRemoved([bool]$ExpectData) {
  if (Get-Service PrinterWLAN -ErrorAction SilentlyContinue) { throw 'Service still exists after uninstall.' }
  if (Get-NetFirewallRule -DisplayName 'PrinterWLAN HTTP 8080' -ErrorAction SilentlyContinue) { throw 'Firewall rule still exists after uninstall.' }
  if (Get-ScheduledTask -TaskName 'PrinterWLAN Management Console' -ErrorAction SilentlyContinue) { throw 'ONLOGON management task still exists after uninstall.' }
  if (Test-Path $appDirectory) {
    $remaining = @(Get-ChildItem $appDirectory -Force -Recurse -ErrorAction SilentlyContinue | Select-Object -First 20 -ExpandProperty FullName)
    throw "Application directory still exists after uninstall. Remaining entries: $($remaining -join '; ')"
  }
  $machinePath=[Environment]::GetEnvironmentVariable('Path','Machine')
  if (($machinePath -split ';') -contains $appDirectory) { throw 'PrinterWLAN install directory remains in machine PATH after uninstall.' }
  if ((Test-Path $dataDirectory) -ne $ExpectData) { throw "ProgramData preservation result was incorrect. Expected data=$ExpectData." }
}

if (-not (Test-Path $uninstaller)) { throw 'Uninstaller is missing before preservation test.' }
if (-not (Test-Path (Join-Path $dataDirectory 'Data\app.db'))) { throw 'Business data is missing before preservation test.' }
$result=Start-Process $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/PRESERVEDATA') -Wait -PassThru
if ($result.ExitCode -ne 0) { throw "Preserving uninstall failed with exit code $($result.ExitCode)." }
Assert-IntegrationRemoved $true

$result=Start-Process $InstallerPath -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/FAKEPRINTER') -Wait -PassThru
if ($result.ExitCode -ne 0) { throw "Reinstall for delete-data test failed with exit code $($result.ExitCode)." }
$deadline=(Get-Date).AddMinutes(2)
do { try { if ((Invoke-RestMethod 'http://127.0.0.1:8080/health' -TimeoutSec 3).status -eq 'ok') { break } } catch { Start-Sleep -Seconds 2 } } while ((Get-Date) -lt $deadline)
if (-not (Test-Path $uninstaller)) { throw 'Uninstaller is missing before delete-data test.' }
$result=Start-Process $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/DELETEDATA') -Wait -PassThru
if ($result.ExitCode -ne 0) { throw "Delete-data uninstall failed with exit code $($result.ExitCode)." }
Assert-IntegrationRemoved $false
Write-Host 'Uninstall preservation and permanent deletion modes removed service, firewall, task, PATH entry, and application files as expected.'

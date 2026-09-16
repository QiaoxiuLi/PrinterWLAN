param(
  [Parameter(Mandatory=$true)][string]$InstallerPath,
  [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/windows-client-acceptance'),
  [string]$AdminPassword = $env:PRINTERWLAN_ADMIN_PASSWORD
)
$ErrorActionPreference = 'Stop'

$os = Get-CimInstance Win32_OperatingSystem
if ([int]$os.ProductType -ne 1) { throw "This acceptance test requires Windows 10 or Windows 11 Desktop; detected $($os.Caption)." }
$build = [int]$os.BuildNumber
$isWindows10 = $os.Caption -match 'Windows 10'
$isWindows11 = $os.Caption -match 'Windows 11'
if (-not ($isWindows10 -or $isWindows11)) { throw "Unsupported Windows client: $($os.Caption)." }
if ($isWindows10 -and $build -lt 17763) { throw "Windows 10 build $build is older than the supported minimum build 17763." }
if (-not [Environment]::Is64BitOperatingSystem) { throw 'PrinterWLAN requires 64-bit Windows.' }

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
& (Join-Path $PSScriptRoot 'SmokeTest.ps1') -InstallerPath $InstallerPath -OutputDirectory $OutputDirectory -AdminPassword $AdminPassword -PrinterMode SystemPdf

$service = Get-CimInstance Win32_Service -Filter "Name='PrinterWLAN'"
if (-not $service -or $service.State -ne 'Running' -or $service.StartMode -ne 'Auto') { throw 'PrinterWLAN is not running as an automatic Windows Service.' }
$dependencies = (Get-Service PrinterWLAN).ServicesDependedOn.Name
if ($dependencies -notcontains 'Spooler') { throw 'PrinterWLAN Windows Service does not depend on the Print Spooler.' }
$delayed = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\PrinterWLAN' -Name DelayedAutostart -ErrorAction Stop).DelayedAutostart
if ($delayed -ne 1) { throw 'PrinterWLAN Windows Service is not configured for delayed automatic startup.' }

$executable = "$env:ProgramFiles\PrinterWLAN\PrinterWLAN.exe"
$evidence = [ordered]@{
  testedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
  windowsCaption = $os.Caption
  windowsVersion = $os.Version
  windowsBuild = $build
  osArchitecture = $os.OSArchitecture
  processorArchitecture = $env:PROCESSOR_ARCHITECTURE
  printerWlanVersion = (Get-Item $executable).VersionInfo.ProductVersion
  serviceState = $service.State
  serviceStartMode = $service.StartMode
  delayedAutomaticStart = $delayed
  serviceDependencies = @($dependencies)
  realWindowsPrintDriver = 'Microsoft Print to PDF'
  printedOutput = (Join-Path $OutputDirectory 'windows-driver-output.pdf')
  result = 'passed'
}
$evidence | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory 'compatibility-evidence.json') -Encoding utf8
Write-Host "Windows client acceptance passed on $($os.Caption) build $build."

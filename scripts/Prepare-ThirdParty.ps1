param(
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
  [switch]$SkipLibreOffice
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content (Join-Path $RepositoryRoot 'third-party/manifests/dependencies.json') -Raw | ConvertFrom-Json
$downloads = Join-Path $RepositoryRoot 'third-party/downloads'
$runtime = Join-Path $RepositoryRoot 'third-party/runtime'
$licenses = Join-Path $RepositoryRoot 'third-party/licenses'
New-Item -ItemType Directory -Force $downloads,$runtime,$licenses | Out-Null

function Get-VerifiedFile($dependency, $destination) {
  if (-not (Test-Path $destination)) { Invoke-WebRequest -Uri $dependency.url -OutFile $destination }
  $actual = (Get-FileHash -Algorithm SHA256 $destination).Hash.ToLowerInvariant()
  if ($actual -ne $dependency.sha256) { Remove-Item $destination -Force; throw "SHA-256 mismatch for $($dependency.name). Expected $($dependency.sha256), received $actual." }
}

$pdf = $manifest.downloads | Where-Object name -Like 'Mozilla PDF.js*'
$pdfArchive = Join-Path $downloads "pdfjs-dist-$($pdf.version).tgz"
Get-VerifiedFile $pdf $pdfArchive
$pdfTemp = Join-Path $runtime 'pdfjs-extract'
if (Test-Path $pdfTemp) { Remove-Item $pdfTemp -Recurse -Force }
New-Item -ItemType Directory -Force $pdfTemp | Out-Null
tar -xf $pdfArchive -C $pdfTemp
$pdfTarget = Join-Path $RepositoryRoot 'src/PrinterWLAN/wwwroot/vendor/pdfjs'
New-Item -ItemType Directory -Force $pdfTarget | Out-Null
Copy-Item (Join-Path $pdfTemp 'package/build/pdf.mjs') $pdfTarget -Force
Copy-Item (Join-Path $pdfTemp 'package/build/pdf.worker.mjs') $pdfTarget -Force
foreach ($folder in @('cmaps','standard_fonts','wasm')) {
  $source = Join-Path $pdfTemp "package/$folder"
  if (Test-Path $source) { Copy-Item $source (Join-Path $pdfTarget $folder) -Recurse -Force }
}
Copy-Item (Join-Path $pdfTemp 'package/LICENSE') (Join-Path $licenses 'PDF.js-Apache-2.0.txt') -Force
Remove-Item $pdfTemp -Recurse -Force

$pdfium = $manifest.downloads | Where-Object name -Like 'PDFium*'
$pdfiumArchive = Join-Path $downloads "pdfium-$($pdfium.version)-win-x64.tgz"
Get-VerifiedFile $pdfium $pdfiumArchive
$pdfiumTemp = Join-Path $runtime 'pdfium-license-extract'
if (Test-Path $pdfiumTemp) { Remove-Item $pdfiumTemp -Recurse -Force }
New-Item -ItemType Directory -Force $pdfiumTemp | Out-Null
tar -xf $pdfiumArchive -C $pdfiumTemp licenses
$pdfiumLicenses = Join-Path $licenses 'PDFium'
if (Test-Path $pdfiumLicenses) { Remove-Item $pdfiumLicenses -Recurse -Force }
Copy-Item (Join-Path $pdfiumTemp 'licenses') $pdfiumLicenses -Recurse -Force
Remove-Item $pdfiumTemp -Recurse -Force

$pdfFixture = $manifest.downloads | Where-Object name -Like 'PDFtoImage*fixture'
$fixtureDownload = Join-Path $downloads 'PDFtoImage-SocialPreview.pdf'
Get-VerifiedFile $pdfFixture $fixtureDownload
$fixtureTarget = Join-Path $runtime 'test-fixtures'
New-Item -ItemType Directory -Force $fixtureTarget | Out-Null
Copy-Item $fixtureDownload (Join-Path $fixtureTarget 'SocialPreview.pdf') -Force

if (-not $SkipLibreOffice) {
  $lo = $manifest.downloads | Where-Object name -Like 'LibreOffice*'
  $loMsi = Join-Path $downloads "LibreOffice-$($lo.version)-x64.msi"
  Get-VerifiedFile $lo $loMsi
  $loExtract = Join-Path $runtime 'LibreOffice'
  if (Test-Path $loExtract) { Remove-Item $loExtract -Recurse -Force }
  $process = Start-Process msiexec.exe -ArgumentList @('/a',('"'+$loMsi+'"'),('/qn'),('TARGETDIR="'+$loExtract+'"')) -Wait -PassThru
  if ($process.ExitCode -ne 0) { throw "LibreOffice administrative extraction failed with exit code $($process.ExitCode)." }
  if (-not (Test-Path (Join-Path $loExtract 'program/soffice.exe'))) {
    $nested = Get-ChildItem $loExtract -Filter soffice.exe -Recurse | Select-Object -First 1
    if (-not $nested) { throw 'LibreOffice soffice.exe was not found after MSI extraction.' }
    $root = Split-Path -Parent (Split-Path -Parent $nested.FullName)
    $normalized = Join-Path $runtime 'LibreOffice-normalized'
    Move-Item $root $normalized
    Remove-Item $loExtract -Recurse -Force
    Move-Item $normalized $loExtract
  }
  Get-ChildItem $loExtract -File -Recurse | Where-Object Name -Match '^(LICENSE|NOTICE|COPYING)' | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $licenses ('LibreOffice-'+$_.Name)) -Force
  }
}

Write-Host 'Pinned third-party runtimes prepared and verified.'

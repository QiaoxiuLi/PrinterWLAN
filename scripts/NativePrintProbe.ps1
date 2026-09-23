param(
  [Parameter(Mandatory=$true)][string]$PrinterName
)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
$document = New-Object System.Drawing.Printing.PrintDocument
try {
  $document.PrinterSettings.PrinterName = $PrinterName
  if (-not $document.PrinterSettings.IsValid) { throw "Printer is not valid: $PrinterName" }
  $document.DocumentName = 'PrinterWLAN native Windows driver control'
  $document.add_PrintPage({
    param($sender, $eventArgs)
    $eventArgs.Graphics.DrawString(
      'PrinterWLAN Windows native driver control',
      [System.Drawing.SystemFonts]::DefaultFont,
      [System.Drawing.Brushes]::Black,
      20,
      20)
  })
  $document.Print()
  Write-Host "Native Windows print control submitted from $([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture)."
}
finally {
  $document.Dispose()
}

# Third-Party Notices

PrinterWLAN is MIT licensed and includes or redistributes the following third-party components in its Windows installer. License and notice material supplied by PDF.js, LibreOffice, SkiaSharp, and PDFium's pinned upstream redistribution archive is copied into the installed `third-party/licenses` directory during the reproducible package build.

| Component | Locked version | License | Project |
|---|---:|---|---|
| Microsoft .NET / ASP.NET Core | 10.0.12 runtime | MIT | https://github.com/dotnet/runtime |
| Microsoft.Data.Sqlite / SQLitePCLRaw | 10.0.12 | MIT / Public Domain | https://www.nuget.org/packages/Microsoft.Data.Sqlite |
| CsvHelper | 33.1.0 | MS-PL / Apache-2.0 dual license | https://joshclose.github.io/CsvHelper/ |
| DocumentFormat.OpenXml | 3.5.1 | MIT | https://github.com/dotnet/Open-XML-SDK |
| PDFtoImage | 5.4.0 | MIT | https://github.com/sungaila/PDFtoImage |
| PDFium (bblanchon builds) | 152.0.7961 | PDFium BSD-style license and bundled third-party notices; wrapper MIT | https://github.com/bblanchon/pdfium-binaries |
| SkiaSharp | 4.150.1 | MIT | https://github.com/mono/SkiaSharp |
| Mozilla PDF.js | 6.3.289 | Apache-2.0 | https://github.com/mozilla/pdf.js |
| LibreOffice | 26.8.0 Windows x64 | MPL-2.0 / LGPL-3.0-or-later | https://www.libreoffice.org/ |
| Microsoft Visual C++ Redistributable | 14.50.35719 x64/ARM64 | Microsoft Software License Terms | https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist |
| Serilog.AspNetCore | 10.0.0 | Apache-2.0 | https://github.com/serilog/serilog-aspnetcore |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | https://github.com/serilog/serilog-sinks-file |

LibreOffice is distributed as an independent, unmodified program and is invoked through its documented command-line interface. PrinterWLAN does not link against LibreOffice libraries. LibreOffice source availability and licensing information are at https://www.libreoffice.org/about-us/licenses/ and https://download.documentfoundation.org/libreoffice/src/.

PDFium contains third-party code covered by additional notices. The `licenses` directory from the corresponding pinned `chromium/7961` Windows x64 redistribution archive is hash-verified and retained in the published output.

No endorsement by any third-party project is implied. Names and trademarks belong to their respective owners.

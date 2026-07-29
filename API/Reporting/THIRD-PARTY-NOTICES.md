# Third-party notices — reporting subsystem

The requirement for this feature was MIT-licensed libraries only. That is met everywhere except one
dependency, which is called out first because it is the exception rather than the rule.

## The exception: Oracle

| Package | Version | Licence |
| --- | --- | --- |
| `Oracle.ManagedDataAccess.Core` | 23.26.300 | Oracle Free Use Terms and Conditions — **not** MIT |
| `Oracle.EntityFrameworkCore` | 10.23.26300 | Oracle Free Use Terms and Conditions — **not** MIT |

Both are free of charge, and both are published by Oracle rather than under an open-source licence.

**There is no MIT-licensed Oracle driver for .NET.** ODP.NET is Oracle's own managed driver and the only
supported way for .NET to speak to an Oracle database directly. The alternatives are worse on this axis,
not better: `dotConnect for Oracle` is commercial, and going through Oracle REST Data Services would keep
the .NET side MIT at the cost of requiring ORDS to be deployed and maintained on the database side.

This was raised before the code was written and accepted as a deliberate trade-off. Two things limit the
blast radius:

- **Data access is confined to one file.** `Data/OracleRefCursorDataProvider.cs` is the only place that
  touches the Oracle client, and it sits behind `IReportDataProvider`. Nothing upstream of that interface
  — grouping, aggregation, rules, rendering — knows Oracle exists. Swapping it for an ORDS-over-HTTP
  provider later means writing one class and changing one registration.
- **Template storage is provider-switched, not Oracle-bound.** `Reporting:Database:Provider` selects
  SQLite or Oracle from the same entities and the same migrations, so development and CI never load the
  Oracle EF provider at all.

If the licence terms ever become unacceptable, set `Reporting:Database:Provider` to `Sqlite` (or any other
EF provider), drop both packages, and replace the one data-provider class.

## Everything else

| Package | Version | Licence | Used for |
| --- | --- | --- | --- |
| `PuppeteerSharp` | 25.4.0 | MIT | Driving headless Chromium to print HTML to PDF |
| `DynamicExpresso.Core` | 2.19.3 | MIT | Evaluating conditional-rule and computed-column expressions |
| `Microsoft.EntityFrameworkCore.*` | 10.0.10 | MIT | Data access |
| `Microsoft.AspNetCore.*` | 10.0.10 | MIT | Web framework |
| `Swashbuckle.AspNetCore` | 10.2.3 | MIT | OpenAPI document and Swagger UI |
| `SQLitePCLRaw.bundle_e_sqlite3` | 3.0.5 | Apache-2.0 | SQLite native library, pinned above the version EF Core pulls in transitively to clear NU1903 (GHSA-2m69-gcr7-jv3q) |
| `xunit`, `xunit.runner.visualstudio` | 2.9.3 / 3.1.4 | MIT | Tests only, not shipped |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.10 | MIT | Tests only, not shipped |
| `react-rnd` | 10.x | MIT | Drag and resize in the designer canvas |

## Runtime components, not packages

**Chromium** is invoked as an external process. Chromium itself is BSD-3-Clause, and it bundles components
under their own permissive licences (Blink, Skia, HarfBuzz and others). It is not redistributed by this
repository: `Reporting:Chromium:ExecutablePath` points at a browser the host already provides, and
PuppeteerSharp's downloader is only a development fallback.

Chromium is the reason Hebrew works. It implements the Unicode bidirectional algorithm and shapes text
with HarfBuzz, so mixed Hebrew, Latin and numeric content resolves correctly and `direction: rtl` tables
mirror their column order. The pure-.NET PDF libraries were considered and rejected on exactly this
point — PDFsharp and MigraDoc have no bidi implementation, which would have meant reversing strings by
hand, and that breaks on mixed content. QuestPDF was ruled out for a different reason: it left MIT for a
revenue-gated licence.

**Fonts.** By default the generated HTML names installed families and embeds nothing, so no font is
redistributed. The first family in the fallback chain is DejaVu Sans, which is under a permissive
Bitstream Vera derived licence and covers Hebrew letters and niqqud.

Setting `Reporting:Fonts:Embed` to `true` inlines every font found in `Reporting:Fonts:Directory` as a
base64 `@font-face` rule, which makes output byte-identical regardless of the host. **Anything placed in
that directory is embedded in generated PDFs, so only put fonts there whose licence permits
redistribution** — Noto Sans Hebrew and Rubik are both OFL 1.1 and suitable. No font files ship in this
repository.

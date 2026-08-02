# Dependency Decision — LiveCharts / SkiaSharp / OpenTK

## Confirmed chain

`LiveChartsCore.SkiaSharpView.WPF` depends on `SkiaSharp.Views.WPF`, which depends on the legacy `OpenTK` and `OpenTK.GLWpfControl` packages. SkiaSharp issue 3316 documents the same NU1701 warning combination on .NET 8 WPF:

- https://github.com/mono/SkiaSharp/issues/3316

## Decision

- Upgrade LiveCharts from 2.0.2 to 2.0.5, whose package explicitly targets `net8.0-windows7.0`.
- Upgrade and pin SkiaSharp WPF/HarfBuzz from 3.119.0 to 3.119.4.
- Pin the legacy OpenTK dependencies directly at the versions required by SkiaSharp.
- Scope NU1701 suppression to those three audited package references only. No project-wide warning suppression is used.
- Do not remove the OpenTK runtime assets: an executed WPF chart test proved `LiveChartsCore.SkiaSharpView.WPF.MotionCanvas` loads `GLWpfControl` during initialization and fails immediately if it is absent.

The current LiveCharts WPF package and framework declarations are published at:

- https://www.nuget.org/packages/LiveChartsCore.SkiaSharpView.WPF/

## SDK policy

`global.json` selects the installed 9.0.305 SDK as the minimum and rolls forward to the latest installed 9.0 feature band, without prereleases. SDK selection is independent of the application's `net8.0-windows` target. The policy follows Microsoft's documented `global.json` model:

- https://learn.microsoft.com/dotnet/core/tools/global-json

## Runtime evidence

- Forced no-cache solution restore: 0 warnings.
- Release solution build: 0 warnings, 0 errors.
- WPF CartesianChart construction, layout, and bitmap render: passed on an STA thread.
- Skia native surface draw and PNG encode: passed.
- QuestPDF report generation and PDF signature check: passed.
- Thermal receipt, reporting, treasury-print routing, and full WPF login tests: passed.
- The required `GLWpfControl.dll`, `OpenTK.dll`, and SkiaSharp assemblies are present in Release output.

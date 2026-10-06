<p align="right"><a href="README.zh-TW.md">繁體中文</a></p>

# Neurocle

Neurocle is a fully offline Windows desktop application for manually annotating image datasets. It supports independent, named classification (CLA), object-detection (OBD), and segmentation (SEG) annotation sets over one shared image pool.

> The first release deliberately excludes model training, inference, auto-annotation, cloud storage, accounts, telemetry, and automatic updates.

## Features

- Import PNG, JPG, JPEG, and BMP files or folders into a managed local project.
- Browse, search, sort, preview, and recover missing project images.
- Create multiple isolated CLA, OBD, and SEG annotation sets, each with its own classes, dataset assignments, Retest flag, and undo/redo history.
- Annotate CLA labels, OBD rectangles, and SEG rectangles, ellipses, polygons, brushes, lines, and local erasing.
- Assign images to `train`, `test`, unassigned, or `not_used`; use stratified automatic splitting and Retest management.
- Import and export one selected annotation set as validated JSON; export selected or all available images as a flat ZIP archive.
- Keep local database backups and daily diagnostic logs; restore a project safely after a failed replacement.
- Run entirely offline from a portable x64 ZIP.

## Requirements

- Windows 10 or Windows 11 x64
- .NET Framework 4.7.2 runtime
- A local fixed drive for projects; network, removable, NAS, and cloud-synced locations are intentionally rejected

## Build and test

Open `Neurocle.sln` in Visual Studio 2019 or later with .NET Framework 4.7.2 support, or run the following in PowerShell:

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' Neurocle.sln -m:1 -p:Configuration=Release -p:Platform=x64
& 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe' 'tests\Neurocle.Tests\bin\x64\Release\Neurocle.Tests.dll'
```

The current automated suite contains 86 MSTest tests. GitHub Actions builds, tests, packages, and verifies the portable runtime contents on `windows-2022`.

## Portable release

The application output is located at `src\Neurocle.WinForms\bin\x64\Release`. Create a portable archive with:

```powershell
New-Item -ItemType Directory -Force -Path artifacts | Out-Null
Compress-Archive -Path .\src\Neurocle.WinForms\bin\x64\Release\* -DestinationPath .\artifacts\Neurocle-win-x64.zip -Force
```

Extract the ZIP and launch `Neurocle.WinForms.exe`. The archive includes the x64 SQLite native library and `THIRD-PARTY-NOTICES.md`.

## Anonymous acceptance assets

Do not use private datasets for release acceptance. Create three small synthetic PNG files instead:

```powershell
& .\tools\New-AcceptanceAssets.ps1 -OutputDirectory .\artifacts\acceptance\images
```

See [the release-candidate acceptance guide](docs/acceptance/release-candidate.md) for the full repeatable checklist.

## Documentation

- [Domain context](CONTEXT.md)
- [Product specification](docs/specs/ai-labeling-tool.md)
- [Implementation plan](docs/plans/implementation-plan.md)
- [Test and acceptance plan](docs/plans/test-and-acceptance-plan.md)
- [Architecture decisions](docs/adr/)
- [Third-party notices](licenses/THIRD-PARTY-NOTICES.md)

## License notices

The third-party runtime notices are available in [licenses/THIRD-PARTY-NOTICES.md](licenses/THIRD-PARTY-NOTICES.md) and are included in portable releases.

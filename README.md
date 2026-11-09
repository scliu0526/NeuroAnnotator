<p align="right"><a href="README.zh-TW.md">繁體中文</a></p>

# Neurocle

Neurocle is a fully offline Windows desktop application for manually annotating image datasets. It supports independent, named classification (CLA), object-detection (OBD), and segmentation (SEG) annotation sets over one shared image pool.

> The first release deliberately excludes model training, inference, auto-annotation, cloud storage, accounts, telemetry, and automatic updates.

## Features

- Import PNG, JPG, JPEG, and BMP files or folders with visible progress. PNG/JPG/JPEG files retain their format; BMP files are converted to PNG.
- Browse, search, sort, preview, delete selected or all images, and recover missing project images.
- Hold Shift while using the mouse wheel in the image list to scroll horizontally without changing selection.
- Create multiple isolated CLA, OBD, and SEG annotation sets from one dialog, then switch annotation sets from either the annotation or dataset page.
- View and sort each image by its present annotation classes and annotation count.
- Annotate CLA labels, OBD rectangles, and SEG rectangles, ellipses, polygons, brushes, lines, and local erasing. New drawing gestures must start inside the image; a gesture started inside can continue outside and is clamped to the image edge. The canvas thumbnail stays hidden until the pencil annotation is completed or canceled.
- Before a SEG pencil (P) region is closed, undo each released pencil stroke independently; redo is unavailable during this stage. After closure, the whole annotation is one Undo/Redo action. Each brush (B) release still completes an annotation immediately. CLA assignment, replacement, and clearing never add Undo or Redo actions.
- Apply a CLA class to all selected images. Batch processing continues after an image fails and reports the affected images and reasons.
- Show expanded import-job details beneath the card actions, increasing the card height so the details do not cover other controls.
- Show the minus cursor hint for SEG erase tools; OBD rectangle drawing does not show it when Ctrl is held.
- Inspect every selected OBD or SEG region with its bounding rectangle and eight resize handles.
- Assign new classes distinct random RGB colors; stored and exported colors use normalized `rgba(R, G, B, 1)` values and identify user-selected colors with `-custom`.
- Adjust brush width and opacity with synchronized numeric and slider controls; annotation and eraser previews use the label color at a fixed 40% opacity.
- Assign images to `train`, `test`, unassigned, or `not_used`; use stratified automatic splitting and Retest management.
- Import one selected annotation set through a single JSON preflight dialog that previews classes and SET values, validates the annotation-set type inline, and requires consent for unknown fields. Matching uses exact, case-sensitive display filenames. CLASS supports add-to-empty and replace for CLA, plus append for OBD/SEG; SET supports add-to-empty and replace for all types. Add-to-empty SET requires Unassigned with Retest off. A size mismatch skips that image; an invalid SET preserves its existing status and Retest; an invalid annotation is skipped individually. Other valid rows import in one transaction, with traceable warnings shown on the completed job card. JSON import does not change Undo/Redo history.
- Export one selected annotation set as JSON; export selected or all available images as a flat ZIP archive.
- Keep local database backups and daily diagnostic logs; restore a project safely after a failed replacement.
- Run entirely offline from a portable x64 ZIP, with the main window maximized at startup.

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

The current automated suite contains 291 MSTest tests. GitHub Actions builds, tests, packages, and verifies the portable runtime contents on `windows-2022`.

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
- [Annotation workflow and image import update specification](docs/specs/annotation-workflow-import-update.md)
- [Implementation plan](docs/plans/implementation-plan.md)
- [Test and acceptance plan](docs/plans/test-and-acceptance-plan.md)
- [Architecture decisions](docs/adr/)
- [Third-party notices](licenses/THIRD-PARTY-NOTICES.md)

## License notices

The third-party runtime notices are available in [licenses/THIRD-PARTY-NOTICES.md](licenses/THIRD-PARTY-NOTICES.md) and are included in portable releases.

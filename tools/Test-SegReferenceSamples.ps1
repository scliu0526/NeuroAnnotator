param(
    [Parameter(Mandatory = $true)]
    [string]$LabelsDirectory,
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\src\Neurocle.Domain\bin\x64\Release')
)

$ErrorActionPreference = 'Stop'

function Get-ImageRegion {
    param($Document, [string]$ImageName)
    $image = @($Document.data | Where-Object { $_.fileName -eq $ImageName })
    if ($image.Count -ne 1) { throw "Expected one '$ImageName' entry." }
    return @($image[0].regionLabel)
}

function Read-Sample {
    param([string]$Name)
    $path = Join-Path $LabelsDirectory $Name
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing reference sample: $path" }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function ConvertTo-Path64 {
    param($Region)
    $path = New-Object Clipper2Lib.Path64
    foreach ($point in @($Region.points)) {
        $path.Add([Clipper2Lib.Point64]::new([long]$point[0], [long]$point[1]))
    }
    Write-Output -NoEnumerate $path
}

function ConvertTo-Paths64 {
    param([Clipper2Lib.Path64]$Path)
    $paths = New-Object Clipper2Lib.Paths64
    $paths.Add($Path)
    Write-Output -NoEnumerate $paths
}

function Assert-PositiveArea {
    param([Clipper2Lib.Path64]$Path, [string]$Description)
    $normalised = [Neurocle.Domain.Segmentation.SegGeometryPrototype]::Union((ConvertTo-Paths64 $Path), (New-Object Clipper2Lib.Paths64))
    if ([Neurocle.Domain.Segmentation.SegGeometryPrototype]::Area($normalised) -le 0) {
        throw "$Description does not produce a Non-Zero filled area."
    }
}

function Get-WindingNumber {
    param([Clipper2Lib.Path64]$Path, [double]$X, [double]$Y)
    $winding = 0
    for ($index = 0; $index -lt $Path.Count; $index++) {
        $first = $Path[$index]
        $second = $Path[($index + 1) % $Path.Count]
        $cross = ($second.X - $first.X) * ($Y - $first.Y) - ($X - $first.X) * ($second.Y - $first.Y)
        if ($first.Y -le $Y) {
            if ($second.Y -gt $Y -and $cross -gt 0) { $winding++ }
        }
        elseif ($second.Y -le $Y -and $cross -lt 0) {
            $winding--
        }
    }

    return $winding
}

Add-Type -Path (Join-Path $BuildDirectory 'Clipper2Lib.dll')
Add-Type -Path (Join-Path $BuildDirectory 'Neurocle.Domain.dll')

$base = Read-Sample 'seg_SEG_labeling.json'
$rotated = Read-Sample 'seg_SEG_labeling (1).json'
$erased = Read-Sample 'seg_SEG_labeling (2).json'
$crossing = Read-Sample 'seg_SEG_labeling (3).json'
$loops = Read-Sample 'seg_SEG_labeling (4).json'
$rotatedLoops = Read-Sample 'seg_SEG_labeling (5).json'

if ((Get-ImageRegion $base '11.png')[0].type -ne 'Rect') { throw 'Rectangle sample mismatch.' }
if ((Get-ImageRegion $base '22.png')[0].type -ne 'Ellipse') { throw 'Ellipse sample mismatch.' }
if ((Get-ImageRegion $base '33.png')[0].type -ne 'PolyLine') { throw 'Brush sample mismatch.' }
if ((Get-ImageRegion $base '44.png')[0].type -ne 'PolyGon') { throw 'Closed pen sample mismatch.' }
if ((Get-ImageRegion $base '55.png')[0].type -ne 'PolyLine') { throw 'Line sample mismatch.' }
$rectangle = (Get-ImageRegion $base '11.png')[0]
$ellipse = (Get-ImageRegion $base '22.png')[0]
$rectanglePath = [Neurocle.Domain.Segmentation.SegGeometryPrototype]::Rectangle($rectangle.x, $rectangle.y, $rectangle.x + $rectangle.width, $rectangle.y + $rectangle.height)
$ellipsePath = [Neurocle.Domain.Segmentation.SegGeometryPrototype]::Ellipse($ellipse.x, $ellipse.y, $ellipse.radiusx, $ellipse.radiusy, 64)
if ([Neurocle.Domain.Segmentation.SegGeometryPrototype]::Area((ConvertTo-Paths64 $rectanglePath)) -le 0) { throw 'Rectangle geometry mismatch.' }
if ([Neurocle.Domain.Segmentation.SegGeometryPrototype]::Area((ConvertTo-Paths64 $ellipsePath)) -le 0) { throw 'Ellipse geometry mismatch.' }
foreach ($imageName in '33.png', '55.png') {
    $line = (Get-ImageRegion $base $imageName)[0]
    if ([Neurocle.Domain.Segmentation.SegGeometryPrototype]::Area([Neurocle.Domain.Segmentation.SegGeometryPrototype]::Stroke((ConvertTo-Path64 $line), $line.strokeWidth)) -le 0) { throw "$imageName stroke mismatch." }
}
foreach ($imageName in '11.png', '22.png') {
    $region = (Get-ImageRegion $rotated $imageName)[0]
    if ($region.type -ne 'PolyGon' -or @($region.points).Count -lt 3) { throw "$imageName rotation type mismatch." }
    if ($region.PSObject.Properties['rotation_angle'] -and [double]$region.rotation_angle -ne 0) { throw "$imageName rotation_angle must be zero." }
}
foreach ($imageName in '33.png', '55.png') {
    $region = (Get-ImageRegion $rotated $imageName)[0]
    if ($region.type -ne 'PolyLine' -or @($region.points).Count -lt 2) { throw "$imageName rotated line mismatch." }
    if ($region.PSObject.Properties['rotation_angle'] -and [double]$region.rotation_angle -ne 0) { throw "$imageName rotation_angle must be zero." }
}
$rotatedPolygon = (Get-ImageRegion $rotated '44.png')[0]
$originalPolygon = (Get-ImageRegion $base '44.png')[0]
if ($rotatedPolygon.type -ne 'PolyGon' -or @($rotatedPolygon.points).Count -lt 3) { throw '44.png rotated polygon type mismatch.' }
if (($rotatedPolygon.points | ConvertTo-Json -Compress) -eq ($originalPolygon.points | ConvertTo-Json -Compress)) { throw '44.png rotated polygon points did not change.' }
if ($rotatedPolygon.PSObject.Properties['rotation_angle'] -and [double]$rotatedPolygon.rotation_angle -ne 0) { throw '44.png rotation_angle must be zero.' }
$donut = (Get-ImageRegion $erased '11.png')[0]
if ($donut.type -ne 'PolyGon') { throw 'Erased donut type mismatch.' }
$donutPath = ConvertTo-Path64 $donut
Assert-PositiveArea $donutPath 'Erased donut'
$bridgeVertices = @($donut.points | Group-Object { "$($_[0]),$($_[1])" } | Where-Object Count -gt 1)
if ($bridgeVertices.Count -lt 2) { throw 'Erased donut does not contain a bridge.' }
if ((Get-WindingNumber $donutPath 2300 1200) -ne 0) { throw 'Erased donut central void is not empty under Non-Zero winding.' }
Assert-PositiveArea (ConvertTo-Path64 ((Get-ImageRegion $crossing '66.png')[0])) 'Self-crossing path'
foreach ($region in @(Get-ImageRegion $loops '77.png')) { Assert-PositiveArea (ConvertTo-Path64 $region) 'Multiple-loop path' }
foreach ($region in @(Get-ImageRegion $rotatedLoops '77.png')) { Assert-PositiveArea (ConvertTo-Path64 $region) 'Rotated multiple-loop path' }

Write-Output 'SEG reference geometry verification passed.'

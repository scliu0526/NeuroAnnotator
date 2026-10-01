param(
    [Parameter(Mandatory = $true)]
    [string]$LabelsDirectory
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
if ((Get-ImageRegion $rotated '11.png')[0].type -ne 'PolyGon') { throw 'Rotated rectangle must become PolyGon.' }
if ((Get-ImageRegion $erased '11.png').Count -ne 1) { throw 'Erased donut sample mismatch.' }
if ((Get-ImageRegion $crossing '66.png')[0].type -ne 'PolyGon') { throw 'Self-crossing sample mismatch.' }
if ((Get-ImageRegion $loops '77.png').Count -ne 3) { throw 'Multiple-loop sample mismatch.' }
if ((Get-ImageRegion $rotatedLoops '77.png').Count -ne 3) { throw 'Rotated multiple-loop sample mismatch.' }

Write-Output 'SEG reference sample structure verification passed.'

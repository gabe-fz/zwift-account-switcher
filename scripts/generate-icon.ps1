[CmdletBinding()]
param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'src\ZwiftAccountSwitcher.App\Assets\AppIcon.ico'
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item $outputDirectory -ItemType Directory -Force | Out-Null

function New-RoundedRectanglePath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $scale = $size / 256.0
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $background = New-RoundedRectanglePath (8 * $scale) (8 * $scale) (240 * $scale) (240 * $scale) (48 * $scale)
        try {
            $backgroundBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 34, 37, 42))
            $borderPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 68, 73, 81), [Math]::Max(1, 4 * $scale))
            try {
                $graphics.FillPath($backgroundBrush, $background)
                $graphics.DrawPath($borderPen, $background)
            }
            finally {
                $backgroundBrush.Dispose()
                $borderPen.Dispose()
            }
        }
        finally {
            $background.Dispose()
        }

        $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 245, 247, 250))
        $orange = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 252, 103, 25))
        $arrowPen = [System.Drawing.Pen]::new($orange.Color, [Math]::Max(1.5, 13 * $scale))
        try {
            $arrowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $arrowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

            # Two distinct rider/account silhouettes.
            $graphics.FillEllipse($white, 57 * $scale, 82 * $scale, 50 * $scale, 50 * $scale)
            $graphics.FillEllipse($orange, 149 * $scale, 82 * $scale, 50 * $scale, 50 * $scale)
            $leftBody = New-RoundedRectanglePath (40 * $scale) (139 * $scale) (84 * $scale) (52 * $scale) (26 * $scale)
            $rightBody = New-RoundedRectanglePath (132 * $scale) (139 * $scale) (84 * $scale) (52 * $scale) (26 * $scale)
            try {
                $graphics.FillPath($white, $leftBody)
                $graphics.FillPath($orange, $rightBody)
            }
            finally {
                $leftBody.Dispose()
                $rightBody.Dispose()
            }

            # Opposing arrows communicate switching without copying Zwift branding.
            $graphics.DrawLine($arrowPen, 55 * $scale, 57 * $scale, 194 * $scale, 57 * $scale)
            $topArrow = @(
                [System.Drawing.PointF]::new(194 * $scale, 57 * $scale),
                [System.Drawing.PointF]::new(171 * $scale, 37 * $scale),
                [System.Drawing.PointF]::new(171 * $scale, 77 * $scale)
            )
            $graphics.FillPolygon($orange, $topArrow)

            $graphics.DrawLine($arrowPen, 201 * $scale, 209 * $scale, 62 * $scale, 209 * $scale)
            $bottomArrow = @(
                [System.Drawing.PointF]::new(62 * $scale, 209 * $scale),
                [System.Drawing.PointF]::new(85 * $scale, 189 * $scale),
                [System.Drawing.PointF]::new(85 * $scale, 229 * $scale)
            )
            $graphics.FillPolygon($orange, $bottomArrow)
        }
        finally {
            $white.Dispose()
            $orange.Dispose()
            $arrowPen.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    [PSCustomObject]@{ Size = $size; Bytes = [byte[]](New-IconPng $size) }
}

$stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
        $writer.Write([Byte]$dimension)
        $writer.Write([Byte]$dimension)
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$image.Bytes.Length)
        $writer.Write([UInt32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write([byte[]]$image.Bytes)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Output "Generated multi-resolution application icon: $OutputPath"

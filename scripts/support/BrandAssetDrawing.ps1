function New-AssetDirectory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function ConvertTo-Color {
    param([string]$Hex)

    return [System.Windows.Media.ColorConverter]::ConvertFromString($Hex)
}

function New-SolidBrush {
    param([string]$Hex)

    $brush = [System.Windows.Media.SolidColorBrush]::new((ConvertTo-Color $Hex))
    $brush.Freeze()
    return $brush
}

function New-LinearBrush {
    param(
        [string]$Start,
        [string]$End,
        [double]$Angle = 45
    )

    $brush = [System.Windows.Media.LinearGradientBrush]::new(
        (ConvertTo-Color $Start),
        (ConvertTo-Color $End),
        $Angle
    )
    $brush.Freeze()
    return $brush
}

function New-Pen {
    param(
        [string]$Hex,
        [double]$Width
    )

    $pen = [System.Windows.Media.Pen]::new((New-SolidBrush $Hex), $Width)
    $pen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
    $pen.Freeze()
    return $pen
}

function New-Typeface {
    param(
        [string]$Weight = "Regular",
        [string]$Style = "Normal"
    )

    return [System.Windows.Media.Typeface]::new(
        [System.Windows.Media.FontFamily]::new("Segoe UI"),
        [System.Windows.FontStyles]::$Style,
        [System.Windows.FontWeights]::$Weight,
        [System.Windows.FontStretches]::Normal
    )
}

function Draw-Text {
    param(
        [System.Windows.Media.DrawingContext]$Context,
        [string]$Text,
        [double]$X,
        [double]$Y,
        [double]$Size,
        [string]$Color,
        [double]$MaxWidth,
        [string]$Weight = "Regular",
        [double]$MaxHeight = 0
    )

    $currentSize = $Size
    do {
        $formatted = [System.Windows.Media.FormattedText]::new(
            $Text,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Windows.FlowDirection]::LeftToRight,
            (New-Typeface -Weight $Weight),
            $currentSize,
            (New-SolidBrush $Color),
            1.0
        )
        $formatted.MaxTextWidth = $MaxWidth

        $fitsWidth = $formatted.WidthIncludingTrailingWhitespace -le $MaxWidth
        $fitsHeight = ($MaxHeight -le 0) -or ($formatted.Height -le $MaxHeight)
        if ($fitsWidth -and $fitsHeight) {
            break
        }

        $currentSize -= 2
    } while ($currentSize -ge 12)

    if ($MaxHeight -gt 0) {
        $formatted.MaxTextHeight = $MaxHeight
    }

    $Context.DrawText($formatted, [System.Windows.Point]::new($X, $Y))
}

function Draw-AppIcon {
    param(
        [System.Windows.Media.DrawingContext]$Context,
        [double]$X,
        [double]$Y,
        [double]$Size
    )

    $scale = $Size / 256.0
    $group = [System.Windows.Media.TransformGroup]::new()
    $group.Children.Add([System.Windows.Media.ScaleTransform]::new($scale, $scale))
    $group.Children.Add([System.Windows.Media.TranslateTransform]::new($X, $Y))
    $Context.PushTransform($group)

    $Context.DrawRoundedRectangle(
        (New-LinearBrush $Colors.Navy $Colors.NavyDark 135),
        $null,
        [System.Windows.Rect]::new(0, 0, 256, 256),
        48,
        48
    )

    $docGeometry = [System.Windows.Media.Geometry]::Parse("M78 48h68l34 34v88c0 13-8 21-21 21H78c-13 0-21-8-21-21V69c0-13 8-21 21-21Z")
    $Context.DrawGeometry((New-LinearBrush "#ffffff" "#dfe8f0" 100), $null, $docGeometry)
    $foldGeometry = [System.Windows.Media.Geometry]::Parse("M145 49v31c0 5 4 9 9 9h27")
    $Context.DrawGeometry((New-SolidBrush "#b9c8d6"), $null, $foldGeometry)

    $linePen = New-Pen "#5f7184" 10
    $Context.DrawLine($linePen, [System.Windows.Point]::new(83, 104), [System.Windows.Point]::new(148, 104))
    $Context.DrawLine($linePen, [System.Windows.Point]::new(83, 128), [System.Windows.Point]::new(135, 128))
    $Context.DrawLine($linePen, [System.Windows.Point]::new(83, 152), [System.Windows.Point]::new(125, 152))

    $lensPen = New-Pen $Colors.Teal 16
    $Context.DrawEllipse($null, $lensPen, [System.Windows.Point]::new(157, 153), 34, 34)
    $Context.DrawLine((New-Pen $Colors.Amber 18), [System.Windows.Point]::new(182, 179), [System.Windows.Point]::new(207, 204))
    $Context.DrawEllipse((New-SolidBrush $Colors.Amber), $null, [System.Windows.Point]::new(93, 103), 6, 6)
    $Context.DrawEllipse((New-SolidBrush $Colors.Teal), $null, [System.Windows.Point]::new(128, 103), 6, 6)
    $Context.DrawEllipse((New-SolidBrush $Colors.Teal), $null, [System.Windows.Point]::new(145, 151), 5, 5)

    $Context.Pop()
}

function Draw-DocumentVisual {
    param(
        [System.Windows.Media.DrawingContext]$Context,
        [double]$X,
        [double]$Y,
        [double]$Width,
        [double]$Height
    )

    $Context.DrawRoundedRectangle(
        (New-SolidBrush "#ffffff"),
        (New-Pen $Colors.Line 2),
        [System.Windows.Rect]::new($X, $Y, $Width, $Height),
        18,
        18
    )
    $Context.DrawRoundedRectangle(
        (New-SolidBrush "#e7f0f7"),
        $null,
        [System.Windows.Rect]::new($X + 28, $Y + 32, $Width - 56, 22),
        8,
        8
    )

    for ($i = 0; $i -lt 5; $i++) {
        $lineWidth = ($Width - 78) - (($i % 3) * 35)
        $lineY = $Y + 82 + ($i * 32)
        $Context.DrawRoundedRectangle(
            (New-SolidBrush "#d9e0e8"),
            $null,
            [System.Windows.Rect]::new($X + 34, $lineY, $lineWidth, 12),
            6,
            6
        )
    }

    $Context.DrawEllipse($null, (New-Pen $Colors.Teal 12), [System.Windows.Point]::new($X + $Width - 82, $Y + $Height - 78), 34, 34)
    $Context.DrawLine((New-Pen $Colors.Amber 13), [System.Windows.Point]::new($X + $Width - 58, $Y + $Height - 54), [System.Windows.Point]::new($X + $Width - 30, $Y + $Height - 26))
}

function Draw-SocialBackground {
    param(
        [System.Windows.Media.DrawingContext]$Context,
        [int]$Width,
        [int]$Height
    )

    $Context.DrawRectangle((New-SolidBrush $Colors.Surface), $null, [System.Windows.Rect]::new(0, 0, $Width, $Height))
    $Context.DrawRectangle((New-LinearBrush $Colors.Navy $Colors.NavyDark 135), $null, [System.Windows.Rect]::new(0, 0, $Width, [Math]::Max(180, $Height * 0.31)))
    $Context.DrawRoundedRectangle(
        (New-SolidBrush "#ffffff"),
        $null,
        [System.Windows.Rect]::new($Width * 0.58, $Height * 0.22, $Width * 0.30, $Height * 0.45),
        22,
        22
    )
    Draw-DocumentVisual -Context $Context -X ($Width * 0.61) -Y ($Height * 0.255) -Width ($Width * 0.24) -Height ($Height * 0.35)
    $Context.DrawEllipse((New-SolidBrush $Colors.Teal), $null, [System.Windows.Point]::new($Width * 0.87, $Height * 0.18), 16, 16)
    $Context.DrawEllipse((New-SolidBrush $Colors.Amber), $null, [System.Windows.Point]::new($Width * 0.91, $Height * 0.72), 12, 12)
}

function Save-RasterAsset {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height,
        [scriptblock]$Draw,
        [object[]]$DrawArguments = @(),
        [ValidateSet("Png", "Bmp")]
        [string]$Format = "Png"
    )

    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    & $Draw $context $Width $Height @DrawArguments
    $context.Close()

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $Width,
        $Height,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Pbgra32
    )
    $bitmap.Render($visual)

    if ($Format -eq "Bmp") {
        $encoder = [System.Windows.Media.Imaging.BmpBitmapEncoder]::new()
    }
    else {
        $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    }

    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.File]::Create($Path)
    try {
        $encoder.Save($stream)
    }
    finally {
        $stream.Dispose()
    }
}

function Save-LogoIconPng {
    param(
        [string]$Path,
        [int]$Size
    )

    Save-RasterAsset -Path $Path -Width $Size -Height $Size -Draw {
        param($context, $width, $height)
        Draw-AppIcon -Context $context -X 0 -Y 0 -Size ([Math]::Min($width, $height))
    }
}

function Save-HorizontalLogoPng {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height
    )

    Save-RasterAsset -Path $Path -Width $Width -Height $Height -Draw {
        param($context, $width, $height)
        $context.DrawRectangle((New-SolidBrush "#ffffff"), $null, [System.Windows.Rect]::new(0, 0, $width, $height))
        $iconSize = [Math]::Min($height * 0.56, $width * 0.22)
        Draw-AppIcon -Context $context -X ($height * 0.22) -Y (($height - $iconSize) / 2) -Size $iconSize
        Draw-Text -Context $context -Text "OnlyRag" -X ($height * 0.22 + $iconSize + 34) -Y ($height * 0.28) -Size ($height * 0.22) -Color $Colors.Ink -MaxWidth ($width * 0.6) -Weight "Bold"
        Draw-Text -Context $context -Text "Local-first document RAG for Windows" -X ($height * 0.22 + $iconSize + 38) -Y ($height * 0.53) -Size ($height * 0.075) -Color $Colors.Muted -MaxWidth ($width * 0.6) -Weight "SemiBold"
    }
}

function Save-SocialImage {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height,
        [string]$Headline,
        [string]$Subtitle
    )

    Save-RasterAsset -Path $Path -Width $Width -Height $Height -Draw {
        param($context, $width, $height, $headline, $subtitle)
        Draw-SocialBackground -Context $context -Width $width -Height $height
        $iconSize = [Math]::Min($width, $height) * 0.13
        Draw-AppIcon -Context $context -X ($width * 0.08) -Y ($height * 0.10) -Size $iconSize
        Draw-Text -Context $context -Text "OnlyRag" -X ($width * 0.08 + $iconSize + 24) -Y ($height * 0.13) -Size ($height * 0.06) -Color "#ffffff" -MaxWidth ($width * 0.44) -Weight "Bold"
        Draw-Text -Context $context -Text $headline -X ($width * 0.08) -Y ($height * 0.36) -Size ($height * 0.105) -Color $Colors.Ink -MaxWidth ($width * 0.48) -Weight "Bold" -MaxHeight ($height * 0.32)
        Draw-Text -Context $context -Text $subtitle -X ($width * 0.08) -Y ($height * 0.69) -Size ($height * 0.04) -Color $Colors.Muted -MaxWidth ($width * 0.50) -Weight "SemiBold" -MaxHeight ($height * 0.15)
        $pillWidth = [Math]::Min($width * 0.34, 330)
        $pillHeight = [Math]::Max($height * 0.07, 46)
        $context.DrawRoundedRectangle((New-SolidBrush $Colors.Blue), $null, [System.Windows.Rect]::new($width * 0.08, $height * 0.84, $pillWidth, $pillHeight), 12, 12)
        Draw-Text -Context $context -Text "Windows desktop app" -X ($width * 0.10) -Y ($height * 0.852) -Size ($height * 0.031) -Color "#ffffff" -MaxWidth ($pillWidth - 36) -Weight "Bold"
    } -DrawArguments @($Headline, $Subtitle)
}

function Save-PostImage {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height,
        [string]$Label,
        [string]$Headline,
        [string]$Subtitle
    )

    Save-RasterAsset -Path $Path -Width $Width -Height $Height -Draw {
        param($context, $width, $height, $label, $headline, $subtitle)
        $context.DrawRectangle((New-SolidBrush "#ffffff"), $null, [System.Windows.Rect]::new(0, 0, $width, $height))
        $context.DrawRectangle((New-SolidBrush $Colors.Surface), $null, [System.Windows.Rect]::new(0, $height * 0.67, $width, $height * 0.33))
        $context.DrawRoundedRectangle((New-LinearBrush $Colors.Navy $Colors.NavyDark 135), $null, [System.Windows.Rect]::new($width * 0.08, $height * 0.08, $width * 0.84, $height * 0.33), 28, 28)
        Draw-AppIcon -Context $context -X ($width * 0.12) -Y ($height * 0.13) -Size ([Math]::Min($width, $height) * 0.14)
        Draw-Text -Context $context -Text $label -X ($width * 0.29) -Y ($height * 0.16) -Size ($height * 0.035) -Color $Colors.Teal -MaxWidth ($width * 0.55) -Weight "Bold"
        Draw-Text -Context $context -Text "OnlyRag" -X ($width * 0.29) -Y ($height * 0.22) -Size ($height * 0.065) -Color "#ffffff" -MaxWidth ($width * 0.55) -Weight "Bold"
        Draw-Text -Context $context -Text $headline -X ($width * 0.10) -Y ($height * 0.49) -Size ($height * 0.062) -Color $Colors.Ink -MaxWidth ($width * 0.80) -Weight "Bold" -MaxHeight ($height * 0.22)
        Draw-Text -Context $context -Text $subtitle -X ($width * 0.10) -Y ($height * 0.75) -Size ($height * 0.034) -Color $Colors.Muted -MaxWidth ($width * 0.76) -Weight "SemiBold" -MaxHeight ($height * 0.14)
        $context.DrawRoundedRectangle((New-SolidBrush $Colors.Amber), $null, [System.Windows.Rect]::new($width * 0.10, $height * 0.91, $width * 0.28, $height * 0.018), 8, 8)
        $context.DrawRoundedRectangle((New-SolidBrush $Colors.Teal), $null, [System.Windows.Rect]::new($width * 0.40, $height * 0.91, $width * 0.16, $height * 0.018), 8, 8)
    } -DrawArguments @($Label, $Headline, $Subtitle)
}

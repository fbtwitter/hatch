using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Svg;

var logoPath = args.Length > 0 ? args[0] : "logo.svg";
var outDir   = args.Length > 1 ? args[1] : "Assets";

Directory.CreateDirectory(outDir);

var svg = SvgDocument.Open(logoPath);

void Save(string name, int canvasW, int canvasH, bool whiteBg = false)
{
    using var canvas = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(canvas);

    g.Clear(whiteBg ? Color.White : Color.Transparent);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.SmoothingMode     = SmoothingMode.AntiAlias;

    // Fit the square SVG into the canvas with 8% padding on each side.
    int logoSize = (int)(Math.Min(canvasW, canvasH) * 0.84);
    int offsetX  = (canvasW - logoSize) / 2;
    int offsetY  = (canvasH - logoSize) / 2;

    using var logo = svg.Draw(logoSize, logoSize);
    g.DrawImage(logo, offsetX, offsetY, logoSize, logoSize);

    canvas.Save(Path.Combine(outDir, name), ImageFormat.Png);
    Console.WriteLine($"  {name} ({canvasW}x{canvasH})");
}

Console.WriteLine("Generating Hatch assets...");
Save("Square44x44Logo.png",    44,  44);
Save("Square150x150Logo.png", 150, 150);
Save("Wide310x150Logo.png",   310, 150);
Save("StoreLogo.png",          50,  50);
Save("SplashScreen.png",      620, 300, whiteBg: true);
Console.WriteLine("Done.");

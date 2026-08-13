using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Svg;

var logoPath = args.Length > 0 ? args[0] : "logo.svg";
var outDir   = args.Length > 1 ? args[1] : "Assets";

Directory.CreateDirectory(outDir);

var svg = SvgDocument.Open(logoPath);

// Render the SVG into a transparent bitmap at the given size.
// paddingFraction: fraction of each side left empty (0 = fill entire canvas).
Bitmap RenderSvg(int w, int h, float paddingFraction = 0f, bool whiteBg = false)
{
    var canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(canvas);
    g.Clear(whiteBg ? Color.White : Color.Transparent);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.SmoothingMode     = SmoothingMode.AntiAlias;

    int logoW   = (int)(w * (1f - paddingFraction * 2));
    int logoH   = (int)(h * (1f - paddingFraction * 2));
    int offsetX = (w - logoW) / 2;
    int offsetY = (h - logoH) / 2;

    using var logo = svg.Draw(logoW, logoH);
    g.DrawImage(logo, offsetX, offsetY, logoW, logoH);
    return canvas;
}

void Save(string name, int canvasW, int canvasH, float padding = 0f, bool whiteBg = false, string? targetDir = null)
{
    var dir = targetDir ?? outDir;
    Directory.CreateDirectory(dir);
    using var bmp = RenderSvg(canvasW, canvasH, padding, whiteBg);
    bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
    Console.WriteLine($"  {name} ({canvasW}x{canvasH})");
}

// Returns a greyscale + reduced-opacity copy of src. Caller owns the returned Bitmap.
Bitmap ToGreyscale(Bitmap src, float opacity = 0.45f)
{
    var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(dst);
    var cm = new System.Drawing.Imaging.ColorMatrix(new float[][]
    {
        [0.299f, 0.299f, 0.299f, 0,       0],
        [0.587f, 0.587f, 0.587f, 0,       0],
        [0.114f, 0.114f, 0.114f, 0,       0],
        [0,      0,      0,      opacity, 0],
        [0,      0,      0,      0,       1],
    });
    var ia = new System.Drawing.Imaging.ImageAttributes();
    ia.SetColorMatrix(cm);
    g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height),
        0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
    return dst;
}

// Build a multi-resolution ICO using PNG-in-ICO (supported on Windows Vista+).
// Each entry is a full PNG blob embedded directly in the ICO container.
// bitmapFactory: given a size, returns the Bitmap to encode (caller disposes it).
void SaveIco(string name, int[] sizes, Func<int, Bitmap>? bitmapFactory = null)
{
    bitmapFactory ??= size => RenderSvg(size, size);

    var entries = new List<(int size, byte[] pngData)>();
    foreach (var size in sizes)
    {
        using var bmp = bitmapFactory(size);
        using var ms  = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        entries.Add((size, ms.ToArray()));
    }

    var icoPath = Path.Combine(outDir, name);
    using var fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write);
    using var bw = new BinaryWriter(fs);

    // ICONDIR header (6 bytes)
    bw.Write((short)0);                    // reserved
    bw.Write((short)1);                    // type = ICO
    bw.Write((short)entries.Count);

    // ICONDIRENTRY array — each 16 bytes
    int dataOffset = 6 + entries.Count * 16;
    foreach (var (size, data) in entries)
    {
        bw.Write((byte)(size >= 256 ? 0 : size)); // width  (0 encodes 256)
        bw.Write((byte)(size >= 256 ? 0 : size)); // height
        bw.Write((byte)0);    // colour count (0 = no palette)
        bw.Write((byte)0);    // reserved
        bw.Write((short)1);   // colour planes
        bw.Write((short)32);  // bits per pixel
        bw.Write(data.Length);
        bw.Write(dataOffset);
        dataOffset += data.Length;
    }

    // Image data blobs
    foreach (var (_, data) in entries)
        bw.Write(data);

    Console.WriteLine($"  {name} ({string.Join(", ", sizes.Select(s => $"{s}px"))})");
}

Console.WriteLine("Generating Hatch assets...");
// PNG assets — larger sizes for high-DPI displays
Save("Square44x44Logo.png",    88,  88);
Save("Square150x150Logo.png", 300, 300);

// Square44x44Logo targetsize variants — required for a transparent (no plate) taskbar icon.
// The _altform-unplated variants are what Windows uses for the taskbar button when
// BackgroundColor="transparent" is set in the manifest. Without them Windows falls back to
// applying a coloured backing plate even though the manifest requests transparency.
int[] targetSizes = [16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256];
foreach (var size in targetSizes)
{
    Save($"Square44x44Logo.targetsize-{size}.png", size, size);
    Save($"Square44x44Logo.targetsize-{size}_altform-unplated.png", size, size);
}
Save("Wide310x150Logo.png",   620, 300);
Save("StoreLogo.png",         100, 100);
// Splash screen keeps a little breathing room on a white background
Save("SplashScreen.png",     1240, 600, padding: 0.15f, whiteBg: true);
// Multi-resolution ICO for taskbar / title bar / Alt+Tab switcher
SaveIco("Hatch.ico", [16, 24, 32, 48, 64, 128, 256]);
// Greyed-out variant shown in the tray when the mascot is hidden
SaveIco("HatchHidden.ico", [16, 24, 32, 48, 64, 128, 256], size =>
{
    using var normal = RenderSvg(size, size);
    return ToGreyscale(normal);
});
// Store listing submission images (Partner Center) — not part of the MSIX package, so
// these render to docs/store-assets instead of Assets. Re-run manually if logo.svg changes.
var storeAssetsDir = Path.Combine(outDir, "..", "..", "docs", "store-assets");
Save("StoreLogo-300.png", 300, 300, targetDir: storeAssetsDir);
Save("StoreLogo-150.png", 150, 150, targetDir: storeAssetsDir);
Save("StoreLogo-71.png",   71,  71, targetDir: storeAssetsDir);
Console.WriteLine("Done.");

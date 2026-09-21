using System.Drawing;

namespace Hatch.Helpers;

public static class MascotPopupPlacement
{
    // All inputs and the result are physical screen pixels, including negative monitor origins.
    public static Rectangle Place(Rectangle workArea, Rectangle mascot, Size desired, int gap,
        bool preferAbove = false)
    {
        gap = Math.Clamp(gap, 0, Math.Max(0, (Math.Min(workArea.Width, workArea.Height) - 1) / 2));
        var bounds = Rectangle.Inflate(workArea, -gap, -gap);
        int width = Math.Clamp(desired.Width, 1, Math.Max(1, bounds.Width));
        int height = Math.Clamp(desired.Height, 1, Math.Max(1, bounds.Height));
        int left = Math.Clamp(mascot.Left - gap, bounds.Left, bounds.Right);
        int right = Math.Clamp(mascot.Right + gap, bounds.Left, bounds.Right);
        int top = Math.Clamp(mascot.Top - gap, bounds.Top, bounds.Bottom);
        int bottom = Math.Clamp(mascot.Bottom + gap, bounds.Top, bounds.Bottom);
        Span<Rectangle> regions = stackalloc Rectangle[4];
        regions[preferAbove ? 2 : 0] = Rectangle.FromLTRB(bounds.Left, bounds.Top, left, bounds.Bottom);
        regions[preferAbove ? 3 : 1] = Rectangle.FromLTRB(right, bounds.Top, bounds.Right, bounds.Bottom);
        regions[preferAbove ? 0 : 2] = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, top);
        regions[preferAbove ? 1 : 3] = Rectangle.FromLTRB(bounds.Left, bottom, bounds.Right, bounds.Bottom);

        var best = bounds;
        long bestArea = 0;
        foreach (var region in regions)
        {
            long area = (long)Math.Min(width, region.Width) * Math.Min(height, region.Height);
            if (area <= bestArea) continue;
            best = region;
            bestArea = area;
            if (region.Width >= width && region.Height >= height) break;
        }

        width = Math.Min(width, best.Width);
        height = Math.Min(height, best.Height);
        int x = Math.Clamp(mascot.Left + (mascot.Width - width) / 2, best.Left, best.Right - width);
        int y = Math.Clamp(mascot.Top + (mascot.Height - height) / 2, best.Top, best.Bottom - height);
        return new Rectangle(x, y, width, height);
    }
}

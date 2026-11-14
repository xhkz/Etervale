namespace PristonTale.Engine3D.Rendering;

// A set of higher-resolution copies of original images, made offline at these densities.
public sealed record HiResArtSet(string Name, int[] Densities);

// Higher-resolution copies of original images, made offline (tools/PristonTale.HudUpscale) and kept under the asset root:
// <root>/<set>/HiRes/<density>x/<original asset path>.png, straight alpha, density times the original's width and height.
// The HUD draws a copy in the original's place, one image pixel per screen pixel at that HUD scale; the world loads the
// 2x copy of a texture when the setting asks for it. Without a copy, the original is used.
public static class HiResArt
{
    public const int MaximumHudDensity = 4;
    public const int WorldDensity = 2;

    public static readonly HiResArtSet Hud = new("Hud", [2, 3, MaximumHudDensity]);
    public static readonly HiResArtSet World = new("World", [WorldDensity]);

    public static string PathOf(HiResArtSet set, string assetPath, int density) =>
        $"{set.Name}/HiRes/{density}x/{assetPath.Replace('\\', '/')}.png";

    // The copy to use at a HUD scale: the scale itself, up to the largest density made (1: the original).
    public static int HudDensityFor(int hudScale) => Math.Clamp(hudScale, 1, MaximumHudDensity);

    // A coverage plane (one byte per pixel) scaled by factor with bilinear filtering, pixel centres aligned.
    public static byte[] UpsampleCoverage(byte[] coverage, int width, int height, int factor)
    {
        var outWidth = width * factor;
        var outHeight = height * factor;
        var result = new byte[outWidth * outHeight];
        for (var y = 0; y < outHeight; y++)
        {
            var sy = Math.Clamp((y + 0.5f) / factor - 0.5f, 0f, height - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, height - 1);
            var fy = sy - y0;
            for (var x = 0; x < outWidth; x++)
            {
                var sx = Math.Clamp((x + 0.5f) / factor - 0.5f, 0f, width - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, width - 1);
                var fx = sx - x0;
                var top = coverage[y0 * width + x0] * (1 - fx) + coverage[y0 * width + x1] * fx;
                var bottom = coverage[y1 * width + x0] * (1 - fx) + coverage[y1 * width + x1] * fx;
                result[y * outWidth + x] = (byte)MathF.Round(top * (1 - fy) + bottom * fy);
            }
        }

        return result;
    }
}

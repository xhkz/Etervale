namespace PristonTale.Engine3D.Rendering;

// Higher-resolution copies of the original interface images, made offline (tools/PristonTale.HudUpscale) and kept
// under the asset root: <root>/Hud/HiRes/<density>x/<original asset path>.png, straight alpha, density times the
// original's width and height. The HUD draws a copy in the original's place, one image pixel per screen pixel at that
// HUD scale; without a copy it draws the original.
public static class HudHiResArt
{
    public const int MaximumDensity = 4;

    public static readonly int[] Densities = [2, 3, 4];

    public static string PathOf(string assetPath, int density) => $"Hud/HiRes/{density}x/{assetPath.Replace('\\', '/')}.png";

    // The copy to use at a HUD scale: the scale itself, up to the largest density made (1: the original).
    public static int DensityFor(int hudScale) => Math.Clamp(hudScale, 1, MaximumDensity);
}

using PristonTale.Engine3D.Rendering;
using PristonTale.HudUpscale;

namespace PristonTale.Tests;

internal static class HudHiResArtTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a HUD scale reads the copy of its own density, up to 4x, from its path under Hud/HiRes", DensityAsync);
        yield return ("the upscaler's input takes covered colour into transparent pixels and is opaque", BleedAsync);
        yield return ("an upscaled copy keeps the original's coverage, scaled bilinearly", CoverageAsync);
        yield return ("a smaller copy averages its area weighted by coverage", ShrinkAsync);
    }

    private static Task DensityAsync()
    {
        Assert.True(HudHiResArt.DensityFor(0) == 1 && HudHiResArt.DensityFor(1) == 1 && HudHiResArt.DensityFor(2) == 2 && HudHiResArt.DensityFor(3) == 3
            && HudHiResArt.DensityFor(6) == HudHiResArt.MaximumDensity, "a HUD scale chose the wrong copy");
        Assert.True(HudHiResArt.PathOf("image\\Sinimage\\Inter\\Exit.bmp", 2) == "Hud/HiRes/2x/image/Sinimage/Inter/Exit.bmp.png", "a copy's path is wrong");
        Assert.True(HudHiResArt.Densities.Contains(HudHiResArt.MaximumDensity), "the largest density is not made");
        return Task.CompletedTask;
    }

    private static Task BleedAsync()
    {
        byte[] row = [200, 10, 20, 255, 0, 0, 0, 0, 0, 0, 0, 0];
        var bled = HiResPixels.Bleed(row, 3, 1);
        Assert.True(bled[4] == 200 && bled[8] == 200 && bled[9] == 10 && bled[10] == 20, "a transparent pixel did not take its covered neighbour's colour");
        Assert.True(bled[3] == 255 && bled[7] == 255 && bled[11] == 255, "the upscaler's input is not opaque");
        var empty = HiResPixels.Bleed([9, 9, 9, 0], 1, 1);
        Assert.True(empty[0] == 0 && empty[3] == 255, "an image with no coverage did not become opaque black");
        return Task.CompletedTask;
    }

    private static Task CoverageAsync()
    {
        byte[] original = [0, 0, 0, 255, 0, 0, 0, 0];
        var upscaled = new byte[4 * 2 * 4];
        Array.Fill(upscaled, (byte)255);
        var result = HiResPixels.WithCoverage(upscaled, original, 2, 1, 2);
        Assert.True(result[3] == 255 && result[7] == 191 && result[11] == 64 && result[15] == 0,
            $"the coverage is {result[3]}, {result[7]}, {result[11]}, {result[15]}");
        Assert.True(result[0] == 255 && result[12] == 255, "the upscaled colour changed");
        Assert.Throws<ArgumentException>(() => HiResPixels.WithCoverage(new byte[4], original, 2, 1, 2), "an upscaled image of the wrong size was accepted");
        return Task.CompletedTask;
    }

    private static Task ShrinkAsync()
    {
        byte[] square = [250, 240, 230, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var one = HiResPixels.Shrink(square, 2, 2, 1, 1);
        Assert.True(one[0] == 250 && one[1] == 240 && one[2] == 230 && one[3] == 64, $"the averaged pixel is {one[0]}, {one[1]}, {one[2]}, {one[3]}");

        var solid = new byte[4 * 4 * 4];
        for (var index = 0; index < 16; index++)
        {
            (solid[index * 4], solid[index * 4 + 1], solid[index * 4 + 2], solid[index * 4 + 3]) = (12, 34, 56, 255);
        }

        var three = HiResPixels.Shrink(solid, 4, 4, 3, 3);
        Assert.True(three.Chunk(4).All(pixel => pixel is [12, 34, 56, 255]), "a 4 to 3 shrink changed a uniform colour");
        return Task.CompletedTask;
    }
}

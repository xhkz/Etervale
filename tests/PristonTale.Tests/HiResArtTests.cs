using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.HudUpscale;

namespace PristonTale.Tests;

internal static class HiResArtTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a HUD scale reads the copy of its own density, up to 4x, from its path under Hud/HiRes", DensityAsync);
        yield return ("the upscaler's input takes covered colour into transparent pixels and is opaque", BleedAsync);
        yield return ("an upscaled copy keeps the original's coverage, scaled bilinearly", CoverageAsync);
        yield return ("a smaller copy averages its area weighted by coverage", ShrinkAsync);
        yield return ("a world texture's 2x copy is read from its path under World/HiRes", WorldPathAsync);
        yield return ("a tiling texture is padded with its opposite edges and cropped back", WrapAsync);
        yield return ("a world texture's copy takes the original's coverage, or stays opaque", WorldCoverageAsync);
    }

    private static Task DensityAsync()
    {
        Assert.True(HiResArt.HudDensityFor(0) == 1 && HiResArt.HudDensityFor(1) == 1 && HiResArt.HudDensityFor(2) == 2 && HiResArt.HudDensityFor(3) == 3
            && HiResArt.HudDensityFor(6) == HiResArt.MaximumHudDensity, "a HUD scale chose the wrong copy");
        Assert.True(HiResArt.PathOf(HiResArt.Hud, "image\\Sinimage\\Inter\\Exit.bmp", 2) == "Hud/HiRes/2x/image/Sinimage/Inter/Exit.bmp.png", "a copy's path is wrong");
        Assert.True(HiResArt.Hud.Densities.Contains(HiResArt.MaximumHudDensity), "the largest density is not made");
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

    private static Task WorldPathAsync()
    {
        Assert.True(HiResArt.PathOf(HiResArt.World, "Field\\forest\\fo_tree.bmp", HiResArt.WorldDensity) == "World/HiRes/2x/Field/forest/fo_tree.bmp.png",
            "a world copy's path is wrong");
        Assert.True(HiResArt.World.Densities is [HiResArt.WorldDensity], "the world copies are not made at the density the loader reads");
        return Task.CompletedTask;
    }

    private static Task WrapAsync()
    {
        byte[] pair = [10, 0, 0, 255, 20, 0, 0, 255];
        var padded = HiResPixels.WrapPad(pair, 2, 1, 1);
        Assert.True(padded.Length == 4 * 3 * 4, "the padded size is wrong");
        Assert.True(padded[0] == 20 && padded[4] == 10 && padded[8] == 20 && padded[12] == 10, "the left and right margins are not the opposite edges");
        Assert.True(padded[16] == 20 && padded[20] == 10 && padded[40] == 20, "the top and bottom margins are not the opposite edges");
        var cropped = HiResPixels.Crop(padded, 4, 1, 1, 2, 1);
        Assert.True(cropped.SequenceEqual(pair), "cropping the margin did not give the original back");
        return Task.CompletedTask;
    }

    private static Task WorldCoverageAsync()
    {
        var original = new DecodedPristonImage(2, 1, [new Color(0, 0, 0, 255), new Color(0, 0, 0, 0)], true);
        var copy = new DecodedPristonImage(4, 2, Enumerable.Repeat(new Color(9, 8, 7, 100), 8).ToArray(), true);
        var covered = PristonTextureLoader.WithCoverageOf(copy, original, covered: true);
        Assert.True(covered.Pixels[0].A == 255 && covered.Pixels[1].A == 191 && covered.Pixels[2].A == 64 && covered.Pixels[3].A == 0,
            $"the copy's coverage is {covered.Pixels[0].A}, {covered.Pixels[1].A}, {covered.Pixels[2].A}, {covered.Pixels[3].A}");
        Assert.True(covered.Pixels[0].R == 9 && covered.HasSourceAlpha, "the copy's colour or alpha source changed");

        var opaque = new DecodedPristonImage(4, 2, Enumerable.Repeat(new Color(9, 8, 7, 100), 8).ToArray(), true);
        var bitmap = original with { HasSourceAlpha = false };
        var kept = PristonTextureLoader.WithCoverageOf(opaque, bitmap, covered: false);
        Assert.True(kept.Pixels.All(pixel => pixel.A == 255) && !kept.HasSourceAlpha, "an opaque original's copy is not opaque, or takes an opacity map no more");
        return Task.CompletedTask;
    }
}

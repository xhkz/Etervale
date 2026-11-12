using System.Diagnostics;
using System.Globalization;
using PristonTale.Assets;
using PristonTale.Engine3D.Rendering;
using PristonTale.GltfExport;

namespace PristonTale.HudUpscale;

// The three steps: prepare writes each interface image as an opaque colour PNG named by its number in the work folder
// (the upscaler reads a flat folder), with manifest.tsv naming the asset path of each; run upscales the folder 4x; collect
// puts each original's coverage back on its result and writes the 4x, 3x and 2x copies where the HUD reads them.
internal static class HudUpscaler
{
    public const int Factor = 4;
    public const string DefaultModel = "realesrgan-x4plus";

    // Every interface image under image/ except the item icons (drawn by the icon cache, not from these copies).
    public static readonly string[] DefaultFolders = ["image"];
    private static readonly string[] Excluded = ["image/Sinimage/Items"];
    private static readonly string[] Extensions = [".bmp", ".tga", ".png", ".jpg", ".jpeg"];

    private const int CannotStart = 126;
    private const string Manifest = "manifest.tsv";
    private const string Input = "in";
    private const string Output = "out";

    public static int Prepare(string assetRoot, string work, IReadOnlyList<string> folders, TextWriter log)
    {
        var input = Path.Combine(work, Input);
        Directory.CreateDirectory(input);
        var lines = new List<string>();
        foreach (var path in Images(assetRoot, folders))
        {
            if (Original(assetRoot, path) is not { } original)
            {
                log.WriteLine($"skipped (unreadable): {path}");
                continue;
            }

            var name = lines.Count.ToString("D5", CultureInfo.InvariantCulture);
            PngWriter.Write(Path.Combine(input, name + ".png"), original.Width, original.Height, HiResPixels.Bleed(original.Rgba, original.Width, original.Height));
            lines.Add($"{name}\t{path}");
        }

        File.WriteAllLines(Path.Combine(work, Manifest), lines);
        log.WriteLine($"prepared {lines.Count} images in {input}");
        return lines.Count;
    }

    public static int Run(string esrgan, string work, string model, TextWriter log)
    {
        var output = Path.Combine(work, Output);
        Directory.CreateDirectory(output);
        var models = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(esrgan)) ?? ".", "models");
        var start = new ProcessStartInfo(esrgan)
        {
            ArgumentList = { "-i", Path.Combine(work, Input), "-o", output, "-n", model, "-s", Factor.ToString(CultureInfo.InvariantCulture), "-f", "png", "-m", models },
            UseShellExecute = false
        };
        log.WriteLine($"running {esrgan} with {model}");
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException($"{esrgan} did not start");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            // Typically a binary unzipped without its execute permission (chmod +x) or blocked by the system.
            log.WriteLine($"{esrgan} could not start: {exception.Message}");
            return CannotStart;
        }
    }

    // Returns the number of images whose upscaled result was missing or the wrong size.
    public static int Collect(string assetRoot, string work, TextWriter log)
    {
        var failed = 0;
        var written = 0;
        foreach (var line in File.ReadAllLines(Path.Combine(work, Manifest)))
        {
            var (name, path) = (line[..line.IndexOf('\t')], line[(line.IndexOf('\t') + 1)..]);
            var result = Path.Combine(work, Output, name + ".png");
            if (Original(assetRoot, path) is not { } original || !File.Exists(result))
            {
                log.WriteLine($"missing result: {path}");
                failed++;
                continue;
            }

            var upscaled = PristonImageDecoder.Decode(File.ReadAllBytes(result), ".png");
            if (upscaled.Width != original.Width * Factor || upscaled.Height != original.Height * Factor)
            {
                log.WriteLine($"wrong size {upscaled.Width}x{upscaled.Height}: {path}");
                failed++;
                continue;
            }

            var full = HiResPixels.WithCoverage(Bytes(upscaled.Pixels), original.Rgba, original.Width, original.Height, Factor);
            foreach (var density in HudHiResArt.Densities)
            {
                var (width, height) = (original.Width * density, original.Height * density);
                var copy = density == Factor ? full : HiResPixels.Shrink(full, original.Width * Factor, original.Height * Factor, width, height);
                var file = AssetFiles.Combine(assetRoot, HudHiResArt.PathOf(path, density));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                PngWriter.Write(file, width, height, copy);
            }

            written++;
        }

        log.WriteLine($"wrote {written} images x{HudHiResArt.Densities.Length} densities; {failed} failed");
        return failed;
    }

    private sealed record Image(int Width, int Height, byte[] Rgba);

    // The original as the HUD sees it (colour key applied), in straight alpha.
    private static Image? Original(string assetRoot, string path)
    {
        var file = AssetFiles.Combine(assetRoot, path);
        DecodedPristonImage decoded;
        try
        {
            decoded = PristonImageDecoder.Decode(AssetFiles.ReadAllBytes(file), Path.GetExtension(file), bmpCoverage: true);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            return null;
        }

        if (!decoded.IsValid)
        {
            return null;
        }

        PristonUiPixels.Prepare(decoded, PristonUiPixels.InterfaceKeyFor(Path.GetExtension(file)));
        var rgba = new byte[decoded.Pixels.Length * 4];
        for (var index = 0; index < decoded.Pixels.Length; index++)
        {
            var color = decoded.Pixels[index];
            var alpha = color.A;
            rgba[index * 4] = Straight(color.R, alpha);
            rgba[index * 4 + 1] = Straight(color.G, alpha);
            rgba[index * 4 + 2] = Straight(color.B, alpha);
            rgba[index * 4 + 3] = alpha;
        }

        return new Image(decoded.Width, decoded.Height, rgba);
    }

    private static byte Straight(byte premultiplied, byte alpha) => alpha == 0 ? (byte)0 : (byte)Math.Min(255, (premultiplied * 255 + alpha / 2) / alpha);

    private static byte[] Bytes(Microsoft.Xna.Framework.Color[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];
        for (var index = 0; index < pixels.Length; index++)
        {
            (bytes[index * 4], bytes[index * 4 + 1], bytes[index * 4 + 2], bytes[index * 4 + 3]) = (pixels[index].R, pixels[index].G, pixels[index].B, pixels[index].A);
        }

        return bytes;
    }

    // Asset-relative paths with '/' separators, sorted, so a manifest is the same from run to run.
    private static IEnumerable<string> Images(string assetRoot, IReadOnlyList<string> folders)
    {
        var root = Path.GetFullPath(assetRoot);
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var directory = AssetFiles.Combine(root, folder);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    && !Excluded.Any(excluded => relative.StartsWith(excluded + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    paths.Add(relative);
                }
            }
        }

        return paths;
    }
}

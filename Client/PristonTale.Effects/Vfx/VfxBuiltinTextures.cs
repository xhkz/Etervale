using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Effects.Vfx;

// Procedural sprite textures for native effects ("builtin:glow" and so on): smooth falloffs with real alpha, so
// additive and alpha-blended particles have no hard edges at any size. Generated once per device.
public sealed class VfxBuiltinTextures : IDisposable
{
    public const string Prefix = "builtin:";
    private const int Size = 128;

    // A "builtin:" path must name one of the Shapes; other paths are asset paths.
    public static bool IsKnownOrAsset(string path) =>
        !path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || Names.Contains(path[Prefix.Length..]);

    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);

    // Each sprite's alpha over [-1, 1]^2. Every shape reaches zero inside the square (the beam, a strip, across its width only),
    // so a bright additive sprite shows no square edge or cut-off tip.
    internal static readonly IReadOnlyDictionary<string, Func<float, float, float>> Shapes = new Dictionary<string, Func<float, float, float>>
    {
        ["glow"] = (x, y) => Gaussian(x, y, 0.32f) * Edge(x, y),
        ["soft"] = (x, y) => SmoothDisc(x, y, 0.95f, 0.45f),
        ["spark"] = (x, y) => Gaussian(x * 4f, y * 0.9f, 0.5f) * Edge(x, y),
        ["beam"] = (x, y) => Gaussian(0f, y, 0.38f) * Edge(0f, y),
        ["ring"] = (x, y) => Ring(x, y, 0.78f, 0.12f) * Edge(x, y),
        ["star"] = (x, y) => MathF.Max(Gaussian(x, y, 0.12f), MathF.Max(Gaussian(x * 9f, y, 0.55f), Gaussian(x, y * 9f, 0.55f)) * 0.9f) * Edge(x, y),
        ["smoke"] = (x, y) => SmoothDisc(x, y, 1f, 0.75f) * (0.55f + 0.45f * Noise(x * 3.1f + 7f, y * 3.1f + 3f)) * Edge(x, y),
        ["flame"] = (x, y) => SmoothDisc(x, y * 0.8f - 0.1f, 0.9f, 0.6f) * (0.6f + 0.4f * Noise(x * 4f + 1f, y * 4f + 5f)) * Edge(x, y)
    };

    public static readonly IReadOnlySet<string> Names = new HashSet<string>(Shapes.Keys, StringComparer.OrdinalIgnoreCase);

    public VfxBuiltinTextures(GraphicsDevice device)
    {
        foreach (var (name, alpha) in Shapes)
        {
            Add(device, name, alpha);
        }
    }

    public bool TryGet(string path, out Texture2D texture)
    {
        texture = null!;
        return path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && _textures.TryGetValue(path[Prefix.Length..], out texture!);
    }

    private void Add(GraphicsDevice device, string name, Func<float, float, float> alpha)
    {
        var pixels = new Color[Size * Size];
        for (var py = 0; py < Size; py++)
        {
            for (var px = 0; px < Size; px++)
            {
                var x = (px + 0.5f) / Size * 2f - 1f;
                var y = (py + 0.5f) / Size * 2f - 1f;
                var a = Math.Clamp(alpha(x, y), 0f, 1f);
                // White colour, shape in alpha; the vertex colour and the blend give the hue.
                pixels[py * Size + px] = new Color((byte)255, (byte)255, (byte)255, (byte)(a * 255f + 0.5f));
            }
        }

        var texture = new Texture2D(device, Size, Size, false, SurfaceFormat.ColorSRgb);
        texture.SetData(pixels);
        _textures[name] = texture;
    }

    private static float Gaussian(float x, float y, float sigma) => MathF.Exp(-(x * x + y * y) / (2f * sigma * sigma));

    private static float Edge(float x, float y) => SmoothDisc(x, y, 1f, 0.3f);

    private static float SmoothDisc(float x, float y, float radius, float softness)
    {
        var distance = MathF.Sqrt(x * x + y * y);
        var t = Math.Clamp((radius - distance) / softness, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Ring(float x, float y, float radius, float width)
    {
        var distance = MathF.Sqrt(x * x + y * y);
        var d = (distance - radius) / width;
        return MathF.Exp(-d * d * 2f);
    }

    // Smooth value noise in [0, 1].
    private static float Noise(float x, float y)
    {
        var ix = (int)MathF.Floor(x);
        var iy = (int)MathF.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        var a = Hash(ix, iy);
        var b = Hash(ix + 1, iy);
        var c = Hash(ix, iy + 1);
        var d = Hash(ix + 1, iy + 1);
        return MathHelper.Lerp(MathHelper.Lerp(a, b, fx), MathHelper.Lerp(c, d, fx), fy);
    }

    private static float Hash(int x, int y)
    {
        var h = unchecked((uint)(x * 374761393 + y * 668265263));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    public void Dispose()
    {
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
    }
}

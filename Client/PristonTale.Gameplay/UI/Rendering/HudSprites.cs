using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Assets;

namespace PristonTale.Gameplay.UI.Rendering;

// Original interface images by asset-relative path. Files are read and decoded on the thread pool;
// Update uploads finished images on the game thread within a fixed byte budget. At a HUD scale above 1 an image is read
// from its higher-resolution copy when there is one (HudHiResArt); its density says how many texture pixels stand for one
// pixel of the original, and sizes and source rectangles stay in the original's pixels (Size, HudPainter).
public sealed class HudSprites : IDisposable
{
    private const int UploadBudgetBytes = 512 * 1024;
    private const int MaximumDimension = 2048;

    private sealed record Prepared(string Path, DecodedPristonImage Image, int Density, bool Missing);

    private readonly record struct Loaded(Texture2D Texture, int Density);

    private sealed class Upload(string path, DecodedPristonImage image, int density)
    {
        public string Path { get; } = path;
        public DecodedPristonImage Image { get; } = image;
        public int Density { get; } = density;
        public Texture2D Texture { get; set; }
        public int Row { get; set; }
    }

    private readonly GraphicsDevice _device;
    private readonly string _assetRoot;
    private readonly IClientLog _log;
    private readonly Dictionary<string, Loaded> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _queued = [];
    private readonly Queue<Upload> _uploads = new();
    private Task<Prepared[]> _decode;
    private int _density = 1;
    private bool _disposed;

    public HudSprites(GraphicsDevice device, string assetRoot, IClientLog log)
    {
        _device = device;
        _assetRoot = assetRoot;
        _log = log;
    }

    public bool Failed { get; private set; }
    public bool IsIdle => _queued.Count == 0 && _decode is null && _uploads.Count == 0;
    public long TextureBytes { get; private set; }

    public Texture2D Get(string path) => Get(path, out _);

    // density: texture pixels per pixel of the original image (1 for the original itself).
    public Texture2D Get(string path, out int density)
    {
        if (path is not null && _textures.TryGetValue(path, out var loaded))
        {
            density = loaded.Density;
            return loaded.Texture;
        }

        density = 1;
        return null;
    }

    // The image's size in the original's pixels.
    public Point Size(string path) =>
        path is not null && _textures.TryGetValue(path, out var loaded)
            ? new Point(loaded.Texture.Width / loaded.Density, loaded.Texture.Height / loaded.Density)
            : Point.Zero;

    // The HUD's integer scale: a change reads every known image again at the density it asks for. The images in use stay
    // until their replacements are uploaded.
    public void SetScale(int hudScale)
    {
        var density = HudHiResArt.DensityFor(hudScale);
        if (density == _density || _disposed)
        {
            return;
        }

        _density = density;
        foreach (var path in _known)
        {
            if (!_queued.Contains(path))
            {
                _queued.Add(path);
            }
        }

        _missing.Clear();
    }
    public bool IsResolved(string path) => _textures.ContainsKey(path) || _missing.Contains(path);
    public bool IsMissing(string path) => _missing.Contains(path);

    public void Request(string path)
    {
        if (_disposed || string.IsNullOrEmpty(path) || !_known.Add(path))
        {
            return;
        }

        _queued.Add(path);
    }

    public void Request(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            Request(path);
        }
    }

    public void Release(string path)
    {
        if (path is null || !_known.Remove(path))
        {
            return;
        }

        _queued.Remove(path);
        _missing.Remove(path);
        if (_textures.Remove(path, out var loaded))
        {
            TextureBytes -= (long)loaded.Texture.Width * loaded.Texture.Height * 4;
            loaded.Texture.Dispose();
        }
    }

    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        CompleteDecode();
        if (_decode is null && _queued.Count > 0)
        {
            var batch = _queued.ToArray();
            _queued.Clear();
            var root = _assetRoot;
            var density = _density;
            _decode = Task.Run(() => Decode(root, batch, density));
            // Observe faults of a batch abandoned by Dispose before completion.
            _ = _decode.ContinueWith(task => _log.Error("HUD sprite decoding failed", task.Exception),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }

        UploadWithinBudget();
    }

    private void CompleteDecode()
    {
        if (_decode is null || !_decode.IsCompleted)
        {
            return;
        }

        var decode = _decode;
        _decode = null;
        if (!decode.IsCompletedSuccessfully)
        {
            Failed = true;
            return;
        }

        foreach (var prepared in decode.Result)
        {
            if (!_known.Contains(prepared.Path))
            {
                continue;
            }

            if (prepared.Missing)
            {
                _missing.Add(prepared.Path);
                continue;
            }

            _uploads.Enqueue(new Upload(prepared.Path, prepared.Image, prepared.Density));
        }
    }

    private void UploadWithinBudget()
    {
        var budget = UploadBudgetBytes;
        while (budget > 0 && _uploads.TryPeek(out var upload))
        {
            if (!_known.Contains(upload.Path))
            {
                upload.Texture?.Dispose();
                _uploads.Dequeue();
                continue;
            }

            try
            {
                var image = upload.Image;
                upload.Texture ??= new Texture2D(_device, image.Width, image.Height, false, SurfaceFormat.Color);
                var rows = Math.Clamp(budget / (image.Width * 4), 1, image.Height - upload.Row);
                upload.Texture.SetData(0, new Rectangle(0, upload.Row, image.Width, rows), image.Pixels, upload.Row * image.Width, rows * image.Width);
                upload.Row += rows;
                budget -= rows * image.Width * 4;
                if (upload.Row < image.Height)
                {
                    continue;
                }

                _uploads.Dequeue();
                if (_textures.Remove(upload.Path, out var previous))
                {
                    TextureBytes -= (long)previous.Texture.Width * previous.Texture.Height * 4;
                    previous.Texture.Dispose();
                }

                _textures.Add(upload.Path, new Loaded(upload.Texture, upload.Density));
                TextureBytes += (long)image.Width * image.Height * 4;
            }
            catch (Exception exception)
            {
                _uploads.Dequeue();
                upload.Texture?.Dispose();
                _missing.Add(upload.Path);
                Failed = true;
                _log.Error($"HUD sprite upload failed: {upload.Path}", exception);
            }
        }
    }

    private static Prepared[] Decode(string assetRoot, string[] paths, int density)
    {
        var result = new Prepared[paths.Length];
        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            if (density > 1 && Read(AssetFiles.Combine(assetRoot, HudHiResArt.PathOf(path, density)), density) is { } copy)
            {
                result[index] = new Prepared(path, copy, density, false);
                continue;
            }

            var image = Read(AssetFiles.Combine(assetRoot, path), 1);
            result[index] = image is null ? new Prepared(path, DecodedPristonImage.Empty, 1, true) : new Prepared(path, image, 1, false);
        }

        return result;
    }

    // The decoded and prepared image, or null when it is missing or unreadable. A copy's sides are whole multiples of its
    // density, so its size in the original's pixels is exact.
    private static DecodedPristonImage Read(string file, int density)
    {
        if (!AssetFiles.Exists(file))
        {
            return null;
        }

        var extension = Path.GetExtension(file);
        DecodedPristonImage image;
        try
        {
            image = PristonImageDecoder.Decode(AssetFiles.ReadAllBytes(file), extension, bmpCoverage: true);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            return null;
        }

        if (!image.IsValid || image.Width > MaximumDimension * density || image.Height > MaximumDimension * density
            || image.Width % density != 0 || image.Height % density != 0)
        {
            return null;
        }

        PristonUiPixels.Prepare(image, PristonUiPixels.InterfaceKeyFor(extension));
        return image;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var loaded in _textures.Values)
        {
            loaded.Texture.Dispose();
        }

        foreach (var upload in _uploads)
        {
            upload.Texture?.Dispose();
        }

        _textures.Clear();
        _uploads.Clear();
        _queued.Clear();
        _decode = null;
        TextureBytes = 0;
    }
}

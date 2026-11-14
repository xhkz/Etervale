using PristonTale.Engine3D.Rendering;

namespace PristonTale.HudUpscale;

// Pixel work around the upscaler, on straight-alpha RGBA bytes (4 per pixel, row-major). The upscaler sees colour only:
// transparent pixels take their opaque neighbours' colour first (so no dark fringe bleeds into the edges), and the
// original's coverage is scaled separately and put back on its result.
public static class HiResPixels
{
    // Upscaler input: every pixel opaque; a transparent one takes the mean colour of its nearest covered pixels, found
    // by growing the covered area one pixel per pass (pixels never reached stay black).
    public static byte[] Bleed(byte[] rgba, int width, int height)
    {
        var result = (byte[])rgba.Clone();
        var known = new bool[width * height];
        var any = false;
        for (var index = 0; index < known.Length; index++)
        {
            known[index] = rgba[index * 4 + 3] > 0;
            any |= known[index];
        }

        var next = new List<(int Index, byte R, byte G, byte B)>();
        while (any)
        {
            next.Clear();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (known[y * width + x])
                    {
                        continue;
                    }

                    int r = 0, g = 0, b = 0, count = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var nx = x + dx;
                            var ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height || !known[ny * width + nx])
                            {
                                continue;
                            }

                            var at = (ny * width + nx) * 4;
                            r += result[at];
                            g += result[at + 1];
                            b += result[at + 2];
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        next.Add((y * width + x, (byte)(r / count), (byte)(g / count), (byte)(b / count)));
                    }
                }
            }

            foreach (var (index, r, g, b) in next)
            {
                known[index] = true;
                (result[index * 4], result[index * 4 + 1], result[index * 4 + 2]) = (r, g, b);
            }

            any = next.Count > 0;
        }

        for (var index = 0; index < known.Length; index++)
        {
            if (!known[index])
            {
                (result[index * 4], result[index * 4 + 1], result[index * 4 + 2]) = ((byte)0, (byte)0, (byte)0);
            }

            result[index * 4 + 3] = 255;
        }

        return result;
    }

    // The original's coverage, scaled by factor with bilinear filtering (pixel centres aligned), on the upscaled colour.
    public static byte[] WithCoverage(byte[] upscaled, byte[] original, int width, int height, int factor)
    {
        if (upscaled.Length != width * factor * height * factor * 4)
        {
            throw new ArgumentException($"the upscaled image is not {width * factor}x{height * factor}");
        }

        var coverage = new byte[width * height];
        for (var index = 0; index < coverage.Length; index++)
        {
            coverage[index] = original[index * 4 + 3];
        }

        var scaled = HiResArt.UpsampleCoverage(coverage, width, height, factor);
        var result = (byte[])upscaled.Clone();
        for (var index = 0; index < scaled.Length; index++)
        {
            result[index * 4 + 3] = scaled[index];
        }

        return result;
    }

    // A tiling texture with margin pixels of its opposite edges around it, so the upscaler sees its neighbours across
    // every edge and the result tiles without a seam once Crop takes the margin off again.
    public static byte[] WrapPad(byte[] rgba, int width, int height, int margin)
    {
        var outWidth = width + margin * 2;
        var outHeight = height + margin * 2;
        var result = new byte[outWidth * outHeight * 4];
        for (var y = 0; y < outHeight; y++)
        {
            var sy = ((y - margin) % height + height) % height;
            for (var x = 0; x < outWidth; x++)
            {
                var sx = ((x - margin) % width + width) % width;
                Array.Copy(rgba, (sy * width + sx) * 4, result, (y * outWidth + x) * 4, 4);
            }
        }

        return result;
    }

    public static byte[] Crop(byte[] rgba, int width, int left, int top, int cropWidth, int cropHeight)
    {
        var result = new byte[cropWidth * cropHeight * 4];
        for (var y = 0; y < cropHeight; y++)
        {
            Array.Copy(rgba, ((top + y) * width + left) * 4, result, y * cropWidth * 4, cropWidth * 4);
        }

        return result;
    }

    // Area resampling to a smaller size (each output pixel averages the source area it covers), weighted by coverage so
    // transparent colour never darkens an edge.
    public static byte[] Shrink(byte[] rgba, int width, int height, int outWidth, int outHeight)
    {
        var result = new byte[outWidth * outHeight * 4];
        var scaleX = (double)width / outWidth;
        var scaleY = (double)height / outHeight;
        for (var oy = 0; oy < outHeight; oy++)
        {
            var top = oy * scaleY;
            var bottom = top + scaleY;
            for (var ox = 0; ox < outWidth; ox++)
            {
                var left = ox * scaleX;
                var right = left + scaleX;
                double r = 0, g = 0, b = 0, a = 0, area = 0;
                for (var y = (int)top; y < Math.Min(height, (int)Math.Ceiling(bottom)); y++)
                {
                    var wy = Math.Min(bottom, y + 1) - Math.Max(top, y);
                    for (var x = (int)left; x < Math.Min(width, (int)Math.Ceiling(right)); x++)
                    {
                        var weight = (Math.Min(right, x + 1) - Math.Max(left, x)) * wy;
                        var at = (y * width + x) * 4;
                        var alpha = rgba[at + 3] / 255d;
                        r += rgba[at] * alpha * weight;
                        g += rgba[at + 1] * alpha * weight;
                        b += rgba[at + 2] * alpha * weight;
                        a += alpha * weight;
                        area += weight;
                    }
                }

                var to = (oy * outWidth + ox) * 4;
                if (a > 0)
                {
                    result[to] = (byte)Math.Round(Math.Clamp(r / a, 0, 255));
                    result[to + 1] = (byte)Math.Round(Math.Clamp(g / a, 0, 255));
                    result[to + 2] = (byte)Math.Round(Math.Clamp(b / a, 0, 255));
                }

                result[to + 3] = (byte)Math.Round(Math.Clamp(a / area * 255, 0, 255));
            }
        }

        return result;
    }}

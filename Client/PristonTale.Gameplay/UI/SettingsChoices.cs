using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.UI;

// The values the settings screens offer for options whose valid range is wider than a list (frame limits) or depends on
// the display (sizes, frequencies). Discrete rendering values come from ClientSettings' own tables.
internal static class SettingsChoices
{
    public static readonly int[] FrameLimits = [0, 30, 60, 90, 120, 144, 165, 240, 360];
    public static readonly GraphicsQuality[] Presets = [GraphicsQuality.Low, GraphicsQuality.Medium, GraphicsQuality.High, GraphicsQuality.Ultra];

    // The sizes a mode offers, smallest first, all from the modes the monitor reports: fullscreen any of them; a window
    // those that fit on the desktop below its title bar; borderless none, since it always covers the desktop.
    public static List<Point> Resolutions(WindowMode mode, IReadOnlyList<DisplayModeOption> modes, DisplayModeOption desktop)
    {
        var sizes = new List<Point>();
        if (mode == WindowMode.Borderless)
        {
            return sizes;
        }

        foreach (var option in modes)
        {
            var size = new Point(option.Width, option.Height);
            var fits = mode == WindowMode.Fullscreen || option.Width <= desktop.Width && option.Height <= desktop.Height - WindowPlacement.TitleBar;
            if (fits && !sizes.Contains(size))
            {
                sizes.Add(size);
            }
        }

        sizes.Sort(static (left, right) => ((long)left.X * left.Y).CompareTo((long)right.X * right.Y));
        return sizes;
    }

    // The refresh rates the chosen monitor reports for the chosen size, lowest first, after 0 (automatic); only
    // fullscreen sets one, so other modes offer 0 alone.
    public static List<int> Frequencies(ClientSettings settings, IReadOnlyList<DisplayModeOption> modes)
    {
        var frequencies = new List<int> { 0 };
        if (settings.WindowMode != WindowMode.Fullscreen)
        {
            return frequencies;
        }

        foreach (var mode in modes)
        {
            if ((settings.Monitor == -1 || mode.Monitor == settings.Monitor)
                && mode.Width == settings.WindowWidth && mode.Height == settings.WindowHeight && !frequencies.Contains(mode.RefreshRate))
            {
                frequencies.Add(mode.RefreshRate);
            }
        }

        frequencies.Sort();
        return frequencies;
    }
}

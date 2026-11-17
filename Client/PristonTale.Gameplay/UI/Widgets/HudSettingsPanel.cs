using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Widgets;

// Display/audio options reached from the system menu "Settings" entry, drawn in the original help box.
// The interface scale is not listed: the in-game HUD always uses the largest integer scale that fits.
internal sealed class HudSettingsPanel
{
    public const int BoxX = 50;
    public const int BoxY = 45;
    public const int SizeW = 9;
    public const int SizeH = 14;
    private const int RowHeight = 17;
    private const int CloudQualities = (int)CloudQuality.High + 1;

    private static readonly int WindowModes = Enum.GetValues<WindowMode>().Length;
    private static readonly int AntiAliasingModes = Enum.GetValues<AntiAliasingMode>().Length;
    private readonly PresentationSettings _settings;
    // Display and audio, graphics and camera, then the read-only shortcut reference.
    private const int PageCount = 3;
    private const int KeysPage = 2;
    private static readonly string[] PageTitles = ["Settings.Title", "Settings.PageGraphics", "Settings.PageKeys"];
    private static readonly string[] KeyLines = [.. Enumerable.Range(1, RowsPerPage).Select(static line => "Settings.Keys" + line)];
    // The option rows of the pages before the reference, in the order RefreshLabels writes them.
    private const int OptionRows = 21;
    private readonly string[] _labels = new string[OptionRows];
    private int _page;
    private string _pageLabel = "";
    private int _labelPage = -1;
    public const int RowsPerPage = 11;
    // The display and message notes, in row units under the page rows and the page buttons.
    private const int NotesRow = 18;
    public static Rectangle PreviousPage => new(BoxX + 22, BoxY + 294, 105, 25);
    public static Rectangle NextPage => new(BoxX + 208, BoxY + 294, 105, 25);
    private ClientSettings _labelSettings;

    public HudSettingsPanel(PresentationSettings settings) => _settings = settings;

    public static Rectangle Box => HudPainter.HelpBoxBounds(BoxX, BoxY, SizeW, SizeH);
    public static Rectangle Title => HudPainter.HelpBoxTitle(BoxX, BoxY, SizeW);
    public static Rectangle OkButton => HudPainter.HelpBoxOk(BoxX, BoxY, SizeW, SizeH);

    public static Rectangle Row(int row) => new(BoxX + 24, BoxY + 64 + row * RowHeight, SizeW * 32 + 64 - 48, RowHeight - 1);

    public void Click(Point pointer, bool backwards)
    {
        if (_settings.AwaitingConfirmation)
        {
            return;
        }

        if (PreviousPage.Contains(pointer))
        {
            _page = (_page - 1 + PageCount) % PageCount;
            return;
        }

        if (NextPage.Contains(pointer))
        {
            _page = (_page + 1) % PageCount;
            return;
        }

        if (_page == KeysPage)
        {
            return;
        }

        var current = _settings.Requested;
        for (var localRow = 0; localRow < RowsPerPage; localRow++)
        {
            if (!Row(localRow).Contains(pointer))
            {
                continue;
            }

            var row = _page * RowsPerPage + localRow;
            var direction = backwards ? -1 : 1;
            _settings.Requested = (row switch
            {
                0 => current with { WindowMode = (WindowMode)(((int)current.WindowMode + direction + WindowModes) % WindowModes) },
                1 => current with { Monitor = ((current.Monitor + 1 + direction + _settings.MonitorCount + 1) % (_settings.MonitorCount + 1)) - 1 },
                2 => NextResolution(current, direction),
                3 => current with { RefreshRate = NextFrequency(current, direction) },
                4 => current with { VerticalSync = !current.VerticalSync },
                5 => current with { MusicEnabled = !current.MusicEnabled },
                6 => current with { SoundEnabled = !current.SoundEnabled },
                7 => current with { MaxFps = Cycle(SettingsChoices.FrameLimits, current.MaxFps, direction) },
                8 => current with { BackgroundMaxFps = Cycle(SettingsChoices.FrameLimits, current.BackgroundMaxFps, direction) },
                9 => current with { AntiAliasing = (AntiAliasingMode)(((int)current.AntiAliasing + direction + AntiAliasingModes) % AntiAliasingModes) },
                10 => current with { ShadowMapSize = Cycle(ClientSettings.ShadowMapSizes, current.ShadowMapSize, direction) },
                11 => current.WithQuality(NextPreset(current.Quality, direction)),
                12 => current with { ShadowUpdateRate = Cycle(ClientSettings.ShadowUpdateRates, current.ShadowUpdateRate, direction) },
                13 => current with { ShadowsEnabled = !current.ShadowsEnabled },
                14 => current with { ClassicTargetHealthBar = !current.ClassicTargetHealthBar },
                15 => current with { ClassicNamePlates = !current.ClassicNamePlates },
                16 => current with { WeatherEnabled = !current.WeatherEnabled },
                17 => current with { RealisticSky = !current.RealisticSky },
                18 => current with { CloudQuality = (CloudQuality)(((int)current.CloudQuality + direction + CloudQualities) % CloudQualities) },
                19 => current with { Language = NextLanguage(current.Language, direction) },
                20 => current with { CameraSceneryCollision = !current.CameraSceneryCollision },
                _ => current
            }).WithQualityLabel();
            return;
        }
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        RefreshLabels();
        painter.HelpBox(BoxX, BoxY, SizeW, SizeH);
        var title = Title;
        painter.Sprite(HudArt.HelpTitle, title.X, title.Y);
        painter.TextCentered(ClientText.Get(PageTitles[_page]), title.Left, title.Right, title.Y + 7, HudTextColors.BoxTitle);
        for (var localRow = 0; localRow < RowsPerPage; localRow++)
        {
            var row = Row(localRow);
            var index = _page * RowsPerPage + localRow;
            var text = _page == KeysPage ? ClientText.Get(KeyLines[localRow]) : index < _labels.Length ? _labels[index] : null;
            if (text is null)
            {
                continue;
            }

            if (_page != KeysPage && row.Contains(pointer))
            {
                painter.Fill(row, Color.FromNonPremultiplied(255, 200, 0, 64));
            }

            painter.Text(painter.Fit(text, row.Width - 8), row.X + 6, row.Y + 1);
        }

        painter.Text(_pageLabel, PreviousPage.X, PreviousPage.Y + 3,
            PreviousPage.Contains(pointer) ? HudTextColors.Marked : HudTextColors.Hint);
        painter.Text(ClientText.Get("Settings.NextPage"), NextPage.X, NextPage.Y + 3,
            NextPage.Contains(pointer) ? HudTextColors.Marked : HudTextColors.Hint);
        var notes = Row(NotesRow);
        painter.Text(painter.Fit(_settings.EffectiveDisplay, notes.Width - 8), notes.X + 6, notes.Y + 6, HudTextColors.Hint);
        var hint = ClientText.Get(_settings.Message.Length > 0 ? "Settings.NeedsAttention" :
            _page == KeysPage ? "Settings.ReferenceOnly" : "Settings.Hint");
        painter.Text(hint, notes.X + 6, notes.Y + 6 + RowHeight, _settings.Message.Length > 0 ? Color.Orange : HudTextColors.Hint);
        var ok = OkButton;
        painter.Sprite(HudArt.HelpButton, ok.X - 9, ok.Y - 4);
        painter.Sprite(ok.Contains(pointer) ? HudArt.OkYellow : HudArt.OkGray, ok.X, ok.Y);
    }

    private ClientSettings NextResolution(ClientSettings current, int direction)
    {
        var sizes = SettingsChoices.Resolutions(current.WindowMode, _settings.DisplayModes, _settings.Desktop);
        if (sizes.Count == 0)
        {
            return current;
        }

        var index = sizes.IndexOf(new Point(current.WindowWidth, current.WindowHeight));
        var next = index < 0 && direction < 0 ? sizes[^1] : sizes[(index + direction + sizes.Count) % sizes.Count];
        return current with { WindowWidth = next.X, WindowHeight = next.Y };
    }

    private int NextFrequency(ClientSettings current, int direction) =>
        current.WindowMode != WindowMode.Fullscreen ? 0 : Cycle(SettingsChoices.Frequencies(current, _settings.DisplayModes), current.RefreshRate, direction);

    private static GraphicsQuality NextPreset(GraphicsQuality quality, int direction)
    {
        var index = Array.IndexOf(SettingsChoices.Presets, quality);
        if (index < 0)
        {
            return direction > 0 ? SettingsChoices.Presets[0] : SettingsChoices.Presets[^1];
        }

        return SettingsChoices.Presets[(index + direction + SettingsChoices.Presets.Length) % SettingsChoices.Presets.Length];
    }

    private static int Cycle(IReadOnlyList<int> values, int value, int direction)
    {
        var index = 0;
        for (var candidate = 0; candidate < values.Count; candidate++)
        {
            if (values[candidate] == value)
            {
                index = candidate;
                break;
            }
        }

        return values[(index + direction + values.Count) % values.Count];
    }

    private void RefreshLabels()
    {
        var settings = _settings.Requested;
        if (_labelSettings == settings && _labelPage == _page)
        {
            return;
        }

        _labelSettings = settings;
        _labelPage = _page;
        _pageLabel = ClientText.Format("Settings.PageOf", ClientText.Get("Settings.PrevPage"), _page + 1, PageCount);
        _labels[0] = ClientText.Format("Settings.Window", ClientText.Get("Settings.WindowMode." + settings.WindowMode));
        _labels[1] = settings.Monitor < 0 ? ClientText.Get("Settings.MonitorCurrent") : ClientText.Format("Settings.Monitor", settings.Monitor);
        _labels[2] = settings.WindowMode == WindowMode.Borderless
            ? ClientText.Get("Settings.ResolutionDesktop")
            : ClientText.Format("Settings.Resolution", settings.WindowWidth, settings.WindowHeight);
        _labels[3] = settings.WindowMode != WindowMode.Fullscreen ? ClientText.Get("Settings.FrequencyDesktop") : ClientText.Format("Settings.Frequency", settings.RefreshRate);
        _labels[4] = ClientText.Get(settings.VerticalSync ? "Settings.VSyncOn" : "Settings.VSyncOff");
        _labels[5] = ClientText.Get(settings.MusicEnabled ? "Settings.MusicOn" : "Settings.MusicOff");
        _labels[6] = ClientText.Get(settings.SoundEnabled ? "Settings.SoundOn" : "Settings.SoundOff");
        _labels[7] = settings.MaxFps == 0 ? ClientText.Get("Settings.MaxFpsUnlimited") : ClientText.Format("Settings.MaxFps", settings.MaxFps);
        _labels[8] = settings.BackgroundMaxFps == 0
            ? ClientText.Get("Settings.BackgroundFpsForeground")
            : ClientText.Format("Settings.BackgroundFps", settings.BackgroundMaxFps);
        _labels[9] = settings.AntiAliasing == AntiAliasingMode.Msaa
            ? ClientText.Format("Settings.AntiAliasingMsaa", Math.Max(settings.MultiSampleCount, 2))
            : settings.AntiAliasing == AntiAliasingMode.Off
                ? ClientText.Get("Settings.AntiAliasingOff")
                : ClientText.Format("Settings.AntiAliasing", settings.AntiAliasing.ToString().ToUpperInvariant());
        _labels[10] = ClientText.Format("Settings.SunShadow", settings.ShadowMapSize);
        _labels[11] = ClientText.Format("Settings.Quality", ClientText.Get("Settings.Quality." + settings.Quality));
        _labels[12] = settings.ShadowUpdateRate == 0
            ? ClientText.Get("Settings.ShadowAnimationEveryFrame")
            : ClientText.Format("Settings.ShadowAnimation", settings.ShadowUpdateRate);
        _labels[13] = ClientText.Get(settings.ShadowsEnabled ? "Settings.ShadowsOn" : "Settings.ShadowsOff");
        _labels[14] = ClientText.Get(settings.ClassicTargetHealthBar ? "Settings.TargetHealthClassic" : "Settings.TargetHealthAlways");
        _labels[15] = ClientText.Get(settings.ClassicNamePlates ? "Settings.NamePlatesClassic" : "Settings.NamePlatesCompact");
        _labels[16] = ClientText.Get(settings.WeatherEnabled ? "Settings.WeatherOn" : "Settings.WeatherOff");
        _labels[17] = ClientText.Get(settings.RealisticSky ? "Settings.SkyRealistic" : "Settings.SkyClassic");
        _labels[18] = ClientText.Format("Settings.Clouds", ClientText.Get("Settings.Clouds." + settings.CloudQuality));
        _labels[19] = ClientText.Format("Settings.Language", ClientText.Get("Language." + settings.Language));
        _labels[20] = ClientText.Get(settings.CameraSceneryCollision ? "Settings.CameraCollisionOn" : "Settings.CameraCollisionOff");
    }

    private static string NextLanguage(string language, int direction)
    {
        var languages = ClientSettings.SupportedLanguages;
        var index = 0;
        for (var candidate = 0; candidate < languages.Count; candidate++)
        {
            if (string.Equals(languages[candidate], language, StringComparison.OrdinalIgnoreCase))
            {
                index = candidate;
                break;
            }
        }

        return languages[(index + direction + languages.Count) % languages.Count];
    }
}

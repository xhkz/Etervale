using System.Globalization;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

internal enum SettingsTab
{
    Display,
    Graphics,
    Effects,
    Game
}

// One value an option offers: its label and the settings with it chosen.
internal readonly record struct OptionChoice(string Label, Func<ClientSettings, ClientSettings> Apply);

internal abstract record SettingsOption(SettingsTab Tab, string LabelKey);

// A dropdown. The chosen value is the first whose Apply leaves the settings unchanged.
internal sealed record ChoiceOption(SettingsTab Tab, string LabelKey, Func<ClientSettings, PresentationSettings, List<OptionChoice>> Choices)
    : SettingsOption(Tab, LabelKey);

internal sealed record SliderOption(SettingsTab Tab, string LabelKey, float Minimum, float Maximum, float Step, Func<ClientSettings, float> Get,
    Func<ClientSettings, float, ClientSettings> Set, Func<float, string> Format) : SettingsOption(Tab, LabelKey);

internal sealed record TextOption(SettingsTab Tab, string LabelKey, int MaxLength, string Symbols, Func<ClientSettings, string> Get,
    Func<ClientSettings, string, ClientSettings> Set) : SettingsOption(Tab, LabelKey);

// Every option of client-settings.json the settings form edits, by tab, in the order it lists them. Window placement
// (windowX, windowY, windowMaximized) follows the window, and uiScale is read by nothing, so neither is listed.
internal static class SettingsOptions
{
    public static readonly SettingsOption[] All =
    [
        EnumChoice<WindowMode>(SettingsTab.Display, "Options.WindowMode", static s => s.WindowMode, static (s, v) => s with { WindowMode = v }),
        new ChoiceOption(SettingsTab.Display, "Options.Monitor", Monitors),
        new ChoiceOption(SettingsTab.Display, "Options.Resolution", Resolutions),
        new ChoiceOption(SettingsTab.Display, "Options.RefreshRate", Frequencies),
        Toggle(SettingsTab.Display, "Options.VerticalSync", static s => s.VerticalSync, static (s, v) => s with { VerticalSync = v }),
        new ChoiceOption(SettingsTab.Display, "Options.MaxFps", static (settings, _) => FrameLimits(settings, static s => s.MaxFps, static (s, v) => s with { MaxFps = v }, "Options.Unlimited")),
        new ChoiceOption(SettingsTab.Display, "Options.BackgroundFps",
            static (settings, _) => FrameLimits(settings, static s => s.BackgroundMaxFps, static (s, v) => s with { BackgroundMaxFps = v }, "Options.ForegroundLimit")),
        new ChoiceOption(SettingsTab.Display, "Options.Language", static (_, _) => Languages()),
        new TextOption(SettingsTab.Display, "Options.ServerAddress", 120, ".-_:/?=", static s => s.ServerAddress, static (s, v) => s with { ServerAddress = v }),

        new ChoiceOption(SettingsTab.Graphics, "Options.Quality", static (_, _) => Qualities()),
        EnumChoice<AntiAliasingMode>(SettingsTab.Graphics, "Options.AntiAliasing", static s => s.AntiAliasing, static (s, v) => s with { AntiAliasing = v }),
        // 0 is accepted in the file and drawn as 2 samples; the list offers the counts MSAA draws, and 0 only while set.
        new ChoiceOption(SettingsTab.Graphics, "Options.MsaaSamples", static (settings, _) =>
            Numbers(settings, ClientSettings.MultiSampleCounts.Where(static count => count > 0).ToArray(), static s => s.MultiSampleCount, static (s, v) => s with { MultiSampleCount = v }, "Options.Samples")),
        new SliderOption(SettingsTab.Graphics, "Options.Sharpening", 0f, 1f, 0.05f, static s => s.Sharpening, static (s, v) => s with { Sharpening = v }, Percent),
        Toggle(SettingsTab.Graphics, "Options.Shadows", static s => s.ShadowsEnabled, static (s, v) => s with { ShadowsEnabled = v }),
        new ChoiceOption(SettingsTab.Graphics, "Options.ShadowMapSize", static (settings, _) =>
            Numbers(settings, ClientSettings.ShadowMapSizes, static s => s.ShadowMapSize, static (s, v) => s with { ShadowMapSize = v }, "Options.Pixels")),
        new SliderOption(SettingsTab.Graphics, "Options.ShadowCascades", 1f, ClientSettings.MostShadowCascades, 1f, static s => s.ShadowCascades,
            static (s, v) => s with { ShadowCascades = (int)MathF.Round(v) }, Whole),
        EnumChoice<ShadowFilterMode>(SettingsTab.Graphics, "Options.ShadowFilter", static s => s.ShadowFilter, static (s, v) => s with { ShadowFilter = v }),
        new ChoiceOption(SettingsTab.Graphics, "Options.ShadowUpdateRate", static (settings, _) => ShadowRates(settings)),
        new ChoiceOption(SettingsTab.Graphics, "Options.LampShadows", static (settings, _) =>
            Numbers(settings, ClientSettings.LampShadowCounts, static s => s.LampShadowCount, static (s, v) => s with { LampShadowCount = v }, "Options.Count")),
        new ChoiceOption(SettingsTab.Graphics, "Options.LampShadowSize", static (settings, _) =>
            Numbers(settings, ClientSettings.LampShadowTileSizes, static s => s.LampShadowTileSize, static (s, v) => s with { LampShadowTileSize = v }, "Options.Pixels")),
        new ChoiceOption(SettingsTab.Graphics, "Options.LocalLights", static (settings, _) =>
            Numbers(settings, ClientSettings.LocalLightLimits, static s => s.LocalLightLimit, static (s, v) => s with { LocalLightLimit = v }, "Options.Count")),
        Toggle(SettingsTab.Graphics, "Options.HiResWorldTextures", static s => s.HiResWorldTextures, static (s, v) => s with { HiResWorldTextures = v }),

        Toggle(SettingsTab.Effects, "Options.AmbientOcclusion", static s => s.AmbientOcclusion, static (s, v) => s with { AmbientOcclusion = v }),
        Toggle(SettingsTab.Effects, "Options.ContactShadows", static s => s.ContactShadows, static (s, v) => s with { ContactShadows = v }),
        Toggle(SettingsTab.Effects, "Options.ImageBasedLighting", static s => s.ImageBasedLighting, static (s, v) => s with { ImageBasedLighting = v }),
        Toggle(SettingsTab.Effects, "Options.Reflections", static s => s.ScreenSpaceReflections, static (s, v) => s with { ScreenSpaceReflections = v }),
        Toggle(SettingsTab.Effects, "Options.VolumetricLight", static s => s.VolumetricLight, static (s, v) => s with { VolumetricLight = v }),
        EnumChoice<BloomQuality>(SettingsTab.Effects, "Options.Bloom", static s => s.Bloom, static (s, v) => s with { Bloom = v }),
        Toggle(SettingsTab.Effects, "Options.AutoExposure", static s => s.AutoExposure, static (s, v) => s with { AutoExposure = v }),
        new SliderOption(SettingsTab.Effects, "Options.Exposure", -3f, 3f, 0.25f, static s => s.ExposureCompensation,
            static (s, v) => s with { ExposureCompensation = v }, Stops),
        EnumChoice<TonemapperMode>(SettingsTab.Effects, "Options.Tonemapper", static s => s.Tonemapper, static (s, v) => s with { Tonemapper = v }),
        Toggle(SettingsTab.Effects, "Options.ColorGrading", static s => s.ColorGrading, static (s, v) => s with { ColorGrading = v }),
        new TextOption(SettingsTab.Effects, "Options.ColorGradingLut", 120, ".-_/\\: ", static s => s.ColorGradingLut, static (s, v) => s with { ColorGradingLut = v }),
        // Classic is still read from older files but drawn as Atmospheric, so it is not offered.
        EnumChoice(SettingsTab.Effects, "Options.DistanceFog", [DistanceFogMode.Off, DistanceFogMode.Atmospheric], static s => s.DistanceFog,
            static (s, v) => s with { DistanceFog = v }),
        Toggle(SettingsTab.Effects, "Options.RealisticSky", static s => s.RealisticSky, static (s, v) => s with { RealisticSky = v }),
        EnumChoice<CloudQuality>(SettingsTab.Effects, "Options.Clouds", static s => s.CloudQuality, static (s, v) => s with { CloudQuality = v }),

        Toggle(SettingsTab.Game, "Options.Music", static s => s.MusicEnabled, static (s, v) => s with { MusicEnabled = v }),
        Toggle(SettingsTab.Game, "Options.Sound", static s => s.SoundEnabled, static (s, v) => s with { SoundEnabled = v }),
        Toggle(SettingsTab.Game, "Options.Weather", static s => s.WeatherEnabled, static (s, v) => s with { WeatherEnabled = v }),
        EnumChoice<PristonCameraMode>(SettingsTab.Game, "Options.CameraMode", static s => s.CameraMode, static (s, v) => s with { CameraMode = v }),
        Toggle(SettingsTab.Game, "Options.ClassicTargetHealth", static s => s.ClassicTargetHealthBar, static (s, v) => s with { ClassicTargetHealthBar = v }),
        Toggle(SettingsTab.Game, "Options.ClassicNamePlates", static s => s.ClassicNamePlates, static (s, v) => s with { ClassicNamePlates = v }),
        Toggle(SettingsTab.Game, "Options.LevelGuide", static s => s.LevelGuide, static (s, v) => s with { LevelGuide = v }),
        EnumChoice<PristonDebugView>(SettingsTab.Game, "Options.DebugView", static s => s.DebugView, static (s, v) => s with { DebugView = v }),
        Toggle(SettingsTab.Game, "Options.CalibrationSpheres", static s => s.CalibrationSpheres, static (s, v) => s with { CalibrationSpheres = v })
    ];

    // The phrase key of an enum value's label.
    public static string ValueKey<T>(T value) where T : struct, Enum => "Options.Value." + value;

    // The enums whose values the form words through ValueKey.
    public static IEnumerable<string> ValueKeys() =>
        Enum.GetNames<WindowMode>().Concat(Enum.GetNames<GraphicsQuality>()).Concat(Enum.GetNames<AntiAliasingMode>()).Concat(Enum.GetNames<ShadowFilterMode>()).Concat(Enum.GetNames<BloomQuality>())
            .Concat(Enum.GetNames<TonemapperMode>()).Concat(Enum.GetNames<DistanceFogMode>()).Concat(Enum.GetNames<CloudQuality>())
            .Concat(Enum.GetNames<PristonCameraMode>()).Concat(Enum.GetNames<PristonDebugView>())
            .Select(static name => "Options.Value." + name).Distinct();

    // The settings the form starts from or resets to: every option at its default, except where the window is and which
    // server it connects to.
    public static ClientSettings Defaults(ClientSettings current) => new ClientSettings() with
    {
        ServerAddress = current.ServerAddress,
        WindowX = current.WindowX,
        WindowY = current.WindowY,
        WindowMaximized = current.WindowMaximized
    };

    public static float Snap(SliderOption option, float value)
    {
        var steps = MathF.Round((Math.Clamp(value, option.Minimum, option.Maximum) - option.Minimum) / option.Step);
        return Math.Clamp(option.Minimum + steps * option.Step, option.Minimum, option.Maximum);
    }

    private static ChoiceOption Toggle(SettingsTab tab, string key, Func<ClientSettings, bool> get, Func<ClientSettings, bool, ClientSettings> set) =>
        new(tab, key, (_, _) =>
        [
            new OptionChoice(ClientText.Get("Options.On"), settings => get(settings) ? settings : set(settings, true)),
            new OptionChoice(ClientText.Get("Options.Off"), settings => get(settings) ? set(settings, false) : settings)
        ]);

    private static ChoiceOption EnumChoice<T>(SettingsTab tab, string key, Func<ClientSettings, T> get, Func<ClientSettings, T, ClientSettings> set)
        where T : struct, Enum => EnumChoice(tab, key, Enum.GetValues<T>(), get, set);

    private static ChoiceOption EnumChoice<T>(SettingsTab tab, string key, T[] values, Func<ClientSettings, T> get, Func<ClientSettings, T, ClientSettings> set)
        where T : struct, Enum =>
        new(tab, key, (_, _) =>
        {
            var choices = new List<OptionChoice>(values.Length);
            foreach (var value in values)
            {
                choices.Add(new OptionChoice(ClientText.Get(ValueKey(value)), settings => Equals(get(settings), value) ? settings : set(settings, value)));
            }

            return choices;
        });

    // The values, and the one the settings hold when it is valid but not among them (the file may name any).
    private static List<OptionChoice> Numbers(ClientSettings settings, IReadOnlyList<int> values, Func<ClientSettings, int> get,
        Func<ClientSettings, int, ClientSettings> set, string format)
    {
        var offered = new List<int>(values);
        if (!offered.Contains(get(settings)))
        {
            offered.Add(get(settings));
            offered.Sort();
        }

        var choices = new List<OptionChoice>(offered.Count);
        foreach (var value in offered)
        {
            choices.Add(new OptionChoice(ClientText.Format(format, value), settings => get(settings) == value ? settings : set(settings, value)));
        }

        return choices;
    }

    private static List<OptionChoice> FrameLimits(ClientSettings settings, Func<ClientSettings, int> get, Func<ClientSettings, int, ClientSettings> set,
        string zeroKey)
    {
        var choices = Numbers(settings, SettingsChoices.FrameLimits, get, set, "Options.Fps");
        choices[0] = choices[0] with { Label = ClientText.Get(zeroKey) };
        return choices;
    }

    private static List<OptionChoice> ShadowRates(ClientSettings settings)
    {
        var choices = Numbers(settings, ClientSettings.ShadowUpdateRates, static s => s.ShadowUpdateRate, static (s, v) => s with { ShadowUpdateRate = v }, "Options.Hertz");
        choices[0] = choices[0] with { Label = ClientText.Get("Options.EveryFrame") };
        return choices;
    }

    private static List<OptionChoice> Qualities()
    {
        var choices = new List<OptionChoice>();
        foreach (var quality in SettingsChoices.Presets)
        {
            choices.Add(new OptionChoice(ClientText.Get(ValueKey(quality)), settings => settings.WithQuality(quality)));
        }

        choices.Add(new OptionChoice(ClientText.Get(ValueKey(GraphicsQuality.Custom)), static settings => settings.WithQuality(GraphicsQuality.Custom)));
        return choices;
    }

    private static List<OptionChoice> Languages()
    {
        var choices = new List<OptionChoice>();
        foreach (var language in ClientSettings.SupportedLanguages)
        {
            choices.Add(new OptionChoice(ClientText.Get("Language." + language), settings => settings.Language == language ? settings : settings with { Language = language }));
        }

        return choices;
    }

    private static List<OptionChoice> Monitors(ClientSettings settings, PresentationSettings presentation)
    {
        var choices = new List<OptionChoice>
        {
            new(ClientText.Get("Options.CurrentMonitor"), static s => s.Monitor == -1 ? s : s with { Monitor = -1 })
        };
        for (var monitor = 0; monitor < Math.Max(presentation.MonitorCount, settings.Monitor + 1); monitor++)
        {
            var index = monitor;
            choices.Add(new OptionChoice(ClientText.Format("Options.MonitorNumber", index + 1), s => s.Monitor == index ? s : s with { Monitor = index }));
        }

        return choices;
    }

    // Borderless covers the desktop; the others offer the reported sizes, and the size set now even if none matches it.
    private static List<OptionChoice> Resolutions(ClientSettings settings, PresentationSettings presentation)
    {
        if (settings.WindowMode == WindowMode.Borderless)
        {
            return [new OptionChoice(ClientText.Get("Options.Desktop"), static s => s)];
        }

        var sizes = SettingsChoices.Resolutions(settings.WindowMode, presentation.DisplayModes, presentation.Desktop);
        var current = new Microsoft.Xna.Framework.Point(settings.WindowWidth, settings.WindowHeight);
        if (!sizes.Contains(current))
        {
            sizes.Add(current);
        }

        var choices = new List<OptionChoice>(sizes.Count);
        foreach (var size in sizes)
        {
            choices.Add(new OptionChoice(ClientText.Format("Options.Size", size.X, size.Y),
                s => s.WindowWidth == size.X && s.WindowHeight == size.Y ? s : s with { WindowWidth = size.X, WindowHeight = size.Y }));
        }

        return choices;
    }

    private static List<OptionChoice> Frequencies(ClientSettings settings, PresentationSettings presentation)
    {
        if (settings.WindowMode != WindowMode.Fullscreen)
        {
            return [new OptionChoice(ClientText.Get("Options.Desktop"), static s => s)];
        }

        var choices = Numbers(settings, SettingsChoices.Frequencies(settings, presentation.DisplayModes), static s => s.RefreshRate, static (s, v) => s with { RefreshRate = v }, "Options.Hertz");
        choices[0] = choices[0] with { Label = ClientText.Get("Options.Automatic") };
        return choices;
    }

    private static string Percent(float value) => ClientText.Format("Options.Percent", (int)MathF.Round(value * 100));

    private static string Whole(float value) => ((int)MathF.Round(value)).ToString(CultureInfo.InvariantCulture);

    private static string Stops(float value) => ClientText.Format("Options.Stops", value.ToString("+0.00;-0.00;0", CultureInfo.InvariantCulture));
}

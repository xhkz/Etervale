using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Tests;

internal static class SettingsFormTests
{
    private static readonly Viewport Screen = new(0, 0, 1280, 720);

    // Settings no option edits: the file's version, where the window was left (it follows the window) and uiScale (read
    // by nothing).
    private static readonly string[] Unlisted = [nameof(ClientSettings.Version), nameof(ClientSettings.WindowX), nameof(ClientSettings.WindowY),
        nameof(ClientSettings.WindowMaximized), nameof(ClientSettings.UiScale)];

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the settings form edits every option of the settings file", EveryOptionAsync);
        yield return ("every dropdown of the settings form shows the default settings' value", DefaultsShownAsync);
        yield return ("a file naming a preset its options no longer match opens as Custom", RenamedPresetAsync);
        yield return ("a value picked from a dropdown reaches the requested settings only on Apply", PickAndApplyAsync);
        yield return ("Cancel and Escape leave the requested settings as they were; Escape closes an open list first", CancelAsync);
        yield return ("an open list scrolls one value per wheel step, however small the step", WheelAsync);
        yield return ("a slider dragged across its track snaps to its step and stays in its range", SliderAsync);
        yield return ("a preset option changed by hand makes the quality custom; a preset picked applies its options", PresetAsync);
        yield return ("Defaults resets every option but the server and the window's place", DefaultsAsync);
        yield return ("the login form's Settings button sits beside Log in, and the settings form's rows and buttons stay in its panel", LoginButtonAsync);
    }

    private static SettingsForm Form(PresentationSettings presentation)
    {
        var form = new SettingsForm(null!, null!, null!, presentation);
        form.Open();
        // A first frame with the button released, so the next press is a click.
        Frame(form, default);
        return form;
    }

    private static SettingsFormResult Frame(SettingsForm form, MouseState mouse, KeyboardState keyboard = default) =>
        form.Update(keyboard, default, mouse, [], 0.016, Screen);

    private static SettingsFormResult Click(SettingsForm form, Point design, int wheel = 0)
    {
        var layout = SettingsFormLayout.Create(Screen.Width, Screen.Height);
        var x = (int)MathF.Ceiling(design.X * layout.Scale);
        var y = (int)MathF.Ceiling(design.Y * layout.Scale);
        var result = Frame(form, new MouseState(x, y, wheel, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        Frame(form, new MouseState(x, y, wheel, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        return result;
    }

    private static int RowOf(SettingsForm form, string labelKey)
    {
        for (var row = 0; row < form.Rows.Count; row++)
        {
            if (SettingsOptions.All[form.Rows[row]].LabelKey == labelKey)
            {
                return row;
            }
        }

        throw new InvalidOperationException($"{labelKey} is not on the shown tab");
    }

    private static void ShowTab(SettingsForm form, SettingsTab tab) =>
        Click(form, SettingsFormLayout.Create(Screen.Width, Screen.Height).Tab((int)tab).Center);

    private static Task EveryOptionAsync()
    {
        var presentation = new PresentationSettings
        {
            MonitorCount = 2,
            DisplayModes = [new DisplayModeOption(0, 1920, 1080, 60), new DisplayModeOption(0, 1280, 720, 144)],
            Desktop = new DisplayModeOption(0, 2560, 1440, 60)
        };
        var edited = new HashSet<string>();
        var defaults = new ClientSettings();
        foreach (var option in SettingsOptions.All)
        {
            var starts = new[] { defaults, defaults with { WindowMode = WindowMode.Fullscreen, WindowWidth = 1920, WindowHeight = 1080 } };
            foreach (var start in starts)
            {
                var results = option switch
                {
                    ChoiceOption choice => choice.Choices(start, presentation).Select(item => item.Apply(start)).ToList(),
                    SliderOption slider => [slider.Set(start, slider.Minimum), slider.Set(start, slider.Maximum)],
                    TextOption text => [text.Set(start, "probe")],
                    _ => []
                };
                foreach (var result in results)
                {
                    foreach (var property in typeof(ClientSettings).GetProperties().Where(static property => property.CanWrite))
                    {
                        if (!Equals(property.GetValue(result), property.GetValue(start)))
                        {
                            edited.Add(property.Name);
                        }
                    }
                }
            }
        }

        var missing = typeof(ClientSettings).GetProperties().Where(static property => property.CanWrite).Select(static property => property.Name)
            .Except(edited).Except(Unlisted).ToArray();
        Assert.True(missing.Length == 0, "options the settings form cannot change: " + string.Join(", ", missing));
        Assert.True(SettingsOptions.All.GroupBy(static option => option.Tab).All(static tab => tab.Count() <= SettingsFormLayout.MostRows),
            "a tab holds more rows than the form shows");
        return Task.CompletedTask;
    }

    private static Task DefaultsShownAsync()
    {
        var presentation = new PresentationSettings { DisplayModes = [new DisplayModeOption(0, 1280, 720, 60)], Desktop = new DisplayModeOption(0, 1920, 1080, 60) };
        var defaults = new ClientSettings();
        foreach (var choice in SettingsOptions.All.OfType<ChoiceOption>())
        {
            var choices = choice.Choices(defaults, presentation);
            Assert.True(choices.Any(item => item.Apply(defaults) == defaults), $"{choice.LabelKey} offers no value the default settings hold");
        }

        var unlisted = (defaults with { MaxFps = 75, MultiSampleCount = 0, ShadowUpdateRate = 0 }).WithQualityLabel();
        foreach (var choice in SettingsOptions.All.OfType<ChoiceOption>())
        {
            Assert.True(choice.Choices(unlisted, presentation).Any(item => item.Apply(unlisted) == unlisted), $"{choice.LabelKey} does not list a valid value the file holds");
        }

        foreach (var slider in SettingsOptions.All.OfType<SliderOption>())
        {
            var value = slider.Get(defaults);
            Assert.True(value >= slider.Minimum && value <= slider.Maximum && SettingsOptions.Snap(slider, value) == value,
                $"{slider.LabelKey}'s default {value} is off its slider");
        }

        return Task.CompletedTask;
    }

    private static Task RenamedPresetAsync()
    {
        var edited = new ClientSettings() with { Bloom = BloomQuality.Off };
        var form = Form(new PresentationSettings { Requested = edited });
        Assert.True(form.Draft == (edited with { Quality = GraphicsQuality.Custom }), "a file naming a preset its options no longer match opened under that preset");
        return Task.CompletedTask;
    }

    private static Task PickAndApplyAsync()
    {
        var presentation = new PresentationSettings();
        var form = Form(presentation);
        var layout = SettingsFormLayout.Create(Screen.Width, Screen.Height);
        ShowTab(form, SettingsTab.Game);
        var row = RowOf(form, "Options.Weather");
        Click(form, layout.Control(row).Center);
        Assert.True(form.OpenOption == form.Rows[row], "clicking a dropdown did not open its list");
        var list = layout.List(layout.Control(row), 2);
        Click(form, SettingsFormLayout.ListRow(list, 1).Center);
        Assert.True(form.OpenOption == -1 && !form.Draft.WeatherEnabled, "picking Off did not close the list and turn the weather off");
        Assert.True(presentation.Requested.WeatherEnabled, "a pick reached the requested settings before Apply");
        Assert.True(Click(form, layout.Apply.Center) == SettingsFormResult.Closed && !presentation.Requested.WeatherEnabled,
            "Apply did not close the form and request the picked value");
        return Task.CompletedTask;
    }

    private static Task CancelAsync()
    {
        var presentation = new PresentationSettings();
        var form = Form(presentation);
        var layout = SettingsFormLayout.Create(Screen.Width, Screen.Height);
        var row = RowOf(form, "Options.VerticalSync");
        Click(form, layout.Control(row).Center);
        Click(form, SettingsFormLayout.ListRow(layout.List(layout.Control(row), 2), 0).Center);
        Assert.True(form.Draft.VerticalSync, "picking On did not turn vertical sync on");
        Assert.True(Click(form, layout.Cancel.Center) == SettingsFormResult.Closed && !presentation.Requested.VerticalSync,
            "Cancel kept the edit");

        form.Open();
        Frame(form, default);
        Click(form, layout.Control(row).Center);
        Assert.True(form.Escape() == SettingsFormResult.Open && form.OpenOption == -1, "Escape did not close the open list first");
        Assert.True(form.Escape() == SettingsFormResult.Closed, "Escape did not close the form");
        Assert.True(presentation.Requested == new ClientSettings(), "closing without Apply changed the requested settings");
        return Task.CompletedTask;
    }

    private static Task WheelAsync()
    {
        var presentation = new PresentationSettings();
        var form = Form(presentation);
        var layout = SettingsFormLayout.Create(Screen.Width, Screen.Height);
        ShowTab(form, SettingsTab.Game);
        var row = RowOf(form, "Options.DebugView");
        Click(form, layout.Control(row).Center);
        var list = layout.List(layout.Control(row), SettingsFormLayout.MostListRows);
        var inside = new Point((int)(list.Center.X * layout.Scale), (int)(list.Center.Y * layout.Scale));
        // A trackpad sends small wheel steps.
        Frame(form, new MouseState(inside.X, inside.Y, -3, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        Click(form, SettingsFormLayout.ListRow(list, SettingsFormLayout.MostListRows - 1).Center, wheel: -3);
        Assert.True(form.Draft.DebugView == (PristonDebugView)SettingsFormLayout.MostListRows, $"after one small wheel step the last row picked {form.Draft.DebugView}");
        return Task.CompletedTask;
    }

    private static Task SliderAsync()
    {
        var presentation = new PresentationSettings();
        var form = Form(presentation);
        var layout = SettingsFormLayout.Create(Screen.Width, Screen.Height);
        ShowTab(form, SettingsTab.Effects);
        var row = RowOf(form, "Options.Exposure");
        var track = SettingsFormLayout.Track(layout.Control(row));
        Mouse(form, layout, new Point(track.X + track.Width * 3 / 4, track.Center.Y), ButtonState.Pressed);
        Assert.True(form.Draft.ExposureCompensation == 1.5f, $"a quarter from the end the exposure is {form.Draft.ExposureCompensation}, not +1.5");
        Mouse(form, layout, new Point(track.Right + 200, track.Center.Y), ButtonState.Pressed);
        Assert.True(form.Draft.ExposureCompensation == 3f, "dragging past the track left the range");
        Mouse(form, layout, new Point(track.X + track.Width * 3 / 8 + 2, track.Center.Y), ButtonState.Pressed);
        Assert.True(form.Draft.ExposureCompensation == -0.75f, $"the exposure {form.Draft.ExposureCompensation} is off its 0.25 step");
        Mouse(form, layout, new Point(track.X, track.Center.Y), ButtonState.Released);
        Mouse(form, layout, new Point(track.X, track.Center.Y), ButtonState.Released);
        Assert.True(form.Draft.ExposureCompensation == -0.75f, "moving with the button released still dragged");
        return Task.CompletedTask;
    }

    private static void Mouse(SettingsForm form, SettingsFormLayout layout, Point design, ButtonState left) =>
        Frame(form, new MouseState((int)MathF.Round(design.X * layout.Scale), (int)MathF.Round(design.Y * layout.Scale), 0, left,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));

    private static Task PresetAsync()
    {
        var high = new ClientSettings().WithQuality(GraphicsQuality.High);
        Assert.True(high.WithQualityLabel() == high, "settings matching their preset lost its name");
        Assert.True((high with { Bloom = BloomQuality.Low }).WithQualityLabel().Quality == GraphicsQuality.Custom, "a preset option changed kept the preset's name");
        Assert.True((high with { MusicEnabled = false }).WithQualityLabel().Quality == GraphicsQuality.High, "an option no preset sets made the quality custom");

        var presentation = new PresentationSettings();
        var form = Form(presentation);
        var layout = SettingsFormLayout.Create(Screen.Width, Screen.Height);
        ShowTab(form, SettingsTab.Graphics);
        var row = RowOf(form, "Options.Quality");
        Click(form, layout.Control(row).Center);
        Click(form, SettingsFormLayout.ListRow(layout.List(layout.Control(row), 5), 0).Center);
        Assert.True(form.Draft == new ClientSettings().WithQuality(GraphicsQuality.Low), "picking Low did not apply the Low preset");
        var shadows = RowOf(form, "Options.Shadows");
        Click(form, layout.Control(shadows).Center);
        Click(form, SettingsFormLayout.ListRow(layout.List(layout.Control(shadows), 2), 1).Center);
        Assert.True(!form.Draft.ShadowsEnabled && form.Draft.Quality == GraphicsQuality.Custom, "turning shadows off kept the Low preset's name");

        var inGame = new PresentationSettings();
        var panel = new HudSettingsPanel(inGame);
        const int ShadowsRow = 13;
        panel.Click(HudSettingsPanel.Row(ShadowsRow).Center, backwards: false);
        Assert.True(inGame.Requested.Quality == GraphicsQuality.Custom, "the in-game panel kept the preset's name with shadows off");
        const int MusicRow = 5;
        inGame.Requested = new ClientSettings();
        panel.Click(HudSettingsPanel.Row(MusicRow).Center, backwards: false);
        Assert.True(inGame.Requested.Quality == GraphicsQuality.High, "the in-game panel made the quality custom for an option no preset sets");
        return Task.CompletedTask;
    }

    private static Task DefaultsAsync()
    {
        var edited = new ClientSettings { ServerAddress = "example.org:9000", WindowX = 40, WindowY = 50, WindowMaximized = true, MusicEnabled = false, Bloom = BloomQuality.Off };
        var presentation = new PresentationSettings { Requested = edited };
        var form = Form(presentation);
        Click(form, SettingsFormLayout.Create(Screen.Width, Screen.Height).Defaults.Center);
        Assert.True(form.Draft == new ClientSettings() with { ServerAddress = "example.org:9000", WindowX = 40, WindowY = 50, WindowMaximized = true },
            "Defaults did not reset the options, or lost the server or the window's place");
        return Task.CompletedTask;
    }

    private static Task LoginButtonAsync()
    {
        foreach (var (width, height) in new[] { (800, 600), (1280, 720), (3440, 1440) })
        {
            var layout = LoginLayout.Create(width, height);
            Assert.True(!layout.Login.Intersects(layout.Settings) && layout.Panel.Contains(layout.Settings) && layout.Settings.Y == layout.Login.Y,
                "the Settings button overlaps Log in, leaves the panel or sits off its row");
            var form = SettingsFormLayout.Create(width, height);
            Assert.True(form.Panel.Contains(form.Apply) && form.Panel.Contains(form.Row(SettingsFormLayout.MostRows - 1)) && form.Row(SettingsFormLayout.MostRows - 1).Bottom < form.Status.Y,
                "the settings form's rows or buttons leave its panel");
        }

        return Task.CompletedTask;
    }
}

using System.Globalization;

namespace PristonTale.Engine3D.Rendering;

// The client's command line for a normal start: --settings PATH and --resolution WIDTHxHEIGHT, in any order. The
// resolution replaces the settings file's window size for this start only; it is saved only with a settings change the
// player accepts later.
public sealed record ClientLaunchOptions(string SettingsPath, int? WindowWidth, int? WindowHeight)
{
    public const string Usage = "Usage: PristonTale [--settings PATH] [--resolution WIDTHxHEIGHT] | --validate-display REPORT.json";

    public static bool TryParse(IReadOnlyList<string> args, out ClientLaunchOptions options, out string error)
    {
        options = new ClientLaunchOptions(null, null, null);
        error = null;
        for (var index = 0; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count)
            {
                error = $"{args[index]} needs a value";
                return false;
            }

            var value = args[index + 1];
            switch (args[index])
            {
                case "--settings" when options.SettingsPath is null:
                    options = options with { SettingsPath = value };
                    break;
                case "--resolution" when options.WindowWidth is null:
                    if (!TryParseResolution(value, out var width, out var height))
                    {
                        error = $"--resolution takes WIDTHxHEIGHT between {ClientSettings.MinimumWindowWidth}x{ClientSettings.MinimumWindowHeight} and "
                            + $"{ClientSettings.MaximumWindowSide}x{ClientSettings.MaximumWindowSide}, not '{value}'";
                        return false;
                    }

                    options = options with { WindowWidth = width, WindowHeight = height };
                    break;
                default:
                    error = $"unexpected argument '{args[index]}'";
                    return false;
            }
        }

        return true;
    }

    public ClientSettings Apply(ClientSettings settings) =>
        WindowWidth is { } width && WindowHeight is { } height ? settings with { WindowWidth = width, WindowHeight = height } : settings;

    private static bool TryParseResolution(string value, out int width, out int height)
    {
        width = 0;
        height = 0;
        var separator = value.IndexOfAny(['x', 'X']);
        return separator > 0
            && int.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out width)
            && int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out height)
            && width is >= ClientSettings.MinimumWindowWidth and <= ClientSettings.MaximumWindowSide
            && height is >= ClientSettings.MinimumWindowHeight and <= ClientSettings.MaximumWindowSide;
    }
}

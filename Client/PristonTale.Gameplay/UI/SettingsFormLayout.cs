using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI;

// The settings form in design units: a panel centred like the login form's, scaled down to fit the drawable.
public readonly record struct SettingsFormLayout(float Scale, Rectangle Panel)
{
    public const int Width = 760;
    public const int Height = 700;
    public const int MostRows = 14;
    public const int RowStride = 31;
    public const int ListRowHeight = 28;
    public const int MostListRows = 8;
    public const int TabCount = 4;

    public Rectangle Title => At(40, 34, 680, 40);
    public Rectangle Status => At(40, 588, 680, 22);
    public Rectangle Defaults => At(40, 620, 200, 44);
    public Rectangle Cancel => At(310, 620, 200, 44);
    public Rectangle Apply => At(520, 620, 200, 44);

    public static SettingsFormLayout Create(int width, int height)
    {
        var (scale, panel) = LoginLayout.Fit(width, height, Width, Height);
        return new SettingsFormLayout(scale, panel);
    }

    public Point Pointer(Point drawable) => LoginLayout.Pointer(drawable, Scale);

    // The whole drawable in design units, for the dimming behind the panel.
    public static Rectangle Screen(int width, int height, float scale) => new(0, 0, (int)MathF.Ceiling(width / scale), (int)MathF.Ceiling(height / scale));

    public Rectangle Tab(int index) => At(40 + index * 172, 86, 164, 40);

    public Rectangle Row(int index) => At(40, 140 + index * RowStride, 680, RowStride - 2);

    public Rectangle Label(int index) => At(52, 140 + index * RowStride, 300, RowStride - 2);

    public Rectangle Control(int index) => At(360, 141 + index * RowStride, 352, RowStride - 4);

    // A slider's track inside its control, leaving room for the value on the right.
    public static Rectangle Track(Rectangle control) => new(control.X + 10, control.Center.Y - 5, control.Width - 96, 10);

    public static Rectangle SliderValue(Rectangle control) => new(control.Right - 80, control.Y, 74, control.Height);

    // An open dropdown's list: below its control, or above it when it would leave the panel.
    public Rectangle List(Rectangle control, int rows)
    {
        var height = rows * ListRowHeight + 8;
        var below = control.Bottom + 2;
        var y = below + height <= Panel.Bottom - 24 ? below : control.Y - 2 - height;
        return new Rectangle(control.X, y, control.Width, height);
    }

    public static Rectangle ListRow(Rectangle list, int row) => new(list.X + 4, list.Y + 4 + row * ListRowHeight, list.Width - 8, ListRowHeight);

    private Rectangle At(int x, int y, int width, int height) => new(Panel.X + x, Panel.Y + y, width, height);
}

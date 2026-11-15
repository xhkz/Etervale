using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI;

public readonly record struct LoginLayout(float Scale, Rectangle Panel)
{
    public Rectangle Title => At(62, 267, 356, 40);
    public Rectangle Subtitle => At(62, 306, 356, 28);
    public Rectangle Account => At(78, 367, 324, 42);
    public Rectangle Password => At(78, 439, 324, 42);
    public Rectangle Login => At(78, 501, 214, 44);
    public Rectangle Settings => At(302, 501, 100, 44);
    public Rectangle Status => At(78, 558, 324, 24);
    public Rectangle Hint => At(78, 586, 324, 18);

    public static LoginLayout Create(int width, int height)
    {
        var (scale, panel) = Fit(width, height, 480, 720);
        return new LoginLayout(scale, panel);
    }

    // A panel of the given design size centred in the drawable, at a scale of at most 1 that leaves a 12-pixel margin.
    public static (float Scale, Rectangle Panel) Fit(int width, int height, int panelWidth, int panelHeight)
    {
        var scale = Math.Max(0.1f, Math.Min(1, Math.Min((width - 24) / (float)panelWidth, (height - 24) / (float)panelHeight)));
        var x = (int)((width / scale - panelWidth) / 2);
        var y = (int)((height / scale - panelHeight) / 2);
        return (scale, new Rectangle(x, y, panelWidth, panelHeight));
    }

    public Point Pointer(Point drawable) => Pointer(drawable, Scale);

    public static Point Pointer(Point drawable, float scale) => new((int)(drawable.X / scale), (int)(drawable.Y / scale));

    private Rectangle At(int x, int y, int width, int height) => new(Panel.X + x, Panel.Y + y, width, height);
}

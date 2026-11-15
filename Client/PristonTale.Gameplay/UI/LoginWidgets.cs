using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI;

// The login screen's controls, shared by the login form and the settings form it opens.
internal static class LoginWidgets
{
    public static readonly Color Highlight = new(255, 230, 180);
    public static readonly Color Disabled = new(155, 155, 155);

    public static void Button(HudCanvas canvas, string label, Rectangle box, bool highlighted, bool pressed, Color tint)
    {
        var color = highlighted ? Highlight : tint;
        if (pressed)
        {
            color *= 0.7f;
        }

        canvas.Frame(box, "Button", 12, color);
        canvas.Text(label, box, highlighted ? Color.White : HudCanvas.Gold, 20f, centered: true);
    }

    public static void Field(HudCanvas canvas, Rectangle box, bool focused, Color tint) => canvas.Frame(box, "Inset", 8, focused ? HudCanvas.Gold : tint);
}

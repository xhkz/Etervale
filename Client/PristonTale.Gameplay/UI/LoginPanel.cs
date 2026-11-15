using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

public enum LoginIntent
{
    None,
    SubmitLogin,
    OpenSettings
}

public sealed class LoginPanel
{
    private enum FocusTarget
    {
        Account,
        Password,
        Login,
        Settings
    }

    private const int FocusTargets = (int)FocusTarget.Settings + 1;

    private readonly HudCanvas _canvas;
    // Starts held: the button of the click that revealed the panel is not a click on it until released.
    private MouseState _previousMouse = HeldMouse;
    private FocusTarget _focus;

    public LoginPanel(SpriteBatch batch, SpriteFont font, HudAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        if (!atlas.IsReady)
        {
            throw new InvalidOperationException("Login requires interface resources initialized before the first screen.");
        }

        _canvas = new HudCanvas(batch, font, atlas);
    }

    internal static readonly MouseState HeldMouse = new(0, 0, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released,
        ButtonState.Released);

    public TextInputField Account { get; } = new() { MaxLength = 20, Focused = true };
    public TextInputField Password { get; } = new() { MaxLength = 20, Masked = true };
    public string Status { get; set; } = string.Empty;
    public bool InputEnabled { get; set; } = true;

    // typed: the characters the window typed since the last frame (TypedText), for the focused field.
    public LoginIntent Update(in KeyboardState keyboard, in KeyboardState previousKeyboard, ReadOnlySpan<char> typed, double dtSeconds, Viewport viewport)
    {
        return Update(keyboard, previousKeyboard, Mouse.GetState(), typed, dtSeconds, viewport);
    }

    internal LoginIntent Update(in KeyboardState keyboard, in KeyboardState previousKeyboard, MouseState mouse, ReadOnlySpan<char> typed, double dtSeconds,
        Viewport viewport)
    {
        var intent = LoginIntent.None;
        if (InputEnabled)
        {
            var layout = LoginLayout.Create(viewport.Width, viewport.Height);
            var cursor = layout.Pointer(mouse.Position);
            var clicked = Clicked(mouse, _previousMouse);
            if (Pressed(keyboard, previousKeyboard, Keys.Tab))
            {
                var direction = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? -1 : 1;
                SetFocus((FocusTarget)(((int)_focus + direction + FocusTargets) % FocusTargets));
            }

            if (clicked)
            {
                if (layout.Account.Contains(cursor))
                {
                    SetFocus(FocusTarget.Account);
                }
                else if (layout.Password.Contains(cursor))
                {
                    SetFocus(FocusTarget.Password);
                }
                else if (layout.Login.Contains(cursor))
                {
                    SetFocus(FocusTarget.Login);
                    intent = LoginIntent.SubmitLogin;
                }
                else if (layout.Settings.Contains(cursor))
                {
                    SetFocus(FocusTarget.Settings);
                    intent = LoginIntent.OpenSettings;
                }
            }

            Account.Update(typed, dtSeconds);
            Password.Update(typed, dtSeconds);
            if (Pressed(keyboard, previousKeyboard, Keys.Enter)
                || _focus >= FocusTarget.Login && Pressed(keyboard, previousKeyboard, Keys.Space))
            {
                intent = _focus == FocusTarget.Settings ? LoginIntent.OpenSettings : LoginIntent.SubmitLogin;
            }
        }

        _previousMouse = mouse;
        return intent;
    }

    // After another form closes: the button that closed it is no click on this one until released.
    public void HoldPointer() => _previousMouse = HeldMouse;

    internal static bool Clicked(in MouseState mouse, in MouseState previous) =>
        mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;

    private void SetFocus(FocusTarget focus)
    {
        _focus = focus;
        Account.Focused = focus == FocusTarget.Account;
        Password.Focused = focus == FocusTarget.Password;
    }

    public void Draw(Viewport viewport)
    {
        var layout = LoginLayout.Create(viewport.Width, viewport.Height);
        var tint = InputEnabled ? Color.White : LoginWidgets.Disabled;
        _canvas.Begin(layout.Scale);
        _canvas.Image("LoginPanel", layout.Panel);
        _canvas.Text("ETERVALE", layout.Title, pixels: 30f, centered: true);
        _canvas.Text(ClientText.Get("Login.Subtitle"), layout.Subtitle, HudCanvas.Muted, 17f, centered: true);
        DrawField(ClientText.Get("Login.Account"), Account, layout.Account, tint);
        DrawField(ClientText.Get("Login.Password"), Password, layout.Password, tint);
        DrawButton(ClientText.Get("Login.LogIn"), layout.Login, _focus == FocusTarget.Login, layout, tint);
        DrawButton(ClientText.Get("Login.Settings"), layout.Settings, _focus == FocusTarget.Settings, layout, tint);
        _canvas.Text(Status, layout.Status, HudCanvas.Gold, 16f, centered: true);
        _canvas.Text(ClientText.Get("Login.Hint"), layout.Hint, HudCanvas.Muted, 13f, centered: true);
        _canvas.End();
    }

    private void DrawField(string label, TextInputField field, Rectangle box, Color tint)
    {
        var focused = InputEnabled && field.Focused;
        _canvas.Text(label, new Rectangle(box.X, box.Y - 28, box.Width, 24), HudCanvas.Muted, 17f);
        LoginWidgets.Field(_canvas, box, focused, tint);
        var text = field.DisplayText;
        if (focused && field.CaretVisible)
        {
            text += "_";
        }

        _canvas.Text(text, new Rectangle(box.X + 14, box.Y + 6, box.Width - 28, box.Height - 12), tint, 20f);
    }

    private void DrawButton(string label, Rectangle box, bool focused, LoginLayout layout, Color tint)
    {
        var hovered = box.Contains(layout.Pointer(_previousMouse.Position));
        var highlighted = InputEnabled && (focused || hovered);
        var pressed = InputEnabled && hovered && _previousMouse.LeftButton == ButtonState.Pressed;
        LoginWidgets.Button(_canvas, label, box, highlighted, pressed, tint);
    }

    private static bool Pressed(in KeyboardState current, in KeyboardState previous, Keys key)
    {
        return current.IsKeyDown(key) && !previous.IsKeyDown(key);
    }
}

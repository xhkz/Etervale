using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Screens.CharacterSelect;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.Screens.Opening;

public sealed class OpeningScreen : GameScreen
{
    private OpeningCinematic _cinematic;
    private OpeningLoginController _loginController;

    private LoginAudio _audio;
    private KeyboardState _lastKeyboard;
    private MouseState _lastMouse;
    private string _status;

    private bool _loginRevealed;

    public OpeningScreen(ScreenContext context) : base(context)
    {
    }

    public override string StatusText => _status;

    public override void Enter()
    {
        _cinematic = new OpeningCinematic(
            Context.GraphicsDevice,
            Context.Content,
            Context.Log,
            AssetPath,
            Context.AssetRoot,
            Context.Sky,
            Context.Presentation,
            Context.Effects);
        _cinematic.Load();
        _audio = LoginAudio.Opening(Context.Audio);
        _loginController = new OpeningLoginController(Context, _audio);
        _status = ClientText.Format("Status.Opening", _cinematic.StatusSuffix);
    }

    public override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _audio.Update(dt);

        _cinematic.Update(gameTime);

        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();
        if (Context.Presentation.AwaitingConfirmation)
        {
            (_lastKeyboard, _lastMouse) = (keyboard, mouse);
            return;
        }

        if (!_loginRevealed)
        {
            if (RevealRequested(keyboard, _lastKeyboard, mouse, _lastMouse) || _cinematic.IsOver)
            {
                _loginRevealed = true;
            }
        }
        else if (_loginController.Update(keyboard, _lastKeyboard, dt) == OpeningLoginController.LoginResult.Succeeded)
        {
            Context.RequestScreen(new CharacterSelectScreen(Context));
        }

        (_lastKeyboard, _lastMouse) = (keyboard, mouse);
    }

    // A key or a mouse button pressed this frame skips the cinematic to the login form.
    internal static bool RevealRequested(in KeyboardState keyboard, in KeyboardState lastKeyboard, in MouseState mouse, in MouseState lastMouse)
    {
        foreach (var key in keyboard.GetPressedKeys())
        {
            if (!lastKeyboard.IsKeyDown(key))
            {
                return true;
            }
        }

        return Pressed(mouse.LeftButton, lastMouse.LeftButton) || Pressed(mouse.RightButton, lastMouse.RightButton)
            || Pressed(mouse.MiddleButton, lastMouse.MiddleButton);
    }

    private static bool Pressed(ButtonState current, ButtonState previous) => current == ButtonState.Pressed && previous == ButtonState.Released;

    public override void Draw()
    {
        _cinematic.Draw();

        var viewport = Context.GraphicsDevice.Viewport;
        if (!_loginRevealed)
        {
            Context.SpriteBatch.Begin();
            Context.SpriteBatch.End();
        }
        else
        {
            _loginController.Draw(viewport);
        }
    }

    public override void Dispose()
    {
        _loginController?.Dispose();
        _audio?.Dispose();
        _cinematic?.Dispose();
    }
}

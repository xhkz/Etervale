using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Contracts.Sessions;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.Screens.Opening;

internal sealed class OpeningLoginController : IDisposable
{
    private enum Phase { Login, Authenticating }

    private readonly ScreenContext _context;
    private readonly LoginAudio _audio;
    private readonly LoginPanel _login;
    private readonly SettingsForm _settings;
    private bool _settingsOpen;
    private Phase _phase = Phase.Login;
    private Task<LoginOutcome> _loginTask;
    private CancellationTokenSource _loginCts;

    public OpeningLoginController(ScreenContext context, LoginAudio audio)
    {
        _context = context;
        _audio = audio;
        _login = new LoginPanel(context.SpriteBatch, context.Font, context.UiAtlas);
        _settings = new SettingsForm(context.SpriteBatch, context.Font, context.UiAtlas, context.Presentation);
    }

    public enum LoginResult { None, Succeeded }

    public LoginResult Update(in KeyboardState keyboard, in KeyboardState lastKeyboard, float dt)
    {
        var typed = _context.TypedText.Drain();
        var viewport = _context.GraphicsDevice.Viewport;
        if (_settingsOpen)
        {
            if (_settings.Update(keyboard, lastKeyboard, typed, dt, viewport) == SettingsFormResult.Closed)
            {
                CloseSettings();
            }
        }
        else if (_phase == Phase.Login)
        {
            switch (_login.Update(keyboard, lastKeyboard, typed, dt, viewport))
            {
                case LoginIntent.SubmitLogin:
                    BeginLogin();
                    break;
                case LoginIntent.OpenSettings:
                    _audio.Play(LoginCue.Button);
                    _settings.Open();
                    _settingsOpen = true;
                    break;
            }
        }

        return _phase == Phase.Authenticating ? PollLogin() : LoginResult.None;
    }

    private void BeginLogin()
    {
        if (string.IsNullOrWhiteSpace(_login.Account.Text) || string.IsNullOrEmpty(_login.Password.Text))
        {
            _login.Status = "Enter your account and password.";
            return;
        }

        _audio.Play(LoginCue.Login);
        _login.Status = "Connecting…";
        _login.InputEnabled = false;
        _phase = Phase.Authenticating;
        _loginCts = new CancellationTokenSource();
        if (_context.Backend.RequiresNewConnection)
        {
            _context.RenewBackend();
        }

        _loginTask = _context.Backend.LoginAsync(_login.Account.Text, _login.Password.Text,
            _context.Presentation.Requested.Language, _loginCts.Token);
    }

    private LoginResult PollLogin()
    {
        if (_loginTask is not { IsCompleted: true })
        {
            return LoginResult.None;
        }

        var succeeded = _loginTask.IsCompletedSuccessfully && _loginTask.Result.Ok;
        if (!succeeded)
        {
            _login.Status = _loginTask.IsCompletedSuccessfully
                ? ClientText.Get("Login.Failure." + _loginTask.Result.Failure)
                : ClientText.Get("Login.Unreachable");
            _login.InputEnabled = true;
            _phase = Phase.Login;
        }

        _loginTask = null;
        _loginCts?.Dispose();
        _loginCts = null;
        return succeeded ? LoginResult.Succeeded : LoginResult.None;
    }

    // Escape closes the settings form (an open list first); it does nothing while the form is shut.
    public void HandleEscape()
    {
        if (_settingsOpen && _settings.Escape() == SettingsFormResult.Closed)
        {
            CloseSettings();
        }
    }

    private void CloseSettings()
    {
        _settingsOpen = false;
        _audio.Play(LoginCue.Back);
        _login.HoldPointer();
    }

    public void Draw(Viewport viewport)
    {
        if (_settingsOpen)
        {
            _settings.Draw(viewport);
        }
        else
        {
            _login.Draw(viewport);
        }
    }

    public void Dispose()
    {
        _loginCts?.Cancel();
        _loginCts?.Dispose();
        if (_loginTask is not null)
        {
            _ = ObserveLoginAsync(_loginTask);
        }
    }

    private async Task ObserveLoginAsync(Task<LoginOutcome> login)
    {
        try
        {
            await login;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.Log.Error($"[Login] canceled request failed: {ex}");
        }
    }
}

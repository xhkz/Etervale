using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI;

public enum SettingsFormResult
{
    Open,
    Closed
}

// Every option of client-settings.json (SettingsOptions) on four tabs, in the login form's frame, buttons and fields:
// dropdowns, sliders and two text fields edit a copy of the requested settings; Apply hands it to the presentation
// (which validates it, applies it and asks to confirm a display change), Cancel drops it, Defaults resets it.
public sealed class SettingsForm
{
    private readonly HudCanvas _canvas;
    private readonly PresentationSettings _presentation;
    private readonly TextInputField[] _texts = new TextInputField[SettingsOptions.All.Length];
    private readonly List<int> _rows = new(SettingsFormLayout.MostRows);
    private readonly List<OptionChoice>[] _choices = new List<OptionChoice>[SettingsOptions.All.Length];
    private readonly int[] _chosen = new int[SettingsOptions.All.Length];
    private readonly string[] _values = new string[SettingsOptions.All.Length];
    private readonly string[] _typed = new string[SettingsOptions.All.Length];
    private readonly string[] _tabs = new string[SettingsFormLayout.TabCount];
    private ClientSettings _draft = new();
    private ClientSettings _shownDraft;
    private string _shownLanguage = string.Empty;
    private SettingsTab _tab;
    private SettingsTab _shownTab = (SettingsTab)(-1);
    private MouseState _previousMouse = LoginPanel.HeldMouse;
    private int _open = -1;
    private int _scroll;
    private int _dragging = -1;
    private int _typing = -1;

    public SettingsForm(SpriteBatch batch, SpriteFont font, HudAtlas atlas, PresentationSettings presentation)
    {
        _canvas = new HudCanvas(batch, font, atlas);
        _presentation = presentation;
        for (var index = 0; index < SettingsOptions.All.Length; index++)
        {
            if (SettingsOptions.All[index] is TextOption text)
            {
                _texts[index] = new TextInputField { MaxLength = text.MaxLength, Symbols = text.Symbols };
            }
        }
    }

    internal ClientSettings Draft => _draft;
    internal int OpenOption => _open;
    internal IReadOnlyList<int> Rows => _rows;

    // Starts from the requested settings, on the first tab; the button that opened the form is no click on it. A file
    // may name a preset its options no longer match, so the quality is named again (Custom).
    public void Open()
    {
        _draft = _presentation.Requested.WithQualityLabel();
        _tab = SettingsTab.Display;
        _open = -1;
        _dragging = -1;
        Type(-1);
        _previousMouse = LoginPanel.HeldMouse;
        SyncTexts();
    }

    // Escape closes an open list first, then the form without applying.
    public SettingsFormResult Escape()
    {
        if (_open >= 0)
        {
            _open = -1;
            return SettingsFormResult.Open;
        }

        return SettingsFormResult.Closed;
    }

    public SettingsFormResult Update(in KeyboardState keyboard, in KeyboardState previousKeyboard, ReadOnlySpan<char> typed, double dtSeconds, Viewport viewport) =>
        Update(keyboard, previousKeyboard, Mouse.GetState(), typed, dtSeconds, viewport);

    internal SettingsFormResult Update(in KeyboardState keyboard, in KeyboardState previousKeyboard, MouseState mouse, ReadOnlySpan<char> typed, double dtSeconds,
        Viewport viewport)
    {
        var layout = SettingsFormLayout.Create(viewport.Width, viewport.Height);
        Refresh();
        var cursor = layout.Pointer(mouse.Position);
        var clicked = LoginPanel.Clicked(mouse, _previousMouse);
        var wheel = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        var result = SettingsFormResult.Open;
        if (mouse.LeftButton == ButtonState.Released && _dragging >= 0)
        {
            _dragging = -1;
            // The drag refreshed only its slider; the other rows (the quality's name among them) catch up now.
            _shownDraft = null;
        }

        if (_typing >= 0)
        {
            var field = _texts[_typing];
            field.Update(typed, dtSeconds);
            if (typed.Length > 0)
            {
                var option = (TextOption)SettingsOptions.All[_typing];
                var text = field.Text;
                if (text != option.Get(_draft))
                {
                    Edit(option.Set(_draft, text));
                }
            }
        }

        if (Pressed(keyboard, previousKeyboard, Keys.Enter))
        {
            if (_typing >= 0)
            {
                Type(-1);
            }
            else
            {
                return Apply();
            }
        }

        if (_open >= 0)
        {
            UpdateList(layout, cursor, clicked, wheel);
        }
        else if (clicked)
        {
            result = Click(layout, cursor);
        }

        if (_dragging >= 0 && SettingsOptions.All[_dragging] is SliderOption slider)
        {
            var row = _rows.IndexOf(_dragging);
            if (row >= 0)
            {
                var track = SettingsFormLayout.Track(layout.Control(row));
                var fraction = Math.Clamp((cursor.X - track.X) / (float)track.Width, 0f, 1f);
                Slide(slider, slider.Minimum + fraction * (slider.Maximum - slider.Minimum));
            }
        }

        _previousMouse = mouse;
        return result;
    }

    private SettingsFormResult Click(SettingsFormLayout layout, Point cursor)
    {
        for (var tab = 0; tab < SettingsFormLayout.TabCount; tab++)
        {
            if (layout.Tab(tab).Contains(cursor))
            {
                _tab = (SettingsTab)tab;
                Type(-1);
                return SettingsFormResult.Open;
            }
        }

        if (layout.Apply.Contains(cursor))
        {
            return Apply();
        }

        if (layout.Cancel.Contains(cursor))
        {
            return SettingsFormResult.Closed;
        }

        if (layout.Defaults.Contains(cursor))
        {
            Type(-1);
            Edit(SettingsOptions.Defaults(_draft));
            SyncTexts();
            return SettingsFormResult.Open;
        }

        Type(-1);
        for (var row = 0; row < _rows.Count; row++)
        {
            var control = layout.Control(row);
            if (!control.Contains(cursor))
            {
                continue;
            }

            var index = _rows[row];
            switch (SettingsOptions.All[index])
            {
                case ChoiceOption:
                    _open = index;
                    _scroll = Math.Clamp(_chosen[index] - SettingsFormLayout.MostListRows / 2, 0, Math.Max(0, _choices[index].Count - SettingsFormLayout.MostListRows));
                    break;
                case SliderOption:
                    _dragging = index;
                    break;
                case TextOption:
                    Type(index);
                    break;
            }

            break;
        }

        return SettingsFormResult.Open;
    }

    private void UpdateList(SettingsFormLayout layout, Point cursor, bool clicked, int wheel)
    {
        var choices = _choices[_open];
        var visible = Math.Min(choices.Count, SettingsFormLayout.MostListRows);
        var hidden = choices.Count - visible;
        if (wheel != 0 && hidden > 0)
        {
            _scroll = Math.Clamp(_scroll - Math.Sign(wheel), 0, hidden);
        }

        if (!clicked)
        {
            return;
        }

        var row = _rows.IndexOf(_open);
        var list = row >= 0 ? layout.List(layout.Control(row), visible) : Rectangle.Empty;
        for (var item = 0; item < visible; item++)
        {
            if (SettingsFormLayout.ListRow(list, item).Contains(cursor))
            {
                Edit(choices[_scroll + item].Apply(_draft));
                break;
            }
        }

        _open = -1;
    }

    private void Slide(SliderOption slider, float value)
    {
        var snapped = SettingsOptions.Snap(slider, value);
        if (snapped != slider.Get(_draft))
        {
            Edit(slider.Set(_draft, snapped));
        }
    }

    // A preset option changed by hand turns the quality to Custom.
    private void Edit(ClientSettings edited) => _draft = edited.WithQualityLabel();

    private SettingsFormResult Apply()
    {
        _presentation.Requested = _draft;
        return SettingsFormResult.Closed;
    }

    private void Type(int index)
    {
        _typing = index;
        for (var field = 0; field < _texts.Length; field++)
        {
            if (_texts[field] is { } text)
            {
                text.Focused = field == index;
            }
        }
    }

    private void SyncTexts()
    {
        for (var index = 0; index < _texts.Length; index++)
        {
            if (_texts[index] is { } field)
            {
                field.SetText(((TextOption)SettingsOptions.All[index]).Get(_draft));
            }
        }
    }

    // The tab's rows and each one's values and wording, worked out again only when the draft, the tab or the language
    // changed.
    private void Refresh()
    {
        if (ReferenceEquals(_shownDraft, _draft) && _shownTab == _tab && _shownLanguage == ClientText.Language)
        {
            return;
        }

        // While a slider is dragged only its value is worded again, so a drag builds no choice lists.
        if (_dragging >= 0 && _shownDraft is not null && _shownTab == _tab && _shownLanguage == ClientText.Language
            && SettingsOptions.All[_dragging] is SliderOption dragged)
        {
            _shownDraft = _draft;
            _values[_dragging] = dragged.Format(dragged.Get(_draft));
            return;
        }

        _shownDraft = _draft;
        _shownTab = _tab;
        _shownLanguage = ClientText.Language;
        for (var tab = 0; tab < _tabs.Length; tab++)
        {
            _tabs[tab] = ClientText.Get("Options.Tab." + (SettingsTab)tab);
        }

        _rows.Clear();
        for (var index = 0; index < SettingsOptions.All.Length; index++)
        {
            var option = SettingsOptions.All[index];
            if (option.Tab != _tab)
            {
                continue;
            }

            _rows.Add(index);
            switch (option)
            {
                case ChoiceOption choice:
                    var choices = choice.Choices(_draft, _presentation);
                    _choices[index] = choices;
                    _chosen[index] = -1;
                    for (var candidate = 0; candidate < choices.Count; candidate++)
                    {
                        if (choices[candidate].Apply(_draft) == _draft)
                        {
                            _chosen[index] = candidate;
                            break;
                        }
                    }

                    _values[index] = _chosen[index] >= 0 ? choices[_chosen[index]].Label : string.Empty;
                    break;
                case SliderOption slider:
                    _values[index] = slider.Format(slider.Get(_draft));
                    break;
                case TextOption text:
                    _values[index] = text.Get(_draft);
                    _typed[index] = _values[index] + "_";
                    break;
            }
        }
    }

    public void Draw(Viewport viewport)
    {
        Refresh();
        var layout = SettingsFormLayout.Create(viewport.Width, viewport.Height);
        var cursor = layout.Pointer(_previousMouse.Position);
        var held = _previousMouse.LeftButton == ButtonState.Pressed;
        _canvas.Begin(layout.Scale);
        _canvas.Image("Fill", SettingsFormLayout.Screen(viewport.Width, viewport.Height, layout.Scale), Color.Black * 0.55f);
        _canvas.Frame(layout.Panel, "Panel", 32);
        _canvas.Text(ClientText.Get("Settings.Title"), layout.Title, pixels: 30f, centered: true);
        for (var tab = 0; tab < SettingsFormLayout.TabCount; tab++)
        {
            var box = layout.Tab(tab);
            var chosen = (SettingsTab)tab == _tab;
            var hovered = _open < 0 && box.Contains(cursor);
            _canvas.Frame(box, chosen ? "Selected" : "Button", 12, hovered && !chosen ? LoginWidgets.Highlight : Color.White);
            _canvas.Text(_tabs[tab], box, chosen || hovered ? Color.White : HudCanvas.Gold, 18f, centered: true);
        }

        for (var row = 0; row < _rows.Count; row++)
        {
            DrawRow(layout, row, cursor);
        }

        _canvas.Text(ClientText.Get("Options.Hint"), layout.Status, HudCanvas.Muted, 14f, centered: true);
        DrawButton(layout.Defaults, "Options.Defaults", cursor, held);
        DrawButton(layout.Cancel, "Options.Cancel", cursor, held);
        DrawButton(layout.Apply, "Options.Apply", cursor, held);
        if (_open >= 0)
        {
            DrawList(layout, cursor);
        }

        _canvas.End();
    }

    private void DrawRow(SettingsFormLayout layout, int row, Point cursor)
    {
        var index = _rows[row];
        var option = SettingsOptions.All[index];
        var control = layout.Control(row);
        var hovered = _open < 0 && layout.Row(row).Contains(cursor);
        if (hovered)
        {
            _canvas.Image("Fill", layout.Row(row), HudCanvas.Gold * 0.12f);
        }

        _canvas.Text(ClientText.Get(option.LabelKey), layout.Label(row), hovered ? Color.White : HudCanvas.Muted, 17f);
        switch (option)
        {
            case ChoiceOption:
                LoginWidgets.Field(_canvas, control, _open == index || hovered && control.Contains(cursor), Color.White);
                _canvas.Text(_values[index], new Rectangle(control.X + 12, control.Y + 3, control.Width - 44, control.Height - 6), Color.White, 17f);
                _canvas.ArrowDown(new Rectangle(control.Right - 26, control.Center.Y - 4, 14, 8), HudCanvas.Gold);
                break;
            case SliderOption slider:
                var track = SettingsFormLayout.Track(control);
                var fraction = (slider.Get(_draft) - slider.Minimum) / (slider.Maximum - slider.Minimum);
                _canvas.Frame(track, "Inset", 4);
                _canvas.Image("Fill", new Rectangle(track.X + 2, track.Y + 2, (int)((track.Width - 4) * fraction), track.Height - 4), HudCanvas.Gold);
                var knob = new Rectangle(track.X + (int)(track.Width * fraction) - 7, control.Y + 1, 14, control.Height - 2);
                _canvas.Frame(knob, "Button", 5, _dragging == index || hovered ? LoginWidgets.Highlight : Color.White);
                _canvas.Text(_values[index], SettingsFormLayout.SliderValue(control), Color.White, 17f, centered: true);
                break;
            case TextOption:
                var field = _texts[index];
                LoginWidgets.Field(_canvas, control, field.Focused, Color.White);
                _canvas.Text(field.CaretVisible ? _typed[index] : _values[index], new Rectangle(control.X + 12, control.Y + 3, control.Width - 24, control.Height - 6), Color.White, 17f);
                break;
        }
    }

    private void DrawList(SettingsFormLayout layout, Point cursor)
    {
        var row = _rows.IndexOf(_open);
        if (row < 0)
        {
            return;
        }

        // The rows' text is drawn first so the list covers it.
        _canvas.FlushText();
        var choices = _choices[_open];
        var visible = Math.Min(choices.Count, SettingsFormLayout.MostListRows);
        var list = layout.List(layout.Control(row), visible);
        _canvas.Frame(list, "Panel", 10);
        for (var item = 0; item < visible; item++)
        {
            var box = SettingsFormLayout.ListRow(list, item);
            var choice = _scroll + item;
            var hovered = box.Contains(cursor);
            if (hovered || choice == _chosen[_open])
            {
                _canvas.Image("Fill", box, HudCanvas.Gold * (hovered ? 0.3f : 0.15f));
            }

            _canvas.Text(choices[choice].Label, new Rectangle(box.X + 8, box.Y + 2, box.Width - 16, box.Height - 4),
                choice == _chosen[_open] ? HudCanvas.Gold : Color.White, 17f);
        }

        if (choices.Count > visible)
        {
            var thumbHeight = Math.Max(12, (list.Height - 8) * visible / choices.Count);
            var thumbY = list.Y + 4 + (list.Height - 8 - thumbHeight) * _scroll / (choices.Count - visible);
            _canvas.Image("Fill", new Rectangle(list.Right - 7, thumbY, 3, thumbHeight), HudCanvas.Gold * 0.8f);
        }
    }

    private void DrawButton(Rectangle box, string key, Point cursor, bool held)
    {
        var hovered = _open < 0 && box.Contains(cursor);
        LoginWidgets.Button(_canvas, ClientText.Get(key), box, hovered, hovered && held, Color.White);
    }

    private static bool Pressed(in KeyboardState current, in KeyboardState previous, Keys key) => current.IsKeyDown(key) && !previous.IsKeyDown(key);
}

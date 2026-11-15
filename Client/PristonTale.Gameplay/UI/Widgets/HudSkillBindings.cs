using PristonTale.Simulation.Skills;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed record HudSkillChoice(uint Id, string Name, int Rank, SkillMouseUse MouseUse);
public readonly record struct HudSkillShortcut(uint SkillId, SkillMouseUse Side);

public sealed class HudSkillBindings
{
    public const uint BasicAttackId = SkillBindings.NormalAttack;
    public static readonly HudSkillChoice BasicAttack = new(BasicAttackId, "Basic attack", 1, SkillMouseUse.Both);
    private readonly Dictionary<uint, HudSkillChoice> _choices = new() { [BasicAttackId] = BasicAttack };
    private readonly HudSkillShortcut[] _shortcuts = new HudSkillShortcut[SkillBindings.ShortcutCount];
    private KeyboardState _previousKeys;
    private MouseState _previousMouse;
    private uint _pressedSkill;
    private SkillMouseUse _pressedSide;
    private bool _boundDuringPress;

    public uint LeftSkillId { get; private set; } = BasicAttackId;
    public uint RightSkillId { get; private set; } = BasicAttackId;
    public HudSkillChoice Left => _choices.GetValueOrDefault(LeftSkillId);
    public HudSkillChoice Right => _choices.GetValueOrDefault(RightSkillId);
    public HudSkillShortcut Shortcut(int index) => _shortcuts[index];

    // Restored once the character's saved choices arrived; Revision counts the owner's changes since, each one to save.
    public bool Restored { get; private set; }
    public int Revision { get; private set; }

    // Set when a click asked for a socket the skill cannot use; Refusals counts them so the HUD shows each once.
    public string Refusal { get; private set; } = "";
    public int Refusals { get; private set; }

    // Offers the learned active skills to the sockets and short keys; an unlearned or lost skill falls back to the
    // basic attack.
    public void SyncLearned(IReadOnlyList<Skill> classSkills, IReadOnlyList<PristonTale.Simulation.Characters.OwnedSkillView> learned)
    {
        foreach (var code in _choices.Keys.Where(code => code != BasicAttackId).ToArray())
        {
            _choices.Remove(code);
        }

        foreach (var owned in learned)
        {
            foreach (var skill in classSkills)
            {
                if (skill.Code == owned.Code && owned.Rank > 0 && skill.MouseUse != SkillMouseUse.None)
                {
                    _choices[skill.Code] = new HudSkillChoice(skill.Code, DataText.SkillName(skill), owned.Rank, skill.MouseUse);
                }
            }
        }

        if (!_choices.ContainsKey(LeftSkillId))
        {
            LeftSkillId = BasicAttackId;
        }

        if (!_choices.ContainsKey(RightSkillId))
        {
            RightSkillId = BasicAttackId;
        }
    }

    // Takes the choices saved with the character, after SyncLearned: a socket whose skill is not learned keeps the basic
    // attack, and a key whose skill is not learned does nothing until it is.
    public void Restore(SkillBindings saved)
    {
        LeftSkillId = CanSelect(saved.Left, SkillMouseUse.Left) ? saved.Left : BasicAttackId;
        RightSkillId = CanSelect(saved.Right, SkillMouseUse.Right) ? saved.Right : BasicAttackId;
        for (var index = 0; index < _shortcuts.Length; index++)
        {
            var shortcut = index < saved.Shortcuts.Length ? saved.Shortcuts[index] : default;
            _shortcuts[index] = new HudSkillShortcut(shortcut.Code, shortcut.Code == 0 ? SkillMouseUse.None : shortcut.Side);
        }

        Restored = true;
    }

    public SkillBindings Snapshot()
    {
        var shortcuts = new SkillShortcut[_shortcuts.Length];
        for (var index = 0; index < shortcuts.Length; index++)
        {
            shortcuts[index] = new SkillShortcut(_shortcuts[index].SkillId, _shortcuts[index].Side);
        }

        return new SkillBindings(LeftSkillId, RightSkillId, [.. shortcuts]);
    }

    public bool Assign(int index, uint skillId, SkillMouseUse side)
    {
        if (index < 0 || index >= _shortcuts.Length || !CanSelect(skillId, side))
        {
            return false;
        }

        // Original keeps one shortcut per skill and one skill per function key.
        for (var other = 0; other < _shortcuts.Length; other++)
        {
            if (_shortcuts[other].SkillId == skillId)
            {
                _shortcuts[other] = default;
            }
        }

        _shortcuts[index] = new HudSkillShortcut(skillId, side);
        Revision++;
        return true;
    }

    public bool Select(uint skillId, SkillMouseUse side)
    {
        if (!CanSelect(skillId, side))
        {
            return false;
        }

        if (side == SkillMouseUse.Left ? LeftSkillId == skillId : RightSkillId == skillId)
        {
            return true;
        }

        if (side == SkillMouseUse.Left)
        {
            LeftSkillId = skillId;
        }
        else
        {
            RightSkillId = skillId;
        }

        Revision++;
        return true;
    }

    public void Update(KeyboardState keyboard, MouseState mouse, uint hoveredSkill)
    {
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
        {
            BeginPress(hoveredSkill, SkillMouseUse.Left);
        }
        else if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            BeginPress(hoveredSkill, SkillMouseUse.Right);
        }

        var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        for (var index = 0; index < _shortcuts.Length && !control; index++)
        {
            var key = Keys.F1 + index;
            if (!keyboard.IsKeyDown(key) || _previousKeys.IsKeyDown(key))
            {
                continue;
            }

            if (hoveredSkill != 0)
            {
                var held = _pressedSide == SkillMouseUse.Left ? mouse.LeftButton : mouse.RightButton;
                if (_pressedSkill == hoveredSkill && held == ButtonState.Pressed && Assign(index, hoveredSkill, _pressedSide))
                {
                    _boundDuringPress = true;
                }
            }
            else
            {
                var shortcut = _shortcuts[index];
                Select(shortcut.SkillId, shortcut.Side);
            }
        }

        var released = _pressedSide == SkillMouseUse.Left ? mouse.LeftButton == ButtonState.Released : mouse.RightButton == ButtonState.Released;
        if (_pressedSkill != 0 && released)
        {
            if (!_boundDuringPress && hoveredSkill == _pressedSkill && !Select(_pressedSkill, _pressedSide)
                && _choices.TryGetValue(_pressedSkill, out var refused))
            {
                Refusal = ClientText.Format(refused.MouseUse == SkillMouseUse.Left ? "SkillWindow.OnlyLeft" : "SkillWindow.OnlyRight", refused.Name);
                Refusals++;
            }

            _pressedSkill = 0;
            _pressedSide = SkillMouseUse.None;
        }

        _previousKeys = keyboard;
        _previousMouse = mouse;
    }

    private void BeginPress(uint skillId, SkillMouseUse side)
    {
        _pressedSkill = skillId;
        _pressedSide = side;
        _boundDuringPress = false;
    }

    private bool CanSelect(uint skillId, SkillMouseUse side)
    {
        return side is SkillMouseUse.Left or SkillMouseUse.Right
            && _choices.TryGetValue(skillId, out var skill) && skill.Rank > 0 && (skill.MouseUse & side) == side;
    }
}

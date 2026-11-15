using System.Collections.Immutable;

namespace PristonTale.Simulation.Skills;

// A function key's skill and the mouse socket it selects; the default leaves the key empty.
public readonly record struct SkillShortcut(uint Code, SkillMouseUse Side);

// The skills the owner put on the mouse sockets and the F1 to F8 keys, kept with the character so they come back at the
// next entry. A choice is NormalAttack or a class skill whose mouse use accepts its side; whether the skill is still
// learned is the client's check, so a skill lost to a reset returns to its place once it is learned again.
public sealed record SkillBindings(uint Left, uint Right, ImmutableArray<SkillShortcut> Shortcuts)
{
    public const uint NormalAttack = 0x11111111;
    public const int ShortcutCount = 8;

    public static SkillBindings Default { get; } = new(NormalAttack, NormalAttack, ImmutableArray.Create(new SkillShortcut[ShortcutCount]));

    public bool IsValidFor(IReadOnlyList<Skill> classSkills)
    {
        if (Shortcuts.IsDefault || Shortcuts.Length != ShortcutCount
            || !Accepts(classSkills, Left, SkillMouseUse.Left) || !Accepts(classSkills, Right, SkillMouseUse.Right))
        {
            return false;
        }

        foreach (var shortcut in Shortcuts)
        {
            if (shortcut != default && !Accepts(classSkills, shortcut.Code, shortcut.Side))
            {
                return false;
            }
        }

        return true;
    }

    // Keeps the choices the class still offers and resets the others (a saved skill the class table no longer has).
    public SkillBindings Sanitized(IReadOnlyList<Skill> classSkills)
    {
        var shortcuts = new SkillShortcut[ShortcutCount];
        for (var index = 0; !Shortcuts.IsDefault && index < Math.Min(Shortcuts.Length, ShortcutCount); index++)
        {
            var shortcut = Shortcuts[index];
            shortcuts[index] = Accepts(classSkills, shortcut.Code, shortcut.Side) ? shortcut : default;
        }

        return new SkillBindings(Accepts(classSkills, Left, SkillMouseUse.Left) ? Left : NormalAttack,
            Accepts(classSkills, Right, SkillMouseUse.Right) ? Right : NormalAttack, ImmutableArray.Create(shortcuts));
    }

    private static bool Accepts(IReadOnlyList<Skill> classSkills, uint code, SkillMouseUse side)
    {
        if (side is not (SkillMouseUse.Left or SkillMouseUse.Right))
        {
            return false;
        }

        if (code == NormalAttack)
        {
            return true;
        }

        foreach (var skill in classSkills)
        {
            if (skill.Code == code)
            {
                return (skill.MouseUse & side) == side;
            }
        }

        return false;
    }
}

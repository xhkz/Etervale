using Microsoft.Xna.Framework;
using PristonTale.Effects.Vfx;

namespace PristonTale.Gameplay.Effects;

// The wings the premium costume armours and robes show on whoever wears them in the wardrobe: Cygnus (blue), Aquila
// (white) and Corvus (red), for the male and female sets of either kind. The wing hangs at the wearer's back, turns with the wearer
// and is not shown in a village or on the dead.
public sealed class CostumeWings
{
    private readonly Dictionary<ulong, (string Effect, VfxHandle Instance)> _worn = new();
    private readonly HashSet<ulong> _seen = new();
    private readonly List<ulong> _gone = new();

    // The wing effect of a costume ("" for none): costume armours CA1xx and robes CA2xx by their ordinal.
    public static string EffectFor(string costumeKey)
    {
        if (costumeKey is not { Length: 5 } || !costumeKey.StartsWith("CA", StringComparison.OrdinalIgnoreCase) || costumeKey[2] is not ('1' or '2')
            || !int.TryParse(costumeKey.AsSpan(3), out var ordinal))
        {
            return "";
        }

        return ordinal switch
        {
            >= 66 and <= 69 => "vfx.costume.wing.blue",
            >= 85 and <= 88 => "vfx.costume.wing.white",
            >= 89 and <= 92 => "vfx.costume.wing.red",
            _ => ""
        };
    }

    // One wearer this frame; yaw is the wearer's model yaw. Wearers not reported before EndFrame lose their wing.
    public void Wear(SkillPresentation skills, ulong entityId, string costumeKey, bool shown, Vector3 position, float yaw)
    {
        var effect = shown ? EffectFor(costumeKey) : "";
        if (effect.Length == 0)
        {
            return;
        }

        _seen.Add(entityId);
        if (!_worn.TryGetValue(entityId, out var worn) || worn.Effect != effect || !worn.Instance.Alive)
        {
            worn.Instance.Stop();
            if (skills.Spawn(effect, entityId, entityId, position) is not { Spawned: true } instance)
            {
                _worn.Remove(entityId);
                return;
            }

            worn = (effect, instance);
            _worn[entityId] = worn;
        }

        // Effect space faces the opposite way of an actor model's yaw.
        worn.Instance.SetOrientation(Matrix.CreateRotationY(yaw + MathHelper.Pi));
    }

    public void EndFrame()
    {
        _gone.Clear();
        foreach (var (entity, worn) in _worn)
        {
            if (!_seen.Contains(entity))
            {
                worn.Instance.Stop();

                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            _worn.Remove(entity);
        }

        _seen.Clear();
    }
}

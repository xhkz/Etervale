using System.Runtime.InteropServices;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Motion;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.Effects;

// What a player's drawn clip meant to its running cast this frame (SkillReleases.Observe).
public enum CastClip : byte
{
    // No cast runs: the clip is the player's own (a basic attack's shot may leave).
    None,
    Casting,
    // A weapon-clip cast's attack clip passed its release frame (one swing of the skill).
    WeaponPass
}

// The running casts and their releases: the caster's drawn clip passing its first event frame releases a pass, once per
// pass of the clip (a weapon-clip skill repeats the attack clip). The clip a cast watches is the first one to start
// after the cast did, an attack clip for a weapon-clip skill and a skill clip for a skill clip. A pass the drawn clip has
// not released by the server's time for it, plus GraceMs when the caster's clip is drawn, releases on that time.
public sealed class SkillReleases
{
    public const double GraceMs = 250;

    private readonly List<Cast> _casts = new(8);

    // A cast started: passes of its clip, each released when releases is set (else only watched).
    public void Begin(SkillEventInfo skillEvent, SkillRule rule, double nowMs, int passes, bool weaponClip, bool releases)
    {
        End(skillEvent.CasterId);
        _casts.Add(new Cast(skillEvent, rule, nowMs, nowMs + skillEvent.DurationMs, passes, weaponClip) { Released = releases ? 0 : passes });
    }

    public void End(ulong casterId)
    {
        for (var index = _casts.Count - 1; index >= 0; index--)
        {
            if (_casts[index].Event.CasterId == casterId)
            {
                _casts.RemoveAt(index);
            }
        }
    }

    public void Clear() => _casts.Clear();

    // One player's drawn clip this frame (restarted: a new clip, or the same one from its start, since the last frame).
    // releases is how many passes to release now, of skillEvent and rule.
    public CastClip Observe(ulong casterId, in ClipEventCursor clip, bool restarted, double nowMs, out SkillEventInfo skillEvent, out SkillRule rule,
        out int releases)
    {
        var casts = CollectionsMarshal.AsSpan(_casts);
        for (var index = 0; index < casts.Length; index++)
        {
            ref var cast = ref casts[index];
            if (cast.Event.CasterId != casterId)
            {
                continue;
            }

            (skillEvent, rule, releases) = (cast.Event, cast.Rule, 0);
            cast.Drawn = true;
            cast.ClipStarted |= restarted;
            var ownClip = cast.ClipStarted && clip.Motion is { } motion && motion.State == (uint)(cast.WeaponClip ? MotionCode.Attack : MotionCode.Skill);
            if (!ownClip || cast.Passed >= cast.Passes || !clip.Passed(0))
            {
                return nowMs < cast.EndMs ? CastClip.Casting : CastClip.None;
            }

            cast.Passed++;
            releases = Math.Max(0, cast.Passed - cast.Released);
            cast.Released += releases;
            return cast.WeaponClip ? CastClip.WeaponPass : CastClip.Casting;
        }

        (skillEvent, rule, releases) = (null, null, 0);
        return CastClip.None;
    }

    // One pass late on the server's time to release now (call until false); casts done with their passes and their time
    // are forgotten.
    public bool NextLate(double nowMs, out SkillEventInfo skillEvent, out SkillRule rule)
    {
        for (var index = _casts.Count - 1; index >= 0; index--)
        {
            ref var cast = ref CollectionsMarshal.AsSpan(_casts)[index];
            if (cast.Released < cast.Passes && nowMs >= cast.ReleaseDueMs(cast.Released) + (cast.Drawn ? GraceMs : 0d))
            {
                cast.Released++;
                (skillEvent, rule) = (cast.Event, cast.Rule);
                return true;
            }

            if (cast.Released >= cast.Passes && nowMs >= cast.EndMs)
            {
                _casts.RemoveAt(index);
            }
        }

        (skillEvent, rule) = (null, null);
        return false;
    }

    // Passes of the clip, Passed of them seen on the drawn clip, Released of them released.
    private struct Cast(SkillEventInfo skillEvent, SkillRule rule, double startMs, double endMs, int passes, bool weaponClip)
    {
        public readonly SkillEventInfo Event = skillEvent;
        public readonly SkillRule Rule = rule;
        public readonly double StartMs = startMs;
        public readonly double EndMs = endMs;
        public readonly int Passes = passes;
        public readonly bool WeaponClip = weaponClip;
        public int Passed;
        public int Released;
        public bool Drawn;
        public bool ClipStarted;

        // When the server's timeline releases this pass: its share of the cast plus the first hit's offset.
        public readonly double ReleaseDueMs(int pass) => StartMs + pass * (EndMs - StartMs) / Passes + Event.HitAtMs;
    }
}

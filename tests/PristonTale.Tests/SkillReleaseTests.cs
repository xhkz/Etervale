using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Skills;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Effects;
using PristonTale.Simulation.Skills;

namespace PristonTale.Tests;

internal static class SkillReleaseTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a weapon-clip cast releases once per pass of its own attack clip, never on a clip from before it", WeaponClipAsync);
        yield return ("a skill-clip cast releases on its skill clip only, and a cast without a release reports its passes", SkillClipAsync);
        yield return ("a cast whose clip is not drawn releases on the server's time, a drawn one after the grace", LateAsync);
        yield return ("the skills the classic sounds at the release frame wait for it; Perforation's shot too", ReleaseDataAsync);
        yield return ("a basic swing sounds once, at its release, with the outcome the server reported", SwingSoundsAsync);
    }

    private static readonly PristonMotionInfo Attack = new((uint)MotionCode.Attack, 10, 40, [800u], 0, 0, 0, 1, 0, 0);
    private static readonly PristonMotionInfo SkillClip = new((uint)MotionCode.Skill, 50, 90, [1600u], 0, 0, 0, 1, 0, 0);

    // The attack clip starts at tick 1600 and releases at 2400; the skill clip starts at 8000 and releases at 9600.
    private const float AttackStart = 1600f;
    private const float AttackPast = 2500f;
    private const float SkillStart = 8000f;
    private const float SkillPast = 9700f;

    private static SkillRule Rule(string name) => SkillRuleCatalog.All.First(rule => rule.Name == name);

    private static SkillEventInfo Cast(ulong caster, float durationMs, float hitAtMs) =>
        new(caster, 2, 0, SkillEventPhase.Start, 1, default, durationMs, hitAtMs, default, 0f, 1, 0f);

    private static CastClip Observe(SkillReleases releases, ref ClipEventCursor clip, ulong caster, PristonMotionInfo motion, float ticks, double nowMs,
        out int released)
    {
        var restarted = clip.Advance(motion, ticks);
        return releases.Observe(caster, clip, restarted, nowMs, out _, out _, out released);
    }

    private static Task WeaponClipAsync()
    {
        var releases = new SkillReleases();
        var clip = new ClipEventCursor();
        clip.Advance(Attack, AttackStart);
        releases.Begin(Cast(1, 900f, 100f), Rule("Avalanche"), 0d, 3, weaponClip: true, releases: true);
        Assert.True(Observe(releases, ref clip, 1, Attack, AttackPast, 10d, out var released) == CastClip.Casting && released == 0,
            "the attack clip running before the cast released it");
        for (var pass = 1; pass <= 3; pass++)
        {
            Observe(releases, ref clip, 1, Attack, AttackStart, 10d, out _);
            Assert.True(Observe(releases, ref clip, 1, Attack, AttackPast, 10d, out released) == CastClip.WeaponPass && released == 1,
                $"pass {pass} of the attack clip did not release once");
            Assert.True(Observe(releases, ref clip, 1, Attack, AttackPast + 100f, 10d, out released) == CastClip.Casting && released == 0,
                $"pass {pass} released twice");
        }

        Observe(releases, ref clip, 1, Attack, AttackStart, 10d, out _);
        Assert.True(Observe(releases, ref clip, 1, Attack, AttackPast, 10d, out released) != CastClip.WeaponPass && released == 0,
            "a fourth pass released a cast of three");
        Assert.True(!releases.NextLate(10d, out _, out _) && !releases.NextLate(900d, out _, out _), "a released cast released again on its time");
        Assert.True(Observe(releases, ref clip, 1, Attack, AttackStart, 900d, out _) == CastClip.None, "a finished cast still holds the clip");
        return Task.CompletedTask;
    }

    private static Task SkillClipAsync()
    {
        var releases = new SkillReleases();
        var clip = new ClipEventCursor();
        releases.Begin(Cast(1, 900f, 300f), Rule("Fire Ball"), 0d, 1, weaponClip: false, releases: true);
        Observe(releases, ref clip, 1, Attack, AttackStart, 10d, out _);
        Assert.True(Observe(releases, ref clip, 1, Attack, AttackPast, 10d, out var released) == CastClip.Casting && released == 0,
            "an attack clip released a skill-clip cast");
        Observe(releases, ref clip, 1, SkillClip, SkillStart, 10d, out _);
        Assert.True(Observe(releases, ref clip, 1, SkillClip, SkillPast, 10d, out released) == CastClip.Casting && released == 1,
            "the skill clip's release frame did not release the cast");

        releases.Begin(Cast(3, 600f, 100f), Rule("Raving"), 0d, 2, weaponClip: true, releases: false);
        Observe(releases, ref clip, 3, Attack, AttackStart, 10d, out _);
        Assert.True(Observe(releases, ref clip, 3, Attack, AttackPast, 10d, out released) == CastClip.WeaponPass && released == 0,
            "a weapon-clip cast without a release did not report its pass, or released");
        Assert.True(!releases.NextLate(500d, out _, out _), "a cast without a release released on its time");
        return Task.CompletedTask;
    }

    private static Task LateAsync()
    {
        var releases = new SkillReleases();
        releases.Begin(Cast(5, 600f, 100f), Rule("Avalanche"), 0d, 2, weaponClip: true, releases: true);
        Assert.True(!releases.NextLate(99d, out _, out _), "an undrawn cast released before the server's time");
        Assert.True(releases.NextLate(100d, out var late, out _) && late.CasterId == 5 && !releases.NextLate(100d, out _, out _),
            "an undrawn cast's first pass did not release once on the server's time");
        Assert.True(!releases.NextLate(399d, out _, out _) && releases.NextLate(400d, out _, out _), "the second pass did not wait its share of the cast");
        Assert.True(!releases.NextLate(600d, out _, out _) && !releases.NextLate(700d, out _, out _), "a cast released more passes than it has");

        var clip = new ClipEventCursor();
        releases.Begin(Cast(6, 300f, 100f), Rule("Avalanche"), 1000d, 1, weaponClip: true, releases: true);
        Observe(releases, ref clip, 6, Attack, AttackStart, 1000d, out _);
        Assert.True(!releases.NextLate(1100d, out _, out _) && !releases.NextLate(1100d + SkillReleases.GraceMs - 1d, out _, out _),
            "a drawn cast did not wait the grace for its clip");
        Assert.True(releases.NextLate(1100d + SkillReleases.GraceMs, out _, out _), "a drawn cast whose clip never released stayed silent");
        return Task.CompletedTask;
    }

    private static Task ReleaseDataAsync()
    {
        string[] atRelease =
        [
            "Wind Arrow", "Perfect Aim", "Arrow of Rage", "Avalanche", "Elemental Shot", "Bomb Shot", "Perforation", "Recall Wolverine", "Phoenix Shot",
            "Farina", "Twist Javelin", "Fire Javelin", "Fire Ball", "Energy Shield", "Diastrophism", "Spirit Elemental", "Mechanic Bomb", "Impulsion",
            "Pike Wind", "Ground Pike", "Tornado", "Charging Strike", "Holy Bolt", "Vigor Ball"
        ];
        var flagged = SkillRuleCatalog.All.Where(static rule => rule.CastSoundAtRelease).Select(static rule => rule.Name).Order().ToArray();
        Assert.True(flagged.SequenceEqual(atRelease.Order()), $"the release sounds are not the 24 skills: {string.Join(", ", flagged)}");
        Assert.True(SkillRuleCatalog.All.Where(static rule => rule.CastEffectAtRelease).Select(static rule => rule.Name).SequenceEqual(["Perforation"]),
            "Perforation's shot does not wait for the release, or another cast effect does");
        return Task.CompletedTask;
    }

    private static Task SwingSoundsAsync()
    {
        var sounds = new SwingSounds();
        sounds.Begin(1, 0d);
        sounds.Outcome(1, WeaponSoundOutcome.Hit, 0d);
        Assert.True(!sounds.Next(0d, out _, out _), "an outcome sounded before its swing's release");
        Assert.True(sounds.Release(1) == WeaponSoundOutcome.Hit, "the release did not sound the reported outcome");
        sounds.Outcome(1, WeaponSoundOutcome.Miss, 10d);
        Assert.True(!sounds.Next(10d, out _, out _) && sounds.Release(1) is null, "a second target of the same swing sounded again");

        sounds.Begin(2, 0d);
        Assert.True(!sounds.Next(0d, out _, out _) && sounds.Release(2) is null, "a release without an outcome sounded");
        sounds.Outcome(2, WeaponSoundOutcome.Critical, 0d);
        Assert.True(sounds.Next(0d, out var attacker, out var outcome) && attacker == 2 && outcome == WeaponSoundOutcome.Critical,
            "an outcome after the release did not sound at once");

        sounds.Begin(3, 0d);
        sounds.Outcome(3, WeaponSoundOutcome.Miss, 0d);
        Assert.True(!sounds.Next(SwingSounds.OutcomeWaitMs - 1d, out _, out _), "an outcome stopped waiting for its release early");
        Assert.True(sounds.Next(SwingSounds.OutcomeWaitMs, out attacker, out outcome) && attacker == 3 && outcome == WeaponSoundOutcome.Miss,
            "an outcome whose release never came stayed silent");

        sounds.Begin(4, 300d);
        sounds.Outcome(4, WeaponSoundOutcome.Hit, 300d);
        sounds.Begin(5, 0d);
        sounds.Outcome(5, WeaponSoundOutcome.Hit, 0d);
        Assert.True(!sounds.Next(0d, out _, out _) && sounds.Release(5) == WeaponSoundOutcome.Hit, "a later player's delay held back the local swing");
        Assert.True(!sounds.Next(300d, out _, out _) && sounds.Release(4) == WeaponSoundOutcome.Hit, "a delayed player's swing did not wait its delay");
        return Task.CompletedTask;
    }
}

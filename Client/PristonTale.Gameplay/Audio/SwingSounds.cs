namespace PristonTale.Gameplay.Audio;

// When a player's basic swing sounds: once per swing, at the drawn attack clip's release, with the outcome the server
// reported for it. The server's swing and outcome of another player wait its playback delay, as its drawn motion does.
// An outcome that arrives after the release plays at once; one whose release is not seen within OutcomeWaitMs (the
// swinger is not drawn) plays then. Further outcomes of the same swing (more targets) are silent.
public sealed class SwingSounds
{
    public const double OutcomeWaitMs = 500;

    private readonly Dictionary<ulong, Swing> _swings = new();
    private readonly List<Queued> _queued = new(16);

    // The server began a swing of this attacker, to apply at atMs.
    public void Begin(ulong attackerId, double atMs) => _queued.Add(new Queued(atMs, attackerId, null));

    // The swing's hit, critical or miss, to apply at atMs.
    public void Outcome(ulong attackerId, WeaponSoundOutcome outcome, double atMs) => _queued.Add(new Queued(atMs, attackerId, outcome));

    // The drawn attack clip released the swing: the outcome to play now, when one is known.
    public WeaponSoundOutcome? Release(ulong attackerId)
    {
        var swing = Of(attackerId);
        if (swing.Played || swing.Released)
        {
            return null;
        }

        swing.Released = true;
        if (swing.Outcome is not { } outcome)
        {
            return null;
        }

        swing.Played = true;
        return outcome;
    }

    // Applies what is due and returns one swing to sound now (call until false): an outcome of a released swing, or one
    // that waited OutcomeWaitMs for its release.
    public bool Next(double nowMs, out ulong attackerId, out WeaponSoundOutcome outcome)
    {
        // Attackers wait different delays, so the queue is not in time order; each attacker's own entries are.
        for (var index = 0; index < _queued.Count; index++)
        {
            var queued = _queued[index];
            if (queued.AtMs > nowMs)
            {
                continue;
            }

            _queued.RemoveAt(index--);
            if (queued.Outcome is not { } reported)
            {
                Of(queued.AttackerId).Reset();
            }
            else if (Apply(queued.AttackerId, reported, queued.AtMs))
            {
                (attackerId, outcome) = (queued.AttackerId, reported);
                return true;
            }
        }

        foreach (var (id, swing) in _swings)
        {
            if (!swing.Played && swing.Outcome is { } waiting && nowMs - swing.OutcomeAtMs >= OutcomeWaitMs)
            {
                swing.Played = true;
                (attackerId, outcome) = (id, waiting);
                return true;
            }
        }

        (attackerId, outcome) = (0, default);
        return false;
    }

    public void Forget(ulong attackerId) => _swings.Remove(attackerId);

    public void Clear()
    {
        _swings.Clear();
        _queued.Clear();
    }

    // True when the outcome sounds now: its swing was already released.
    private bool Apply(ulong attackerId, WeaponSoundOutcome outcome, double atMs)
    {
        var swing = Of(attackerId);
        if (swing.Played || swing.Outcome is not null)
        {
            return false;
        }

        if (swing.Released)
        {
            swing.Played = true;
            return true;
        }

        swing.Outcome = outcome;
        swing.OutcomeAtMs = atMs;
        return false;
    }

    private Swing Of(ulong attackerId)
    {
        if (!_swings.TryGetValue(attackerId, out var swing))
        {
            swing = new Swing();
            _swings[attackerId] = swing;
        }

        return swing;
    }

    private readonly record struct Queued(double AtMs, ulong AttackerId, WeaponSoundOutcome? Outcome);

    private sealed class Swing
    {
        public WeaponSoundOutcome? Outcome { get; set; }
        public double OutcomeAtMs { get; set; }
        public bool Released { get; set; }
        public bool Played { get; set; }

        public void Reset()
        {
            Outcome = null;
            Released = false;
            Played = false;
        }
    }
}

using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Audio;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Gameplay.World;

// Character, weapon and footstep sounds: monsters on their animation's cues, players' basic swings at their drawn attack
// clip's release with the hit, critical or miss the server reported (SwingSounds) and each pass of a weapon-clip skill
// as a hit, players' damage and death by class, and the local player's steps on the floor of its field. Skill hits play
// no weapon sound.
public sealed partial class GameScene
{
    private CombatSounds _combatSounds;
    private MonsterSoundCues _monsterSounds;
    private int _footsteps;
    private ClipEventCursor _stepClip;
    private readonly SwingSounds _swingSounds = new();

    private void CreateCombatSounds()
    {
        if (_combatSounds is not null || _assetRoot is null)
        {
            return;
        }

        _combatSounds = new CombatSounds(_assetRoot, _log);
        _monsterSounds = new MonsterSoundCues(_combatSounds, this);
        _monsterSounds.WarpStarted += OnMonsterWarp;
    }

    private void UpdateCombatSounds()
    {
        if (_combatSounds is null)
        {
            return;
        }

        _combatSounds.Enabled = _audio?.SoundEnabled ?? true;
        _combatSounds.Update();
        if (_remote is not null && _playerBody is not null)
        {
            _monsterSounds.Update(_monsterViews, _playerBody.Position);
        }

        StepFootsteps();
        while (_swingSounds.Next(_skillClockMs, out var attacker, out var outcome))
        {
            PlaySwing(attacker, outcome);
        }
    }

    // A player's attack clip passed its release frame this frame.
    private void ObserveSwingRelease(ulong attackerId, SwingRelease release)
    {
        if (release == SwingRelease.SkillPass)
        {
            PlaySwing(attackerId, WeaponSoundOutcome.Hit);
        }
        else if (release == SwingRelease.Basic && _swingSounds.Release(attackerId) is { } outcome)
        {
            PlaySwing(attackerId, outcome);
        }
    }

    private void PlaySwing(ulong attackerId, WeaponSoundOutcome outcome)
    {
        if (_combatSounds is not null && _playerBody is not null && _remote is not null && SwingOf(attackerId) is { } swing)
        {
            _combatSounds.PlayWeapon(swing.WeaponKey, swing.Class, outcome, swing.Position, _playerBody.Position);
        }
    }

    // The local player's walk and run clips step on their first two event frames.
    private void StepFootsteps()
    {
        if (!_playerBodyActor.IsValid || !((Effects.IMonsterScene)this).TryGetActorMotion(_playerBodyActor, out var motion, out var ticks)
            || motion is null)
        {
            return;
        }

        _stepClip.Advance(motion, ticks);
        if ((MotionCode)motion.State is not (MotionCode.Walk or MotionCode.Run))
        {
            return;
        }

        for (var index = 0; index < 2; index++)
        {
            if (_stepClip.Passed(index))
            {
                PlayFootstep();
                LeaveStep(index);
            }
        }
    }

    private void ObserveCombatSounds(PresenceUpdate update)
    {
        if (_combatSounds is null || _playerBody is null || _remote is null)
        {
            return;
        }

        var listener = _playerBody.Position;
        foreach (var removed in update.Removed)
        {
            _swingSounds.Forget(removed);
        }

        foreach (var combatEvent in update.Events)
        {
            switch (combatEvent.Kind)
            {
                case CombatEventKind.SwingBegin when SwingOf(combatEvent.AttackerId) is not null:
                    _swingSounds.Begin(combatEvent.AttackerId, SwingSoundAtMs(combatEvent.AttackerId));
                    break;
                case CombatEventKind.Hit or CombatEventKind.Miss when !combatEvent.Skill && SwingOf(combatEvent.AttackerId) is not null:
                    var outcome = combatEvent.Kind == CombatEventKind.Miss ? WeaponSoundOutcome.Miss
                        : combatEvent.Critical ? WeaponSoundOutcome.Critical : WeaponSoundOutcome.Hit;
                    _swingSounds.Outcome(combatEvent.AttackerId, outcome, SwingSoundAtMs(combatEvent.AttackerId));
                    break;
                case CombatEventKind.Revive:
                    var risen = combatEvent.TargetId == _remote.SelfEntityId ? listener
                        : _remote.TryGetDrawnPosition(combatEvent.TargetId, out var drawn) ? drawn : (Vector3?)null;
                    if (risen is { } at)
                    {
                        _combatSounds.PlayRestart(at, listener);
                    }

                    break;
            }
        }
    }

    // Another player's swing reaches its drawn motion one playback delay later; the local player's at once.
    private double SwingSoundAtMs(ulong attackerId) => _skillClockMs + (attackerId == _remote.SelfEntityId ? 0d : _remote.PlaybackDelayMsOf(attackerId));

    // The weapon, class and position of a player that swung: the local player from its own equipment, others from their
    // appearance; null for monsters.
    private (string WeaponKey, PlayerClass Class, Vector3 Position)? SwingOf(ulong attackerId)
    {
        if (attackerId == _remote.SelfEntityId)
        {
            var weapon = "";
            foreach (var item in Owner?.Items ?? [])
            {
                if ((EquipmentSlot)item.Slot == EquipmentSlot.Weapon)
                {
                    weapon = item.CatalogKey;
                }
            }

            return (weapon, _player.Class.Class, _playerBody.Position);
        }

        return _remote.TryGetPlayerAudio(attackerId, out var playerClass, out var weaponKey, out var position)
            ? (weaponKey, playerClass, position)
            : null;
    }

    // A player entering damage or death plays its class's sound where it stands.
    private void PlayPlayerMotionSound(PlayerClass playerClass, MotionCode motion, Vector3 position)
    {
        if (_combatSounds is null || _playerBody is null)
        {
            return;
        }

        var cue = motion switch
        {
            MotionCode.Damage => CharacterSoundCue.Damage,
            MotionCode.Dead => CharacterSoundCue.Dead,
            _ => (CharacterSoundCue?)null
        };
        if (cue is { } sound)
        {
            _combatSounds.PlayCharacter((int)playerClass, sound, position, _playerBody.Position);
        }
    }

    private void OnRemotePlayerMotionStarted(PlayerClass playerClass, MotionCode motion, Vector3 position) =>
        PlayPlayerMotionSound(playerClass, motion, position);

    // The local player's step on the floor of its field: stone in towns and ruins, dungeon stone, soft ground in forests
    // and deserts; other fields are silent, as in the original.
    private void PlayFootstep()
    {
        if (_combatSounds is null)
        {
            return;
        }

        _footsteps++;
        var floor = _activeField?.Definition.State switch
        {
            FieldState.Village or FieldState.Ruin => FootstepFloor.Stone,
            FieldState.Forest or FieldState.Desert => FootstepFloor.Soft,
            FieldState.Dungeon => FootstepFloor.Dungeon,
            _ => (FootstepFloor?)null
        };
        if (floor is { } ground)
        {
            _combatSounds.PlayFootstep(ground, _footsteps);
        }
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Contracts.Units;
using PristonTale.Engine3D.Physics;

namespace PristonTale.Engine3D.Rendering;

public sealed class PristonCamera
{
    // Auto mode, in the original's angle units on its 70 Hz tick: while the character moves, a gap to its facing under a
    // quarter turn plus 180 closes by a 64th of itself, at least 4, and stops within 256 of the facing.
    private const float FollowReachAngles = GameUnits.AngleTurn / 4 + 180;
    private const float FollowStopAngles = 256;
    private const float FollowMinStepAngles = 4;
    private const float FollowStepDivisor = 64;
    private const float MostFollowBacklogSeconds = 0.25f;

    // Lock mode: the original's 440 units away (600 in a window wider than 4:3) at 45 degrees less 128 of pitch, turned to
    // the heading of 45 degrees on entering it.
    private const float LockDistance = 440;
    private const float LockWideDistance = 600;
    private const float NarrowAspect = 4f / 3f;
    private const int LockPitchAngles = GameUnits.AngleTurn / 8 - 128;
    private const int LockHeadingAngles = GameUnits.AngleTurn / 8;

    private readonly PristonCameraSettings _settings;
    private readonly float _fieldOfViewRadians;

    private Vector3 _position;
    private float _yaw;
    private float _pitch;
    private MouseState _lastMouse;
    private bool _hasMouse;
    private float _distance;
    private int _projectionWidth = -1;
    private int _projectionHeight = -1;
    private float _projectionFieldOfView = float.NaN;
    private bool _hasPosition;
    private bool _locked;
    private float _turnSeconds;
    private readonly PristonCameraWave _wave = new();

    public PristonCamera(Vector3 target, Vector3 forward, PristonCameraSettings settings = null)
    {
        _settings = settings ?? PristonCameraSettings.Default;
        _fieldOfViewRadians = _settings.FieldOfViewDegrees * MathF.PI / 180f;
        Reset(target, forward);
    }

    public Matrix View { get; private set; }
    public Matrix Projection { get; private set; }
    public Vector3 Position => _position;
    public Vector3 FlatForward { get; private set; } = Vector3.Forward;
    public Vector3 Target { get; private set; }

    public PristonCameraMode Mode { get; set; } = PristonCameraMode.Manual;

    // Auto mode turns only while this holds: turning the camera by hand or picking a target clears it, and a walk click
    // sets it again.
    public bool AutoFollow { get; set; } = true;

    public void Reset(Vector3 target, Vector3 forward)
    {
        _distance = _settings.DefaultDistance;
        _pitch = _settings.DefaultPitch;
        var normalized = NormalizedForward(forward);
        _yaw = WorldSpace.RenderYaw(normalized);
        _hasPosition = false;
        _hasMouse = false;
        _locked = false;
        _turnSeconds = 0f;
        AutoFollow = true;
        _wave.Stop();
        UpdateFollow(target, 0f);
    }

    public void Update(GameTime gameTime, GraphicsDevice graphicsDevice, Vector3 target, Vector3 forward)
    {
        Update(gameTime, graphicsDevice, target, forward, null);
    }

    public void SnapFollow(Vector3 target, IPristonCollisionQuery collisionWorld = null)
    {
        _hasPosition = false;
        UpdateFollow(target, 0f, collisionWorld);
    }

    public void SetScriptedView(GraphicsDevice graphicsDevice, Vector3 position, Vector3 lookAt)
        => SetScriptedView(graphicsDevice, position, lookAt, Vector3.Up);

    // Shakes the follow view (PristonCameraWave); a new shake replaces a running one.
    public void Wave(int factor, int delayFrames) => _wave.Start(factor, delayFrames);

    public void SetScriptedView(GraphicsDevice graphicsDevice, Vector3 position, Vector3 lookAt, Vector3 up) =>
        SetScriptedView(graphicsDevice, position, lookAt, up, _fieldOfViewRadians);

    // A scripted view through another vertical field of view; the next view without one returns to the settings' angle.
    public void SetScriptedView(GraphicsDevice graphicsDevice, Vector3 position, Vector3 lookAt, Vector3 up, float verticalFieldOfViewRadians)
    {
        EnsureProjection(graphicsDevice, verticalFieldOfViewRadians);
        _position = position;
        _hasPosition = true;
        Target = lookAt;

        var horizontal = new Vector3(lookAt.X - position.X, 0f, lookAt.Z - position.Z);
        FlatForward = horizontal.LengthSquared() > 0.0001f ? Vector3.Normalize(horizontal) : Vector3.Forward;
        View = Matrix.CreateLookAt(position, lookAt, up);
    }

    public void Update(GameTime gameTime, GraphicsDevice graphicsDevice, Vector3 target, Vector3 forward,
        IPristonCollisionQuery collisionWorld, bool pointerCaptured = false, bool inputEnabled = true, bool keysEnabled = true, bool moving = false)
    {
        var seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        UpdateOrbit(Mouse.GetState(), keysEnabled ? Keyboard.GetState() : default, pointerCaptured, graphicsDevice.Viewport.Bounds,
            seconds, inputEnabled);
        EnsureProjection(graphicsDevice, _fieldOfViewRadians);
        HoldLock(graphicsDevice.Viewport.AspectRatio);
        TurnBehind(forward, moving, seconds);
        UpdateFollow(target, seconds, collisionWorld);
    }

    internal void HoldLock(float aspect)
    {
        if (Mode != PristonCameraMode.Lock)
        {
            _locked = false;
            return;
        }

        if (!_locked)
        {
            _locked = true;
            // A render yaw is pi less the heading (WorldSpace).
            _yaw = MathF.PI - GameUnits.FromAngle(LockHeadingAngles);
        }

        _distance = MathHelper.Clamp(aspect > NarrowAspect ? LockWideDistance : LockDistance, _settings.MinDistance, _settings.MaxDistance);
        _pitch = MathHelper.Clamp(GameUnits.FromAngle(LockPitchAngles), _settings.MinPitch, _settings.MaxPitch);
    }

    internal void TurnBehind(Vector3 forward, bool moving, float deltaSeconds)
    {
        if (Mode != PristonCameraMode.Auto || !AutoFollow || !moving || !float.IsFinite(deltaSeconds) || deltaSeconds <= 0f)
        {
            _turnSeconds = 0f;
            return;
        }

        _turnSeconds = MathF.Min(_turnSeconds + deltaSeconds, MostFollowBacklogSeconds);
        var behind = WorldSpace.RenderYaw(NormalizedForward(forward));
        while (_turnSeconds >= GameUnits.TickSeconds)
        {
            _turnSeconds -= GameUnits.TickSeconds;
            _yaw = FollowTurn(_yaw, behind);
        }
    }

    internal static float FollowTurn(float yaw, float behind)
    {
        var gap = MathHelper.WrapAngle(yaw - behind) / GameUnits.RadiansPerAngle;
        var size = MathF.Abs(gap);
        if (size >= FollowReachAngles || size <= FollowStopAngles)
        {
            return yaw;
        }

        var step = MathF.Max(size / FollowStepDivisor, FollowMinStepAngles);
        return yaw - MathF.Sign(gap) * step * GameUnits.RadiansPerAngle;
    }

    public void UpdateOrbit(MouseState mouse, bool pointerCaptured = false, Rectangle viewport = default, float deltaSeconds = 0f, bool inputEnabled = true)
        => UpdateOrbit(mouse, default, pointerCaptured, viewport, deltaSeconds, inputEnabled);

    public void UpdateOrbit(MouseState mouse, KeyboardState keyboard, bool pointerCaptured = false, Rectangle viewport = default,
        float deltaSeconds = 0f, bool inputEnabled = true)
    {
        if (!inputEnabled)
        {
            _hasMouse = false;
            return;
        }

        if (!_hasMouse)
        {
            _lastMouse = mouse;
            _hasMouse = true;
            return;
        }

        var locked = Mode == PristonCameraMode.Lock;
        if (!pointerCaptured && mouse.MiddleButton == ButtonState.Pressed && _lastMouse.MiddleButton == ButtonState.Pressed)
        {
            var dx = mouse.X - _lastMouse.X;
            var dy = mouse.Y - _lastMouse.Y;
            _yaw -= dx * _settings.MouseOrbitSensitivity;
            if (!locked)
            {
                _pitch = MathHelper.Clamp(_pitch + dy * _settings.MouseOrbitSensitivity, _settings.MinPitch, _settings.MaxPitch);
            }

            if (dx != 0 || (dy != 0 && !locked))
            {
                AutoFollow = false;
            }
        }

        if (!locked && !pointerCaptured && mouse.MiddleButton == ButtonState.Released && viewport.Contains(mouse.Position)
            && viewport.Width > _settings.EdgeOrbitWidth * 2 && float.IsFinite(deltaSeconds) && deltaSeconds > 0f)
        {
            if (mouse.X < viewport.Left + _settings.EdgeOrbitWidth)
            {
                _yaw -= _settings.EdgeOrbitRadiansPerSecond * deltaSeconds;
                AutoFollow = false;
            }
            else if (mouse.X > viewport.Right - _settings.EdgeOrbitWidth)
            {
                _yaw += _settings.EdgeOrbitRadiansPerSecond * deltaSeconds;
                AutoFollow = false;
            }
        }

        if (float.IsFinite(deltaSeconds) && deltaSeconds > 0f)
        {
            var orbit = _settings.EdgeOrbitRadiansPerSecond * deltaSeconds;
            if (keyboard.IsKeyDown(Keys.Left))
            {
                _yaw -= orbit;
                AutoFollow = false;
            }
            else if (keyboard.IsKeyDown(Keys.Right))
            {
                _yaw += orbit;
                AutoFollow = false;
            }

            var zoom = _settings.KeyboardZoomUnitsPerSecond * deltaSeconds;
            if (!locked && keyboard.IsKeyDown(Keys.Up))
                _distance = MathHelper.Clamp(_distance - zoom, _settings.MinDistance, _settings.MaxDistance);
            else if (!locked && keyboard.IsKeyDown(Keys.Down))
                _distance = MathHelper.Clamp(_distance + zoom, _settings.MinDistance, _settings.MaxDistance);
        }

        var scroll = mouse.ScrollWheelValue - _lastMouse.ScrollWheelValue;
        if (!locked && !pointerCaptured && scroll != 0)
        {
            _distance = MathHelper.Clamp(_distance - scroll * _settings.MouseZoomSensitivity, _settings.MinDistance, _settings.MaxDistance);
        }

        _lastMouse = mouse;
    }

    private void EnsureProjection(GraphicsDevice graphicsDevice, float fieldOfViewRadians)
    {
        var width = Math.Max(1, graphicsDevice.Viewport.Width);
        var height = Math.Max(1, graphicsDevice.Viewport.Height);
        if (width == _projectionWidth && height == _projectionHeight && fieldOfViewRadians == _projectionFieldOfView)
        {
            return;
        }

        _projectionWidth = width;
        _projectionHeight = height;
        _projectionFieldOfView = fieldOfViewRadians;
        var aspect = width / (float)height;
        var verticalFieldOfView = VerticalFieldOfView(fieldOfViewRadians, _settings.MaxHorizontalFieldOfViewDegrees, aspect);
        Projection = PristonDepth.Perspective(verticalFieldOfView, aspect, _settings.NearPlane, _settings.FarPlane);
    }

    internal static float VerticalFieldOfView(float verticalRadians, float maxHorizontalDegrees, float aspect)
    {
        var maxHorizontal = maxHorizontalDegrees * MathF.PI / 180f;
        var capped = 2f * MathF.Atan(MathF.Tan(maxHorizontal * 0.5f) / aspect);
        return MathF.Min(verticalRadians, capped);
    }

    public void UpdateFollow(Vector3 target, float deltaSeconds, IPristonCollisionQuery collisionWorld = null)
    {
        var lookAt = target + new Vector3(0f, _settings.TargetHeight, 0f);
        var alpha = 1f;
        if (_hasPosition && float.IsFinite(deltaSeconds) && deltaSeconds > 0f)
        {
            alpha = 1f - MathF.Exp(-_settings.FollowSmoothing * deltaSeconds);
            lookAt = Vector3.Lerp(Target, lookAt, alpha);
        }

        var arm = lookAt - CameraForward() * MathF.Max(0f, _distance + _wave.Advance(deltaSeconds));
        var desired = arm;
        if (_hasPosition)
        {
            var translated = _position + lookAt - Target;
            desired = Vector3.Lerp(translated, desired, alpha);
        }

        Target = lookAt;
        var previous = _hasPosition ? _position : desired;
        var resolved = PristonCameraCollision.Resolve(previous, desired, lookAt.Y, _settings, collisionWorld);
        if (_hasPosition && IsStranded(resolved, desired, arm, lookAt, collisionWorld))
        {
            // The camera sweeps from its own position so poles and portals never pull it forward. A camera that is held
            // away from its arm by a wall it cannot see the focus through was left behind a wall the target crossed (a
            // field edge, a gate, a server correction); sweeping from there would keep it stuck, so re-anchor on the arm.
            resolved = PristonCameraCollision.Resolve(lookAt, arm, lookAt.Y, _settings, collisionWorld);
        }

        _position = resolved;
        _hasPosition = true;

        FlatForward = new Vector3(MathF.Sin(_yaw), 0f, MathF.Cos(_yaw));
        var viewTarget = Vector3.DistanceSquared(_position, lookAt) > 0.000001f ? lookAt : _position + CameraForward();
        View = Matrix.CreateLookAt(_position, viewTarget, Vector3.Up);
    }

    private bool IsStranded(Vector3 position, Vector3 desired, Vector3 arm, Vector3 lookAt, IPristonCollisionQuery collisionWorld)
    {
        if (collisionWorld is null || Vector3.DistanceSquared(position, desired) <= 1f)
        {
            return false;
        }

        var strandedDistance = _settings.CollisionWallPad * 2f;
        if (Vector3.DistanceSquared(position, arm) <= strandedDistance * strandedDistance)
        {
            return false;
        }

        return collisionWorld.TryTraceSegment(lookAt, position, out _, out _);
    }

    private static Vector3 NormalizedForward(Vector3 forward)
    {
        if (forward.LengthSquared() < 0.0001f)
        {
            return Vector3.Forward;
        }

        forward.Y = 0f;
        if (forward.LengthSquared() < 0.0001f)
        {
            return Vector3.Forward;
        }

        forward.Normalize();
        return forward;
    }

    private Vector3 CameraForward()
    {
        var horizontal = MathF.Cos(_pitch);
        return new Vector3(
            MathF.Sin(_yaw) * horizontal,
            -MathF.Sin(_pitch),
            MathF.Cos(_yaw) * horizontal);
    }
}

// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared._Nestation.Pain;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Nestation.Pain;

public sealed partial class PainOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> CircleMaskShader = "GradientCircleMask";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly ShaderInstance _colorShader;
    private readonly ShaderInstance _blackShader;

    private float _colorLevel;
    private float _blackLevel;
    private Vector3 _color = new(1f, 0f, 0f);
    private byte _lastPulse;
    private bool _pulseInit;
    private float _weakPulse;

    public PainOverlay()
    {
        IoCManager.InjectDependencies(this);
        _colorShader = _prototypeManager.Index(CircleMaskShader).InstanceUnique();
        _blackShader = _prototypeManager.Index(CircleMaskShader).InstanceUnique();
    }

    private static Vector3 ColorOf(PainKind kind)
    {
        return kind switch
        {
            PainKind.Blunt => new Vector3(0.55f, 0.05f, 0.3f),
            PainKind.Slash => new Vector3(0.95f, 0.05f, 0.05f),
            PainKind.Piercing => new Vector3(0.7f, 0f, 0f),
            PainKind.Burn => new Vector3(1f, 0.45f, 0f),
            PainKind.Acid => new Vector3(0.6f, 0.95f, 0.1f),
            PainKind.Shock => new Vector3(0.45f, 0.7f, 1f),
            _ => new Vector3(1f, 0f, 0f)
        };
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return;

        if (args.Viewport.Eye != eyeComp.Eye)
            return;

        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out PainComponent? pain))
            return;

        var frame = (float) _timing.FrameTime.TotalSeconds;
        var time = (float) _timing.RealTime.TotalSeconds;

        if (!_pulseInit)
        {
            _lastPulse = pain.WeakPulse;
            _pulseInit = true;
        }
        else if (pain.WeakPulse != _lastPulse)
        {
            _lastPulse = pain.WeakPulse;
            _weakPulse = 1f;
        }

        var colorTarget = 0f;
        var blackTarget = 0f;
        var rate = 2.2f;

        switch (pain.Level)
        {
            case PainLevel.Medium:
                colorTarget = 0.3f + 0.2f * pain.Intensity;
                rate = 2.2f;
                break;
            case PainLevel.Strong:
                colorTarget = 0.6f + 0.3f * pain.Intensity;
                blackTarget = 0.5f;
                rate = 3.2f;
                break;
            case PainLevel.Extreme:
                colorTarget = 0.9f;
                blackTarget = 0.85f;
                rate = 3.6f;
                break;
        }

        _colorLevel = Approach(_colorLevel, colorTarget, frame * 1.5f);
        _blackLevel = Approach(_blackLevel, blackTarget, frame * 1.5f);
        _color = Vector3.Lerp(_color, ColorOf(pain.Dominant), MathF.Min(1f, frame * 3f));

        var weak = 0f;

        if (_weakPulse > 0f)
        {
            _weakPulse = MathF.Max(0f, _weakPulse - frame);
            weak = MathF.Sin(_weakPulse * MathF.PI);
        }

        var handle = args.WorldHandle;
        var viewport = args.WorldAABB;
        var distance = args.ViewportBounds.Width;

        if (_blackLevel > 0.01f)
            DrawVignette(handle, viewport, distance, _blackShader, Vector3.Zero, _blackLevel, 0f, 0.35f + 0.5f * _blackLevel);

        if (_colorLevel > 0.01f)
        {
            var pulse = MathF.Max(0f, MathF.Sin(time * rate));
            DrawVignette(handle, viewport, distance, _colorShader, _color, _colorLevel, pulse, 0.8f);
        }

        if (weak > 0.01f)
            DrawVignette(handle, viewport, distance, _blackShader, Vector3.Zero, 0.2f, weak * 0.6f, 0.3f * weak);
    }

    private static float Approach(float value, float target, float delta)
    {
        if (value < target)
            return MathF.Min(target, value + delta);

        return MathF.Max(target, value - delta);
    }

    private static void DrawVignette(
        DrawingHandleWorld handle,
        Box2 viewport,
        float distance,
        ShaderInstance shader,
        Vector3 color,
        float level,
        float pulse,
        float alpha)
    {
        var outerMax = 2.0f * distance;
        var outerMin = 0.8f * distance;
        var innerMax = 0.6f * distance;
        var innerMin = 0.2f * distance;

        var outer = outerMax - level * (outerMax - outerMin);
        var inner = innerMax - level * (innerMax - innerMin);

        shader.SetParameter("time", pulse);
        shader.SetParameter("color", color);
        shader.SetParameter("darknessAlphaOuter", alpha);
        shader.SetParameter("outerCircleRadius", outer);
        shader.SetParameter("outerCircleMaxRadius", outer + 0.2f * distance);
        shader.SetParameter("innerCircleRadius", inner);
        shader.SetParameter("innerCircleMaxRadius", inner + 0.02f * distance);
        handle.UseShader(shader);
        handle.DrawRect(viewport, Color.White);
    }
}

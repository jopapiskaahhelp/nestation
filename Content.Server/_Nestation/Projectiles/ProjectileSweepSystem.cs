// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared.Damage.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Nestation.Projectiles;

public sealed partial class ProjectileSweepSystem : EntitySystem
{
    private const float MinSweepDistance = 0.5f;
    private const float MinHitDistance = 0.01f;
    private const float HitNudge = 0.08f;

    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ProjectileComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var projectile, out var body, out var xform))
        {
            if (projectile.ProjectileSpent || projectile is { Weapon: null, OnlyCollideWhenShot: true })
                continue;

            var speed = body.LinearVelocity.Length();
            var distance = speed * frameTime;

            if (distance < MinSweepDistance || xform.MapID == MapId.Nullspace)
                continue;

            if (!TryComp(uid, out FixturesComponent? fixtures) ||
                !fixtures.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var fixture))
                continue;

            var direction = body.LinearVelocity / speed;
            var origin = _transform.GetWorldPosition(xform);
            var ray = new CollisionRay(origin, direction, fixture.CollisionMask);

            var found = false;
            var bestDistance = float.MaxValue;
            var bestPosition = Vector2.Zero;

            foreach (var hit in _physics.IntersectRay(xform.MapID, ray, distance, uid, false))
            {
                if (_timing.CurTime < projectile.WhenToStopIgnoringShooter &&
                    (hit.HitEntity == projectile.Shooter || hit.HitEntity == projectile.Weapon))
                    continue;

                if (TryComp(hit.HitEntity, out RequireProjectileTargetComponent? require) &&
                    require.Active &&
                    CompOrNull<TargetedProjectileComponent>(uid)?.Target != hit.HitEntity)
                    continue;

                if (hit.Distance < MinHitDistance || hit.Distance >= bestDistance)
                    continue;

                found = true;
                bestDistance = hit.Distance;
                bestPosition = hit.HitPos + direction * HitNudge;
            }

            if (found)
                _transform.SetWorldPosition(xform, bestPosition);
        }
    }
}

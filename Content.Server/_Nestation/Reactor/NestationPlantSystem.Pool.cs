// Nestation CC BY-NC 3.0
using Content.Shared._Nestation.Reactor;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem
{
    public float PoolTemperature(List<EntityUid> group)
    {
        if (group.Count == 0)
            return NestationTuning.Ambient;

        var sum = 0f;
        var n = 0;

        foreach (var uid in group)
        {
            if (!TryComp(uid, out NestationPoolComponent? pool))
                continue;

            sum += pool.Temperature;
            n++;
        }

        return n == 0 ? NestationTuning.Ambient : sum / n;
    }

    public float PoolLevel(List<EntityUid> group)
    {
        if (group.Count == 0)
            return 0f;

        var sum = 0f;
        var n = 0;

        foreach (var uid in group)
        {
            if (!TryComp(uid, out NestationPoolComponent? pool))
                continue;

            sum += pool.Level;
            n++;
        }

        return n == 0 ? 0f : sum / n;
    }

    private void SetPool(List<EntityUid> group, float temperature, float level)
    {
        foreach (var uid in group)
        {
            if (!TryComp(uid, out NestationPoolComponent? pool))
                continue;

            pool.Temperature = temperature;
            pool.Level = level;
        }
    }

    private float PoolCapacity(List<EntityUid> group, float level)
    {
        var total = 0f;

        foreach (var uid in group)
        {
            if (TryComp(uid, out NestationPoolComponent? pool))
                total += pool.HeatCapacity;
        }

        return total * MathF.Max(level, 0.05f);
    }

    private void AddPoolHeat(List<EntityUid> group, float joules)
    {
        var temp = PoolTemperature(group);
        var level = PoolLevel(group);
        var capacity = PoolCapacity(group, level);

        if (capacity <= 0f)
            return;

        SetPool(group, temp + joules / capacity, level);
    }

    private void StepPools(float dt)
    {
        var visited = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<NestationPoolComponent>();
        var seeds = new List<EntityUid>();

        while (query.MoveNext(out var uid, out _))
        {
            seeds.Add(uid);
        }

        foreach (var seed in seeds)
        {
            if (visited.Contains(seed) || TerminatingOrDeleted(seed))
                continue;

            var group = _graph.GetPoolGroup(seed);

            foreach (var member in group)
            {
                visited.Add(member);
            }

            if (group.Count == 0 || !TryComp(seed, out NestationPoolComponent? proto))
                continue;

            var temp = PoolTemperature(group);
            var level = PoolLevel(group);
            var capacity = PoolCapacity(group, level);
            var boost = 1f + MathF.Max(0f, temp - 70f) / 30f * 1.5f;
            var loss = group.Count * proto.LossPerSegment * MathF.Max(0f, temp - NestationTuning.Ambient) * boost * dt;

            temp -= loss / capacity;
            temp = MathF.Max(temp, NestationTuning.Ambient);

            if (temp > 100f)
            {
                var excess = (temp - 100f) * capacity;
                temp = 100f;
                level -= excess / (group.Count * proto.LatentCapacity);
            }
            else if (level < 1f)
            {
                level += proto.MakeupRate * dt;
            }

            level = Math.Clamp(level, 0f, 1f);
            SetPool(group, temp, level);

            var state = temp > 90f ? NestationVisualState.Hot : temp > 40f ? NestationVisualState.Running : NestationVisualState.Off;

            foreach (var member in group)
            {
                _appearance.SetData(member, NestationVisuals.State, state);
            }
        }
    }
}

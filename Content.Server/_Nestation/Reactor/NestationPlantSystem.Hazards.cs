// Nestation CC BY-NC 3.0
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Nestation.Reactor;
using Content.Shared.Popups;
using Robust.Shared.Localization;

namespace Content.Server._Nestation.Reactor;

public sealed partial class NestationPlantSystem
{
    private readonly List<(TimeSpan Due, EntityUid Player, float Seconds)> _echoes = new();

    private static void Soonest(ref float countdown, float value)
    {
        if (value < 0f)
            value = 0f;

        if (countdown < 0f || value < countdown)
            countdown = value;
    }

    private void Blast(EntityUid uid, float tnt, string popupKey)
    {
        var (total, slope, max) = NestationPhysics.ToExplosion(tnt);
        Log.Info($"Nestation blast: {tnt:0.0} kg TNT, R(1 bar)={NestationPhysics.RadiusAtPressure(tnt, 1f):0.0} m, R(0.3 bar)={NestationPhysics.RadiusAtPressure(tnt, 0.3f):0.0} m, R(0.1 bar)={NestationPhysics.RadiusAtPressure(tnt, 0.1f):0.0} m, safe={NestationPhysics.SafeRadius(tnt):0.0} m");

        _popup.PopupEntity(Loc.GetString(popupKey), uid, PopupType.LargeCaution);
        _explosion.QueueExplosion(
            uid,
            ExplosionSystem.DefaultExplosionPrototypeId,
            total,
            slope,
            max,
            tileBreakScale: 1f,
            maxTileBreak: 2,
            canCreateVacuum: true);

        ScheduleEchoes(uid);
    }

    private void ScheduleEchoes(EntityUid origin)
    {
        var source = _transform.GetMapCoordinates(origin);

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } player)
                continue;

            var position = _transform.GetMapCoordinates(player);

            if (position.MapId != source.MapId)
                continue;

            var delay = (position.Position - source.Position).Length() / NestationPhysics.SoundSpeed;
            _popup.PopupEntity(Loc.GetString("nestation-popup-flash"), player, player, PopupType.LargeCaution);
            _echoes.Add((_timing.CurTime + TimeSpan.FromSeconds(delay), player, delay));
        }
    }

    private void ProcessEchoes()
    {
        for (var i = _echoes.Count - 1; i >= 0; i--)
        {
            var (due, player, seconds) = _echoes[i];

            if (due > _timing.CurTime)
                continue;

            _echoes.RemoveAt(i);

            if (TerminatingOrDeleted(player))
                continue;

            _popup.PopupEntity(
                Loc.GetString("nestation-popup-echo",
                    ("seconds", seconds.ToString("0.00")),
                    ("meters", NestationPhysics.SoundDistance(seconds).ToString("0")),
                    ("km", NestationPhysics.SoundDistanceKm(seconds).ToString("0.00"))),
                player,
                player,
                PopupType.Large);
        }
    }

    private void DestroyCore(NestationPlantContext c, float tnt, string popupKey)
    {
        c.Core.Exploded = true;
        var coords = Transform(c.CoreUid).Coordinates;
        Blast(c.CoreUid, tnt, popupKey);
        Spawn("NestationReactorWreck", coords);
        QueueDel(c.CoreUid);
    }

    private void EvaluateHazards(NestationPlantContext c, float dt)
    {
        var core = c.Core;
        var countdown = -1f;
        var concentration = NestationPhysics.HydrogenConcentration(core.Hydrogen, core.HallVolume);

        core.PotentialTnt = NestationPhysics.HydrogenTnt(core.Hydrogen, concentration);

        if (core.Power > NestationTuning.ExcursionPower)
            core.ExcursionTimer += dt;
        else
            core.ExcursionTimer = 0f;

        if (core.ExcursionTimer >= 0.5f)
        {
            var tnt = NestationPhysics.ExcursionTnt(core.Target, core.DelayedNeutronFraction, core.FuelMass * NestationPhysics.FuelSpecificHeat, core.TempCoefficient);
            DestroyCore(c, MathF.Max(tnt, 5f), "nestation-popup-excursion");
            return;
        }

        var closed = c.PrimaryClosed && core.Breach < 0.01f;
        var primaryPressure = closed ? MathF.Max(155f, NestationTuning.SaturationPressure(core.CoolantTemp)) : 1f;

        if (primaryPressure > 200f)
            core.PressureTimer += dt;
        else
            core.PressureTimer = MathF.Max(0f, core.PressureTimer - dt);

        if (core.PressureTimer >= 1f)
        {
            var tnt = NestationPhysics.VesselBurstTnt(core.Inventory, core.CoolantTemp, primaryPressure, 4f);
            DestroyCore(c, MathF.Max(tnt, 5f), "nestation-popup-vessel");
            return;
        }

        if (core.FuelTemp > core.MeltTemp)
            core.MeltTimer += dt;
        else
            core.MeltTimer = MathF.Max(0f, core.MeltTimer - dt);

        if (core.MeltTimer >= 5f)
        {
            var water = core.Inventory + core.CavityWater;
            var tnt = NestationPhysics.SteamExplosionTnt(core.FuelMass, core.FuelTemp, water);

            if (concentration >= 0.04f)
                tnt += core.PotentialTnt;

            DestroyCore(c, MathF.Max(tnt, 2f), "nestation-popup-meltdown");
            return;
        }

        if (concentration >= 0.04f)
        {
            var ignition = 0.005f + (core.FuelTemp > 560f ? 0.01f : 0f) + 0.25f * concentration;

            if (_random.NextFloat() < 1f - MathF.Exp(-ignition * dt))
            {
                var tnt = core.PotentialTnt;
                core.Hydrogen = 0f;
                Blast(c.CoreUid, MathF.Max(tnt, 1f), "nestation-popup-hydrogen");
                return;
            }

            Soonest(ref countdown, 1f / ignition);
        }
        else if (core.HydrogenRate > 0f)
        {
            var rate = core.HydrogenRate * NestationPhysics.HydrogenVolumePerKg / core.HallVolume;
            Soonest(ref countdown, (0.04f - concentration) / rate);
        }

        if (core.MeltTimer > 0f)
            Soonest(ref countdown, 5f - core.MeltTimer);

        if (core.PressureTimer > 0f)
            Soonest(ref countdown, 1f - core.PressureTimer);

        if (c.Sg != null && c.SteamGeneratorUid != null)
        {
            var sgDamage = DamageFraction(c.SteamGeneratorUid.Value, c.Sg.DamageLimit);
            var burst = c.Sg.BurstPressure * (1f - 0.6f * sgDamage);
            var limit = sgDamage > 0.5f ? 1f : 4f;

            if (c.Sg.Pressure > burst)
                c.Sg.BurstTimer += dt;
            else
                c.Sg.BurstTimer = MathF.Max(0f, c.Sg.BurstTimer - dt);

            if (c.Sg.BurstTimer >= limit)
            {
                var uid = c.SteamGeneratorUid.Value;
                var tnt = NestationPhysics.VesselBurstTnt(c.Sg.WaterMassFull * c.Sg.Level, c.Sg.Temp, c.Sg.Pressure, c.Sg.SteamVolume);
                c.Sg.BurstTimer = 0f;
                Blast(uid, MathF.Max(tnt, 2f), "nestation-popup-sg-burst");
                QueueDel(uid);
            }
            else if (c.Sg.BurstTimer > 0f)
            {
                Soonest(ref countdown, limit - c.Sg.BurstTimer);
            }
        }

        if (c.Turbine != null && c.TurbineUid != null)
        {
            if (c.Turbine.Rpm > NestationTuning.ExplodeRpm)
                c.Turbine.OverspeedTimer += dt;
            else
                c.Turbine.OverspeedTimer = 0f;

            if (c.Turbine.OverspeedTimer >= 0.5f)
            {
                var uid = c.TurbineUid.Value;
                var tnt = NestationPhysics.RotorTnt(c.Turbine.RotorInertia, c.Turbine.Rpm);
                c.Turbine.OverspeedTimer = 0f;
                Blast(uid, MathF.Max(tnt, 2f), "nestation-popup-turbine-burst");
                QueueDel(uid);
            }
            else if (c.Turbine.OverspeedTimer > 0f)
            {
                Soonest(ref countdown, 0.5f - c.Turbine.OverspeedTimer);
            }
        }

        core.Countdown = countdown;

        if (countdown >= 0f)
            core.Warnings |= NestationWarning.MeltImminent;
    }
}

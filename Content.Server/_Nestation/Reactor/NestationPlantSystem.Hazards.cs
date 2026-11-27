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

    private void StartDoom(NestationPlantContext c, float tnt, string key, bool destroys)
    {
        var core = c.Core;
        core.Dooming = true;
        core.DoomTimer = core.DoomDelay;
        core.DoomTnt = tnt;
        core.DoomKey = key;
        core.DoomDestroys = destroys;
        _popup.PopupEntity(Loc.GetString("nestation-popup-critical"), c.CoreUid, PopupType.LargeCaution);
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

        if (core.ForcedCritical > 0f)
            core.ForcedCritical = MathF.Max(0f, core.ForcedCritical - dt);

        UpdateRates(c, dt);

        if (core.Dooming)
        {
            core.DoomTimer -= dt;
            core.Countdown = MathF.Max(core.DoomTimer, 0f);
            core.Warnings |= NestationWarning.MeltImminent;

            if (core.DoomTimer > 0f)
                return;

            if (core.DoomDestroys)
            {
                DestroyCore(c, core.DoomTnt, core.DoomKey);
            }
            else
            {
                var current = NestationPhysics.HydrogenTnt(core.Hydrogen, NestationPhysics.HydrogenConcentration(core.Hydrogen, core.HallVolume));
                core.Hydrogen = 0f;
                core.Dooming = false;
                Blast(c.CoreUid, MathF.Max(MathF.Max(core.DoomTnt, current), 1f), core.DoomKey);
            }

            return;
        }

        if (core.Power > NestationTuning.ExcursionPower)
            core.ExcursionTimer += dt;
        else
            core.ExcursionTimer = 0f;

        if (core.ExcursionTimer >= 0.5f)
        {
            var tnt = NestationPhysics.ExcursionTnt(core.Target, core.DelayedNeutronFraction, core.FuelMass * NestationPhysics.FuelSpecificHeat, core.TempCoefficient);
            StartDoom(c, MathF.Max(tnt, 5f), "nestation-popup-excursion", true);
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
            StartDoom(c, MathF.Max(tnt, 5f), "nestation-popup-vessel", true);
            return;
        }

        if (core.FuelTemp > core.MeltTemp)
            core.MeltTimer += dt;
        else
            core.MeltTimer = MathF.Max(0f, core.MeltTimer - dt);

        if (core.MeltTimer >= 1f)
        {
            var water = core.Inventory + core.CavityWater;
            var tnt = NestationPhysics.SteamExplosionTnt(core.FuelMass, core.FuelTemp, water);

            if (concentration >= 0.04f)
                tnt += core.PotentialTnt;

            StartDoom(c, MathF.Max(tnt, 2f), "nestation-popup-meltdown", true);
            return;
        }

        if (concentration >= 0.04f)
        {
            var ignition = 0.03f + 0.5f * concentration;

            if (_random.NextFloat() < 1f - MathF.Exp(-ignition * dt))
            {
                StartDoom(c, MathF.Max(core.PotentialTnt, 1f), "nestation-popup-hydrogen", false);
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
            Soonest(ref countdown, 1f - core.MeltTimer);

        if (core.PressureTimer > 0f)
            Soonest(ref countdown, 1f - core.PressureTimer);

        if (c.Sg != null && c.SteamGeneratorUid != null)
        {
            var sgDamage = DamageFraction(c.SteamGeneratorUid.Value, c.Sg.DamageLimit);
            var burst = c.Sg.BurstPressure * (1f - 0.6f * sgDamage);
            var limit = sgDamage > 0.5f ? 0.5f : 2f;

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

        PredictCatastrophe(c, closed, ref countdown);

        if (countdown > 600f)
            countdown = -1f;

        core.Countdown = countdown;

        if (countdown >= 0f)
            core.Warnings |= NestationWarning.MeltImminent;
    }

    private static void Smooth(ref float rate, float now, ref float previous, float dt)
    {
        var instant = (now - previous) / dt;
        rate += (instant - rate) * (1f - MathF.Exp(-dt / 8f));
        previous = now;
    }

    private void UpdateRates(NestationPlantContext c, float dt)
    {
        var core = c.Core;

        if (!core.RatesReady)
        {
            core.RatesReady = true;
            core.PrevFuelTemp = core.FuelTemp;
            core.PrevCoolantTemp = core.CoolantTemp;
            core.PrevPower = core.Power;

            if (c.Sg != null)
                c.Sg.PrevPressure = c.Sg.Pressure;

            if (c.Turbine != null)
                c.Turbine.PrevRpm = c.Turbine.Rpm;

            return;
        }

        Smooth(ref core.FuelTempRate, core.FuelTemp, ref core.PrevFuelTemp, dt);
        Smooth(ref core.CoolantTempRate, core.CoolantTemp, ref core.PrevCoolantTemp, dt);
        Smooth(ref core.PowerRate, core.Power, ref core.PrevPower, dt);

        if (c.Sg != null)
            Smooth(ref c.Sg.PressureRate, c.Sg.Pressure, ref c.Sg.PrevPressure, dt);

        if (c.Turbine != null)
            Smooth(ref c.Turbine.RpmRate, c.Turbine.Rpm, ref c.Turbine.PrevRpm, dt);
    }

    private void PredictCatastrophe(NestationPlantContext c, bool closed, ref float countdown)
    {
        var core = c.Core;

        if (core.FuelTemp > 900f && core.FuelTemp < core.MeltTemp && core.FuelTempRate > 0.3f)
            Soonest(ref countdown, (core.MeltTemp - core.FuelTemp) / core.FuelTempRate + 1f);

        if (closed && core.CoolantTemp > 330f && core.CoolantTemp < 370f && core.CoolantTempRate > 0.2f)
            Soonest(ref countdown, (370f - core.CoolantTemp) / core.CoolantTempRate + 1f);

        if (core.Power > 1.15f && core.Power < NestationTuning.ExcursionPower && core.PowerRate > 0.003f)
            Soonest(ref countdown, (NestationTuning.ExcursionPower - core.Power) / core.PowerRate + 0.5f);

        if (c.Sg != null && c.Sg.Pressure > 60f && c.Sg.PressureRate > 0.02f)
        {
            var burst = c.Sg.BurstPressure;

            if (c.Sg.Pressure < burst)
                Soonest(ref countdown, (burst - c.Sg.Pressure) / c.Sg.PressureRate + 2f);
        }

        if (c.Turbine != null && c.Turbine.Rpm > 1.05f && c.Turbine.Rpm < NestationTuning.ExplodeRpm && c.Turbine.RpmRate > 0.0005f)
            Soonest(ref countdown, (NestationTuning.ExplodeRpm - c.Turbine.Rpm) / c.Turbine.RpmRate + 0.5f);
    }
}

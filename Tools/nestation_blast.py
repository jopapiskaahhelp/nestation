# Nestation CC BY-NC 3.0
import math

TS = 20.0
C_W = 4186.0
L = 2.257e6
TNT = 4.184e6
H2_LHV = 1.2e8


def way_wigner(t, t0):
    t = max(t, 1.0)
    return 0.0622 * (t ** -0.2 - (t + t0) ** -0.2)


def evaporation_time(m, dT, p):
    return m * (C_W * dT + L) / p


def zr_rate(tc):
    tk = max(tc + 273.15, 1000.0)
    return 5e-5 * math.exp(22853.0 * (1 / 1473.15 - 1 / tk))


def sadovsky(w, r):
    c = w ** (1 / 3)
    return 0.84 * c / r + 2.7 * c * c / r ** 2 + 7.0 * w / r ** 3


def radius_at(w, bar):
    lo, hi = 0.2, 2000.0
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        if sadovsky(w, mid) > bar:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def h2_eff(conc):
    return 0.30 if conc >= 0.18 else 0.12 if conc >= 0.08 else 0.04


def simulate(scram, jam, breach, pumps, minutes=90, bypass=False, seed=1, verbose=True, step_print=300):
    import random
    rnd = random.Random(seed)
    dt = 0.5
    DT_SCALE, BOOST = 5.0, 4.0
    Tf, Tc = 600.0, 300.0
    n, dec, inv, cavity = 1.0, 0.0, 700.0, 3000.0
    zr_ox, h2 = 0.0, 0.0
    ins = 0.2
    pre, op, sd = 1.0, 18000.0, 0.0
    hall, nominal = 2500.0, 700.0
    melt_t, press_t = 0.0, 0.0
    out = []
    t = 0.0
    result = None
    while t < minutes * 60:
        level = inv / nominal
        wetted = min(1.0, level / 0.4)
        open_loop = breach > 0.01
        sat = 120.0 if open_loop else (3000.0 if bypass else 335.0)
        phi = pumps
        fb = 1 - 0.0007 * (Tf - 600)
        if Tc > 335:
            fb *= 1 - 0.5 * min(1, (Tc - 335) / 30)
        fb *= 0.3 + 0.7 * wetted
        fb = max(0.05, min(2, fb))
        if scram and not jam:
            ins = min(1.0, ins + 0.2 * dt)
        k = (1 - ins) * 1.25 * fb
        target = max(0, min(4, k))
        tau = (0.7 if target > 1.6 else 6) if target > n else 2
        n += (target - n) * (1 - math.exp(-dt / tau))
        if n >= 0.02:
            sd = 0
            op = min(3e7, op + n * dt * DT_SCALE)
            pre += (n - pre) * (1 - math.exp(-dt / 60))
        else:
            sd += dt * DT_SCALE
        dtgt = way_wigner(sd, op) * pre * BOOST if n < 0.02 else 0.0
        dec += (dtgt - dec) * (1 - math.exp(-dt / 3))
        th = 3e6 * (n + dec)
        nat = 0.04 if open_loop else 0.12
        gfc = 1e4 * (nat + (1 - nat) * phi ** 0.8) * (0.04 + 0.96 * wetted)
        if Tc > 340 and not open_loop:
            gfc *= 0.35
        qf = gfc * (Tf - Tc)
        hs = 0.0
        if not open_loop and wetted > 0.5:
            hs = 75e3 * (0.08 + 0.92 * phi ** 0.8) * max(0, Tc - 260)
        qzr = 0.0
        pool = inv + 0.15 * cavity
        if zr_ox < 600 and pool > 0:
            steam = min(1.0, pool / 40)
            rem = 600 - zr_ox
            ox = rem * zr_rate(Tf) * TS * dt * steam
            ox = min(ox, rem, pool / 0.395)
            if ox > 0:
                w = ox * 0.395
                from_inv = min(w, inv)
                inv -= from_inv
                cavity = max(0, cavity - (w - from_inv) / 0.15)
                zr_ox += ox
                h2 += ox * 0.0442
                qzr = ox * 6.42e6 / (dt * TS)
        h2 = max(0, h2 - h2 * 0.0005 * dt)
        cap = (max(inv, 0) * C_W + 5e6) / TS
        Tf += (th + qzr - qf) / 9e4 * dt
        if Tc > sat and inv > 0:
            inv -= min(inv, (Tc - sat) * inv * C_W / L)
            Tc = sat
        net = qf - hs
        if Tc >= sat - 0.01 and net > 0 and inv > 0:
            inv -= min(inv, net * dt * TS / L)
            Tc = sat
        else:
            Tc += net / cap * dt
            if Tc > sat and inv > 0:
                inv -= min(inv, (Tc - sat) * inv * C_W / L)
                Tc = sat
        Tf = max(20, min(6000, Tf))
        Tc = max(20, min(3000, Tc))
        if open_loop:
            leak = 30 + breach * breach * 60
            floor = nominal * 0.35 * (1 - breach)
            if inv > floor:
                inv = max(floor, inv - leak * dt)
        inv = max(0, inv)
        conc = h2 * 11.1 / hall
        closed = not open_loop
        pp = max(155.0, math.exp(11.876 - 3967 / (Tc + 234.4))) if closed else 1.0
        press_t = press_t + dt if pp > 200 else max(0, press_t - dt)
        melt_t = melt_t + dt if Tf > 2800 else max(0, melt_t - dt)
        if press_t >= 1.0:
            w = (0.15 * inv * C_W * max(0, Tc - 100) + max(0, pp - 1) * 1e5 * 4 / 0.3) / TNT
            result = ("разрыв корпуса (BLEVE)", t, max(w, 5), conc, Tf)
            break
        if melt_t >= 5:
            water = inv + cavity
            eff = 0.08 if water > 750 else 0.005
            w = eff * 1500 * 300 * (Tf - 100) / TNT
            if conc >= 0.04:
                w += h2_eff(conc) * h2 * H2_LHV / TNT
            result = ("расплав и паровой взрыв", t, max(w, 2), conc, Tf)
            break
        if conc >= 0.04:
            ign = 0.005 + (0.01 if Tf > 560 else 0) + 0.25 * conc
            if rnd.random() < 1 - math.exp(-ign * dt):
                w = h2_eff(conc) * h2 * H2_LHV / TNT
                result = ("водородный взрыв", t, max(w, 1), conc, Tf)
                break
        if verbose and int(t / dt) % int(step_print / dt) == 0:
            out.append(f"  t={t/60:5.1f} мин n={n*100:5.1f}% Tf={Tf:6.0f} Tc={Tc:5.0f} вода={inv:5.0f} кг Zr={zr_ox:5.1f} H2={h2:5.1f} кг ({conc*100:4.1f}%) распад={3e3*dec:6.0f} кВт")
        t += dt
    return out, result


def report(name, **kw):
    print(f"== {name} ==")
    out, res = simulate(**kw)
    print("\n".join(out))
    if res:
        kind, tt, w, conc, tf = res
        print(f"  >>> {kind} через {tt/60:.1f} мин: {w:.0f} кг ТНТ, R(1 бар)={radius_at(w,1):.0f} м, R(0,3)={radius_at(w,0.3):.0f} м, R(0,1)={radius_at(w,0.1):.0f} м, безопасно 15·∛W={15*w**(1/3):.0f} м")
    else:
        print("  >>> взрыв не произошёл за", kw.get("minutes"), "мин")
    print()


if __name__ == "__main__":
    print("Остаточное тепло по Вэю-Вигнеру (T0 = 5 суток), доля от 3 МВт:")
    for label, t in (("1 с", 1), ("1 мин", 60), ("1 час", 3600), ("1 сутки", 86400)):
        print(f"  {label:8s} {way_wigner(t, 432000)*100:5.2f}%  = {way_wigner(t, 432000)*3000:6.1f} кВт")
    print()
    print("Время выкипания первого контура t = m(cΔT+L)/P (m=700 кг, нагрев 300→335):")
    for p in (3e6, 3e5, 1.9e5, 5e4):
        print(f"  P={p/1e3:6.0f} кВт: {evaporation_time(700, 35, p):8.0f} с физ. = {evaporation_time(700,35,p)/TS:7.0f} с игровых")
    print()
    print("Пароциркониевая реакция Zr + 2H2O -> ZrO2 + 2H2 + 6,42 МДж/кг, 0,0442 кг H2 на кг Zr:")
    for tc in (1000, 1200, 1500, 1800, 2200):
        print(f"  T={tc} C: скорость {zr_rate(tc)*100:8.5f} %/с физ.")
    print()
    print("Формула Садовского, радиусы по избыточному давлению:")
    for w in (5, 20, 50, 100, 250):
        print(f"  W={w:4d} кг: R(1 бар)={radius_at(w,1):5.1f} м, R(0,3)={radius_at(w,0.3):5.1f} м, R(0,1)={radius_at(w,0.1):5.1f} м, безопасно 15·∛W={15*w**(1/3):5.1f} м")
    print()
    print("Звук/вспышка: D = v·t = 343·t м; быстро D ≈ t/3 км")
    print()
    report("Потеря теплоносителя + потеря охлаждения, АЗ сработала (как Фукусима)", scram=True, jam=False, breach=0.5, pumps=0.0, minutes=240, step_print=600)
    report("Внешний взрыв: корпус пробит, стержни заклинило", scram=False, jam=True, breach=0.7, pumps=0.0, minutes=60, step_print=120)
    report("Остановка ГЦН при выведенных защитах (клапан заблокирован)", scram=False, jam=False, breach=0.0, pumps=0.0, minutes=10, bypass=True, step_print=10)
    print("Разброс момента воспламенения водорода (сценарий Фукусима), 8 запусков:")
    for sd in range(8):
        _, res = simulate(True, False, 0.5, 0.0, minutes=240, seed=sd, verbose=False)
        if res:
            kind, tt, w, conc, tf = res
            print(f"  seed {sd}: {kind}, t={tt/60:5.1f} мин, H2={conc*100:4.1f}%, W={w:5.1f} кг ТНТ, R(0,3)={radius_at(w,0.3):4.1f} м")
        else:
            print(f"  seed {sd}: взрыва нет")

# Nestation CC BY-NC 3.0
import math
def psat(T): return math.exp(11.876-3967/(max(T,0)+234.4))
class S: pass
def run(n_pool=24, T=1500, bypass=False, kill_circ_at=None, kill_primary_at=None, setpoint_ramp=True, verbose=True, scram_enabled=True):
    dt=0.5
    core=S(); core.Tf=20; core.Tc=20; core.n=0; core.D=0; core.ins=1.0; core.sp=0.1; core.auto=True; core.scr=False
    sg=S(); sg.T=20; sg.set=20; sg.L=0.5; sg.flow=0
    pumps={'p':0,'f':0,'c':0}; draw={'p':0,'f':0,'c':0}
    rpm=0; rpm=0
    cond=20; pool=20; level=1.0
    reserve=45e6; avail=1
    burn=0
    log=[]
    hq=0
    for step in range(int(T/dt)):
        t=step*dt
        if setpoint_ramp and core.auto and t>20 and not core.scr:
            core.sp=min(1.0, core.sp+0.01*dt)  # ramp 1%/s after 20s
        # pumps
        tgt={'p':1.0,'f':max(0,min(1.1,sg.flow+2*(0.5-sg.L))),'c':max(0.25,min(1.1,hq/2.1e6))}
        if kill_primary_at and t>kill_primary_at: tgt['p']=0
        if kill_circ_at and t>kill_circ_at: tgt['c']=0
        nom={'p':60e3,'f':25e3,'c':15e3}
        for k in pumps:
            goal=tgt[k]*avail
            tau=6 if goal>pumps[k] else 14
            pumps[k]+= (goal-pumps[k])*(1-math.exp(-dt/tau))
            draw[k]=nom[k]*pumps[k]**3
        phiP=pumps['p']; phiF=pumps['f']
        # core
        react=1.25-0.5*burn
        fb=1-0.0007*(core.Tf-600)
        fb=max(0.1,min(2,fb))
        if core.scr:
            core.ins=min(1,core.ins+0.2*dt)
        elif core.auto:
            rate=max(-0.02,min(0.02,(core.n-core.sp)*0.1)); core.ins=max(0,min(1,core.ins+rate*dt))
        target=max(0,min(4,(1-core.ins)*react*fb))
        tau=(0.7 if target>1.6 else 6) if target>core.n else 2
        core.n+=(target-core.n)*(1-math.exp(-dt/tau))
        burn=min(1,burn+core.n*dt/36000)
        core.D+=(0.065*core.n-core.D)*(1-math.exp(-dt/1500))
        th=3e6*(core.n+max(0,core.D-0.065*core.n))
        nat=0.12; gfc=1e4*(nat+(1-nat)*phiP**0.8)
        qf=gfc*(core.Tf-core.Tc)
        dry=max(0.2,min(1,(sg.L-0.05)/0.25))
        hs=75e3*(0.08+0.92*phiP**0.8)*dry*max(0,core.Tc-sg.T)
        core.Tf+=(th-qf)/9e4*dt; core.Tc+=(qf-hs)/4e5*dt
        # sg
        running=core.n>0.02
        sg.set=min(260,sg.set+0.8*dt) if running else max(60,sg.set-0.4*dt)
        demand=max(0,min(4.8e6,hs+6e5*(sg.T-sg.set)/12))
        cap=0
        if sg.T>=180 and cond<120 or True:
            if sg.T>=180: cap=3.3e6
            if not bypass: cap*=max(0,min(1,(1.10-rpm)/0.05))
        tp=min(demand,cap)
        byp=min(demand-tp,1.5e6) if cond<120 else 0
        rel=max(0,sg.T-280)*1.2e5
        sg.T+=(hs-tp-byp-rel)/6e5*dt
        sg.flow=(tp+byp+rel)/3e6
        sg.L=max(0,min(1,sg.L+0.012*(phiF-sg.flow)*dt))
        P=psat(sg.T)
        # turbine
        vac=1 if cond<=60 else max(0.35,1-0.65*(cond-60)/50)
        eta=0.30*vac
        mech=eta*tp
        rpm=1.0 if (sg.T>=180 and True) else rpm
        gross=min(mech,1.2e6) if rpm>=0.95 else 0
        # generator
        own=sum(draw.values())
        cov=min(gross,own); deficit=own-cov; charge=0
        if deficit>1: reserve=max(0,reserve-deficit*dt)
        elif reserve<45e6 and gross>own:
            charge=min(20e3,gross-own); reserve=min(45e6,reserve+charge*dt)
        avail=1 if (deficit<=1 or reserve>0) else max(0,min(1,cov/max(own,1)))
        net=max(0,gross-own-charge)
        # condenser/pool
        hin=max(0,tp-gross)+byp
        hq=hin
        flow=pumps['c']*max(0,min(1,level/0.25))
        g=1.2e5*(0.03+0.97*flow)
        hout=g*max(0,cond-pool)
        cond+=(hin-hout)/1e6*dt
        cond=max(20,min(400,cond))
        cap_pool=n_pool*2.5e6*max(level,0.05)
        pool+=(hout+own)*dt/cap_pool
        boost=1+max(0,pool-70)/30*1.5
        loss=n_pool*6000*max(0,pool-20)*boost*dt
        pool-=loss/cap_pool; pool=max(pool,20)
        if pool>100:
            ex=(pool-100)*cap_pool; pool=100; level-=ex/(n_pool*3e8)
        elif level<1: level+=0.0002*dt
        level=max(0,min(1,level))
        # protections
        trip = core.Tf>850 or core.n>1.15 or (core.n>0.05 and phiP<0.3) or P>60 or core.Tc>330 or (core.n>0.05 and sg.L<0.2) or cond>115
        if scram_enabled and trip and not core.scr:
            core.scr=True; core.auto=False
            if verbose: print(f"SCRAM at t={t:.0f}: Tf={core.Tf:.0f} n={core.n:.2f} P={P:.1f} Tc={core.Tc:.0f} cond={cond:.0f} L={sg.L:.2f}")
        if step%int(60/dt)==0 and verbose:
            print(f"t={t:5.0f} n={core.n*100:5.1f}% Th={th/1e6:4.2f}MW Tf={core.Tf:5.0f} Tc={core.Tc:5.0f} Ts={sg.T:5.0f} P={P:5.1f} L={sg.L:.2f} gross={gross/1e3:5.0f} own={own/1e3:5.1f} net={net/1e3:5.0f} cond={cond:4.0f} pool={pool:4.1f} res={reserve/1e6:4.1f}")
    return dict(net=net,gross=gross,own=own,pool=pool,th=th)
print("== нормальный запуск до 100%, бассейн 24 секции ==")

import io, contextlib
def last(**kw):
    buf=io.StringIO()
    with contextlib.redirect_stdout(buf):
        r=run(**kw)
    return r, buf.getvalue()
print("== номинал, 24 секции ==")
r,_=last(n_pool=24,T=1500,verbose=False); print({k:round(v/1e3,1) if k!='pool' else round(v,1) for k,v in r.items()})
for n in (8,12,16,20,24,32):
    r,_=last(n_pool=n,T=3000,verbose=False)
    print(f"бассейн {n:2d} секций: pool={r['pool']:5.1f}C gross={r['gross']/1e3:5.0f} net={r['net']/1e3:5.0f} кВт")
print("== потеря циркуляции бассейна на t=700, защиты ВКЛ ==")
_,o=last(n_pool=24,T=1500,kill_circ_at=700); print("\n".join(l for l in o.splitlines() if "SCRAM" in l) or "SCRAM не сработал")
print("== остановка ГЦН на t=700, защиты ВЫКЛ (bypass) ==")
_,o=last(n_pool=24,T=1500,kill_primary_at=700,scram_enabled=False)
for l in o.splitlines():
    if l.startswith("t=") and int(l[2:7])>=660 and int(l[2:7])<=1000: print(l)

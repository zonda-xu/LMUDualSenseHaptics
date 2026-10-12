"""Independent Python transcription of the road formulas; not C# execution.

Use as an inspectable numerical specification when the .NET tests cannot run.
The production-code tests are Revision04Tests.cs and Revision05Tests.cs and must still be run on .NET.
"""
from dataclasses import dataclass, replace
import json
import math
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parents[1]
(ROOT/'artifacts/validation').mkdir(parents=True,exist_ok=True)
text=(ROOT/'src/LMUDualSenseHaptics/Core/RoadResponse.cs').read_text(encoding='utf-8')
C={k:float(v) for k,v in re.findall(r'public double (\w+) \{get;init;\}=([.\d]+);',text)}
def clamp(x):return max(0.,min(1.,x))
def smooth(a,b,x):
    u=clamp((x-a)/(b-a));return u*u*(3-2*u)
def ema(p,x,dt,tau):return p+(1-math.exp(-dt/tau))*(x-p)
def env(p,x,dt,a,r):return ema(p,clamp(x),dt,a if x>p else r)
def response(x):
    x=clamp((x-.015)/.985)
    return C['MinimumOutput']*smooth(0,.08,x)+(1-C['MinimumOutput'])*x**C['ResponseGamma']
@dataclass
class Wheel:
    suspension:float=0
    y:float=0
    tire:float=0
    load:float=3000
    surface:int|None=None
    material:str=''
    @property
    def is_kerb(self):return self.surface==5 or self.surface in (None,0) and self.material.lstrip().upper().startswith(('RMBL','RUMBLE','KERB','CURB'))
class Road:
    def __init__(self):
        self.last=None
        self.state=[dict(slow_s=0.,slow_w=0.,slow_t=0.,slow_l=0.,texture=0.,bump=0.,kerb=0.,phase=0.,hz=0.,reversals=0.,direction=0,last_direction=0.,inferred=0.) for _ in range(4)]
    def update(self,t,wheels,speed=20,sensitivity=1,fallback=True):
        if self.last is None or not 0<t-self.last[0]<=.25:
            self.__init__();self.last=(t,wheels);return
        dt=t-self.last[0];moving=smooth(.6,6,speed);contacts=[];evidences=[]
        for z,w,old in zip(self.state,wheels,self.last[1]):
            sv=(w.suspension-old.suspension)/dt;wv=(w.y-old.y)/dt;tv=(w.tire-old.tire)/dt
            lr=(w.load-old.load)/(dt*max((w.load+old.load)*.5,500))
            for key,value,tau in [('slow_s',sv,.18),('slow_w',wv,.18),('slow_t',tv,.18),('slow_l',lr,.10)]:
                z[key]=ema(z[key],value,dt,tau)
            signed=max((sv-z['slow_s'],wv-z['slow_w'],2*(tv-z['slow_t'])),key=abs)
            sv=abs(sv-z['slow_s']);wv=abs(wv-z['slow_w']);tv=abs(tv-z['slow_t']);lr=abs(lr-z['slow_l'])
            sus=smooth(C['SuspensionNoiseMps'],C['SuspensionFullMps'],sv*sensitivity)
            wheel=smooth(C['SuspensionNoiseMps'],C['SuspensionFullMps'],wv*sensitivity)
            tire=smooth(C['TireNoiseMps'],C['TireFullMps'],tv*sensitivity)
            load=smooth(C['LoadNoisePerSec'],C['LoadFullPerSec'],lr*sensitivity)
            contact=w.load>C['ContactMinLoadN'];evidence=max(sus,.8*wheel,.65*tire,.55*load)
            contacts.append(contact);evidences.append(evidence)
            texture=evidence*moving*contact
            bump=max(smooth(C['BumpNoiseMps'],C['BumpFullMps'],sv*sensitivity),.6*smooth(C['BumpLoadNoisePerSec'],C['BumpLoadFullPerSec'],lr*sensitivity))*moving*contact
            z['texture']=env(z['texture'],texture,dt,C['AttackSeconds'],C['ReleaseSeconds'])
            z['bump']=env(z['bump'],bump,dt,.004,.105)
            direction=(1 if signed>0 else -1) if abs(signed)>C['SuspensionNoiseMps']*1.5 else 0
            z['reversals']*=math.exp(-dt/.16)
            if direction and direction!=z['direction']:
                interval=t-z['last_direction']
                if z['direction'] and .008<=interval<=.09:z['reversals']=min(4,z['reversals']+1)
                z['direction']=direction;z['last_direction']=t
            if not contact or moving==0:
                z['texture']=z['bump']=z['kerb']=z['reversals']=0;z['direction']=0
        for i,(z,w) in enumerate(zip(self.state,wheels)):
            contact=contacts[i];evidence=evidences[i]
            opposite=max(self.state[1 if i%2==0 else 0]['texture'],self.state[3 if i%2==0 else 2]['texture'])
            z['inferred']=.55*moving*smooth(1.6,3,z['reversals'])*smooth(.06,.35,z['texture'])*smooth(.06,.20,z['texture']-opposite*1.2) if fallback and not w.is_kerb and w.surface in (None,0) and contact else 0
            kerb=moving*(C['KerbBaseLevel']+(1-C['KerbBaseLevel'])*evidence) if w.is_kerb and contact else z['inferred']
            z['kerb']=env(z['kerb'],kerb,dt,.008,.065)
            z['hz']=max(12,min(48,speed/C['KerbSpacingM']));z['phase']=(z['phase']+z['hz']*dt)%1
            if not contact or moving==0:z['texture']=z['bump']=z['kerb']=z['inferred']=0
        self.last=(t,wheels)
    def render(self,road_gain=.55,kerb_gain=.75,age=0,separation=60):
        road=[];kerb=[]
        for z in self.state:
            level=max(z['texture']*math.exp(-age/C['ReleaseSeconds']),z['bump']*math.exp(-age/.105))
            road.append(response(level)*road_gain)
            carrier=.65+.35*(.5+.5*math.sin(2*math.pi*(z['phase']+z['hz']*min(age,.05))))
            kerb.append(response(z['kerb']*math.exp(-age/.065))*carrier*kerb_gain)
        def spatial(a):
            l=max(a[0],a[2]);r=max(a[1],a[3]);cross=.28*(1-separation/100)
            return [max(l,r*cross),max(r,l*cross)]
        r,k=spatial(road),spatial(kerb)
        return [max(r[0],k[0]),max(r[1],k[1])]

def wheels():return [Wheel() for _ in range(4)]
checks=[];metrics={}
def check(name,value):
    if not value:raise AssertionError(name)
    checks.append(name)

e=Road()
for n in range(300):e.update(n*.01,wheels());checkval=e.render()
check('Smooth-road steady state remains zero',checkval==[0,0])
check('Minimum output curve maps zero to zero',response(0)==0)
x=[response(n/10000) for n in range(10001)]
check('Response curve is monotonic bounded finite',min(x)==0 and max(x)<=1 and all(a<=b for a,b in zip(x,x[1:])))
for index,name in [(0,'front-left'),(3,'rear-right')]:
    e=Road();e.update(0,wheels());w=wheels();w[index].surface=5;e.update(.01,w);a=e.render()
    side=index%2;metrics[name+'_first_tick_bytes']=[round(v*255) for v in a]
    check('Flat tagged kerb produces localized output: '+name,a[side]>.15 and a[side]>a[1-side]*3)
for field,amount in [('suspension',.003),('y',.003),('tire',.001),('load',3400)]:
    e=Road();e.update(0,wheels());w=wheels();setattr(w[0],field,amount);e.update(.01,w);a=e.render()
    check('Independent road input: '+field,a[0]>.08 and a[0]>a[1])
e=Road();peak=0
for n in range(100):
    w=wheels();w[0].suspension=.0008*math.sin(2*math.pi*15*n*.01);e.update(n*.01,w);peak=max(peak,e.render()[0])
metrics['suspension_only_0.8mm_15Hz_peak_byte']=round(peak*255)
check('Sub-millimeter suspension-only oscillation is not force-gated',peak*255>20)
for airborne in (False,True):
    e=Road();e.update(0,wheels());w=[Wheel(suspension=.04,surface=5,load=0 if airborne else 3000) for _ in range(4)]
    e.update(.01,w,speed=20 if airborne else 0)
    check('Airborne gate' if airborne else 'Stationary gate',e.render()==[0,0])
e=Road();e.update(0,wheels())
for n in range(1,20):
    w=wheels();w[0].surface=5;e.update(n*.01,w)
for n in range(20,100):e.update(n*.01,wheels())
check('Kerb release returns to exact output zero',e.render()==[0,0])
e=Road()
for n in range(200):
    w=[Wheel(suspension=.005*math.sin(n*.01)) for _ in range(4)];e.update(n*.01,w)
check('Slow pitch does not produce sustained road feedback',e.render()==[0,0])
peaks=[]
for hz in (60,100,250):
    e=Road()
    for n in range(hz+1):
        w=wheels();w[0].surface=5;e.update(n/hz,w)
    peaks.append(e.state[0]['kerb'])
check('Constant kerb envelope agrees at 60/100/250 Hz',max(peaks)-min(peaks)<1e-10)
e=Road();e.update(0,wheels());w=wheels();w[0].surface=5;e.update(.01,w);e.update(.01,wheels())
check('Invalid source delta resets road state',e.render()==[0,0])
check('Grass / gravel are not mislabelled kerbs',all(not Wheel(surface=x,material='GRASS').is_kerb for x in (0,1,2,3,4,6)))
check('Kerb material fallback works on dry surface',all(Wheel(surface=0,material=x).is_kerb for x in ('rmbl01','RUMBLE','kerb_a','CURB01')))
def inference(both=False,enabled=True,surface=None,single=False):
    e=Road();peak=0
    for n in range(200):
        t=n*.01; y=(.006 if n==20 else 0) if single else .002*math.sin(2*math.pi*20*t)
        w=wheels();w[0].suspension=y;w[0].surface=surface
        if both:w[1].suspension=y;w[1].surface=surface
        e.update(t,w,fallback=enabled);peak=max(peak,e.state[0]['inferred'])
    return peak
metrics['one_side_inferred_peak']=inference()
check('Repeated one-side unlabeled vibration gets an explicitly inferred cue',metrics['one_side_inferred_peak']>.05)
check('Symmetric vibration does not infer a one-side kerb',inference(both=True)==0)
check('Single bump does not infer a continuous kerb',inference(single=True)==0)
check('Disabled inference remains zero',inference(enabled=False)==0)
check('Known grass surface is not relabelled by inference',inference(surface=3)==0)
check('Unknown type explicit kerb material recognized',Wheel(surface=None,material=' CURB_A').is_kerb)
check('Grass gravel wet types reject misleading kerb material',all(not Wheel(surface=x,material='CURB_A').is_kerb for x in (1,2,3,4,6)))
report={'kind':'independent Python numerical specification with synthetic inputs; NOT the C# executable and NOT hardware',
        'passed':len(checks),'checks':checks,'metrics':metrics,'physical_feedback_verified':False}
(ROOT/'artifacts/validation/road-model.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(f'{len(checks)} independent numerical checks passed. These do not replace .NET regression tests.')
print(json.dumps(metrics,indent=2))

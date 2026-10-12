"""Independent Python transcription of the PCM kernel; NOT compiled C# or WASAPI.

No external packages required. Samples are generated in memory and are not played.
Checks cover channel routing, carrier/cadence, continuity, level bounds and expiry.
"""
from dataclasses import dataclass, replace
import hashlib,json,math,struct
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
(ROOT/'artifacts/validation').mkdir(parents=True,exist_ok=True)
@dataclass(frozen=True)
class Side:
    road:float=0; bump:float=0; kerb:float=0; slip:float=0; impact:float=0; engine:float=0; cue:float=0; cadence:float=24
@dataclass(frozen=True)
class Command:
    left:Side=Side(); right:Side=Side(); enabled:bool=True; deadline:float=100
    road_hz:float=75; kerb_hz:float=110; bump_hz:float=45; engine_hz:float=60
    gain:float=.75; right_gain:float=1; swap:bool=False

def safe(x,maximum=1):return max(0,min(maximum,x)) if math.isfinite(x) else 0

def hz(x,fallback):return max(8,min(250,x)) if math.isfinite(x) else fallback

def limit(x):return .85*math.tanh(x/.85) if math.isfinite(x) else 0

class Oscillator:
    def __init__(self,offset):self.offset=offset;self.reset()
    def reset(self):self.phase=[self.offset]*8;self.envelope=[0.]*7;self.dc_input=0.;self.dc_output=0.
    def sine(self,index,freq,dt):
        result=math.sin(2*math.pi*self.phase[index]);p=self.phase[index]+freq*dt;self.phase[index]=p-math.floor(p);return result
    def next(self,s,c,dt,ramp):
        for i,target in enumerate((s.road,s.bump,s.kerb,s.slip,s.impact,s.engine,s.cue)):
            self.envelope[i]+=(safe(target)-self.envelope[i])*ramp
        road,bump,kerb,slip,impact,engine,cue=self.envelope
        texture=.78*self.sine(0,hz(c.road_hz,75),dt)+.22*self.sine(1,hz(c.road_hz*1.617,121),dt)
        cadence=.5+.5*self.sine(4,hz(s.cadence,24),dt);serration=.18+.82*cadence**3
        k=self.sine(3,hz(c.kerb_hz,110),dt)*serration
        b=self.sine(2,hz(c.bump_hz,45),dt);loss=self.sine(5,155,dt)
        motor=self.sine(6,hz(c.engine_hz,60),dt);hit=self.sine(7,52,dt)
        priority=max(road,kerb,bump,impact);background=1-max(0,min(.95,priority*1.8))
        x=road*texture*(1-.45*bump)*(1-.30*kerb)+.85*bump*b+1.15*kerb*k+.55*slip*loss+.85*impact*hit+.45*cue*hit+.65*engine*motor*background
        dc=x-self.dc_input+math.exp(-2*math.pi*12*dt)*self.dc_output
        self.dc_input=x;self.dc_output=dc;return dc
class Synth:
    def __init__(self,rate=48000):self.rate=rate;self.left=Oscillator(0);self.right=Oscillator(.31)
    def render(self,c,frames=4800,start=0):
        output=[[0.,0.,0.,0.] for _ in range(frames)];dt=1/self.rate;ramp=1-math.exp(-dt/.005)
        if not c.enabled or not math.isfinite(c.deadline) or start>=c.deadline:
            self.left.reset();self.right.reset();return output
        for i in range(frames):
            if start+i*dt>=c.deadline:self.left.reset();self.right.reset();break
            l=limit(self.left.next(c.left,c,dt,ramp)*safe(c.gain))
            r=limit(self.right.next(c.right,c,dt,ramp)*safe(c.right_gain,2)*safe(c.gain))
            if c.swap:l,r=r,l
            # C# writes float32, but level accumulation itself uses doubles.
            output[i][2]=struct.unpack('<f',struct.pack('<f',l))[0]
            output[i][3]=struct.unpack('<f',struct.pack('<f',r))[0]
        return output

def channel(a,c):return [x[c] for x in a]
def energy(a,c):return sum(x[c]**2 for x in a)
def zero(a):return all(x==0 for f in a for x in f)
def projection(a,frequency,rate=48000):
    x=channel(a,2);n=len(x)
    # Hann window makes the check tolerant of the intentional inharmonic detail layer.
    real=sum(v*(.5-.5*math.cos(2*math.pi*i/(n-1)))*math.cos(2*math.pi*frequency*i/rate) for i,v in enumerate(x))
    imag=sum(v*(.5-.5*math.cos(2*math.pi*i/(n-1)))*math.sin(2*math.pi*frequency*i/rate) for i,v in enumerate(x))
    return math.hypot(real,imag)/n
checks=[];metrics={}
def check(name,value):
    if not value:raise AssertionError(name)
    checks.append(name)
c=Command(left=Side(road=.4));a=Synth().render(c)
check('Left haptic signal maps to channel 3 only',energy(a,2)>1 and energy(a,3)==0)
check('First two speaker channels remain exact zero',energy(a,0)==energy(a,1)==0)
check('PCM waveform is bipolar',min(channel(a,2))<0<max(channel(a,2)))
b=Synth().render(Command(right=Side(road=.4)))
check('Right haptic signal maps to channel 4 only',energy(b,3)>1 and energy(b,2)==0)
swapped=Synth().render(replace(c,swap=True))
check('Channel swap is sample-exact',all(x[2]==y[3] and x[3]==y[2] for x,y in zip(a,swapped)))
c=Command(left=Side(.3,.2,.4,.1,0,.1,0))
a=Synth().render(c);s=Synth();b=[]
for i in range(10):b.extend(s.render(c,480,i*.01))
check('Buffer partitioning does not change oscillator phase',a==b)
check('OFF is digital silence',zero(Synth().render(replace(c,enabled=False))))
check('Zero envelopes have no unconditional minimum vibration',zero(Synth().render(Command())))
check('Master zero mutes PCM',zero(Synth().render(replace(c,gain=0))))
a=Synth().render(replace(c,deadline=.01),960)
check('Deadline expires inside a buffer',not zero(a[:480]) and zero(a[480:]))
check('Already expired command is silent',zero(Synth().render(replace(c,deadline=.01),start=.02)))
check('Nonfinite command deadline is rejected',zero(Synth().render(replace(c,deadline=float('nan')))))
check('Nonfinite master gain is muted',zero(Synth().render(replace(c,gain=float('nan')))))
extreme=Command(left=Side(10,10,10,10,10,10,10),right=Side(10,10,10,10,10,10,10),gain=99,right_gain=99)
a=Synth().render(extreme)
check('Limiter is finite and bounded',all(math.isfinite(x) and abs(x)<=.850001 for f in a for x in f))
metrics['all_effects_peak_full_scale']=max(abs(x) for f in a for x in f)
kerb=Command(left=Side(kerb=.6));a=Synth().render(kerb,24000)
check('Kerb has its own carrier at 110 Hz',projection(a,110)>5*projection(a,75))
check('Kerb cadence creates modulation sidebands',projection(a,86)>.05*projection(a,110) and projection(a,134)>.05*projection(a,110))
check('Changing cadence changes kerb waveform',Synth().render(kerb)!=Synth().render(replace(kerb,left=replace(kerb.left,cadence=40))))
road=Command(left=Side(road=.4));a=Synth().render(road,24000)
check('Road carrier concentrates energy near 75 Hz',projection(a,75)>5*projection(a,110))
metrics['road_rms_full_scale']=math.sqrt(energy(a,2)/len(a))
check('Changing road carrier changes the waveform',Synth().render(road)!=Synth().render(replace(road,road_hz=100)))
a=Synth().render(road,48000)
metrics['road_mean_after_200ms']=sum(channel(a[9600:],2))/len(a[9600:])
check('Steady road output has negligible DC mean',abs(metrics['road_mean_after_200ms'])<.003)
for rate in (44100,48000,96000):
    a=Synth(rate).render(road,int(rate*.02))
    check('Sample-rate-independent stereo routing at '+str(rate),energy(a,2)>0 and energy(a,3)==energy(a,0)==energy(a,1)==0)
report={'kind':'independent Python PCM specification; NOT compiled C#, NOT Windows COM/WASAPI, NOT hardware',
        'passed':len(checks),'checks':checks,'metrics':metrics,'physical_feedback_verified':False,
        'production_source_sha256':hashlib.sha256((ROOT/'src/LMUDualSenseHaptics/Haptics/VoiceCoilSynthesizer.cs').read_bytes()).hexdigest()}
(ROOT/'artifacts/validation/audio-model.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(f'{len(checks)} independent PCM checks passed. This is not C# or WASAPI execution.')
print(json.dumps(metrics,indent=2))

"""ACC-inspired synthetic LMU replays; production pipeline/PCM, no game or HID access."""
import argparse
import copy
import json
import math
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', default='dotnet')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
fixture = root/'tests/LMUDualSenseHaptics.Tests/fixtures/clean.synthetic.jsonl'
lines = fixture.read_text(encoding='utf-8').splitlines()
profile = json.loads(lines[0])['Profile']
template = json.loads(lines[1])['Frame']
dll = root/'tests/LMUDualSenseHaptics.Tests/bin/Release/net8.0/LMUDualSenseHaptics.Tests.dll'
folder = root/'artifacts/replay-acc-reference'
folder.mkdir(parents=True,exist_ok=True)
(root/'artifacts/validation').mkdir(parents=True,exist_ok=True)

# Scenario designs follow GripLossFeedbackChecks.cs from the pinned MIT-licensed
# ACC project, but the numbers below generate LMU frames, not ACC recordings.
cases = ('normal','loaded-corner','loaded-corner-high','understeer','rear-recovery','left-kerb','right-kerb',
         'left-grass','right-grass','wheelspin','tc-wheelspin')

def make_frame(case,n):
    f=copy.deepcopy(template); t=n*.01; speed=100/3.6
    f.update(Sequence=n,CapturedAtSec=t,SourceAgeSec=0,SourceTimeSec=t,Connected=True,Driving=True,Paused=False,
             CarId='acc-reference-synthetic',VehicleModel='Reference',VehicleClass='Hypercar',GameVersion=14200,
             SpeedMps=speed,LocalVxMps=0,LocalVyMps=0,LocalVzMps=-speed,AccelX=0,AccelY=0,AccelZ=0,
             Brake01=0,Throttle01=0,Steering=.2,LocalYawRate=.25,Gear=4,Rpm=5000,MaxRpm=8000,
             AbsIntervening=False,TcIntervening=False,LastImpactTime=0,LastImpactMagnitude=0)
    front=.04; rear=[.04,.04]; body=0
    if case=='loaded-corner': front=.13;rear=[.09,.09];body=.06
    if case=='loaded-corner-high': front=.19;rear=[.12,.12];body=.06
    if case=='understeer': front=.30;rear=[.06,.06];body=.05
    if case=='rear-recovery':
        if 40<=n<100: front=.07;rear=[.35,.35];body=.32
        elif 100<=n<114:
            front=.26;rear=[.14,.14];body=.216;f['Steering']=-.4;f['LocalYawRate']=-.2
        elif n>=114:front=.08;rear=[.09,.09];body=.06
    side_case=case in ('left-kerb','right-kerb','left-grass','right-grass')
    if side_case:
        front=.05;body=.06
        rear[0 if case.startswith('left') else 1]=.40
    if case in ('wheelspin','tc-wheelspin'):
        front=.01;rear=[.01,.01];f['Steering']=.01;f['LocalYawRate']=.02
        if n>=140:f['Throttle01']=1;f['TcIntervening']=case=='tc-wheelspin'
    f['LocalVxMps']=body*speed
    for i,w in enumerate(f['Wheels']):
        angle=front if i<2 else rear[i-2]
        w.update(OmegaRadPerSec=-speed/.35,SuspDeflectionM=0,SuspForceN=3000,TireLoadN=3000,
                 TireVerticalDeflectionM=.01,GroundLongitudinalMps=-speed,
                 GroundLateralMps=math.tan(angle)*speed,SurfaceType=0,TerrainName='ROAD',Valid=True)
        if side_case and i==(2 if case.startswith('left') else 3):
            w['SurfaceType']=5 if case.endswith('kerb') else 2
            w['TerrainName']='KERB' if case.endswith('kerb') else 'GRASS'
            w['SuspDeflectionM']=.002*math.sin(2*math.pi*15*t)
        if case in ('wheelspin','tc-wheelspin') and n>=140 and i>=2:w['OmegaRadPerSec']*=1.8
    return f

results=[]
for case in cases:
    p=copy.deepcopy(profile)
    p['SpatialSeparation']=100
    for key in ('Engine','Impact','Shift','Abs'):p[key]={'Enabled':False,'Strength':0}
    path=folder/(case+'.jsonl'); output=folder/(case+'.replay.jsonl')
    with path.open('w',encoding='utf-8',newline='\n') as stream:
        stream.write(json.dumps({'Kind':'profile','Profile':p},ensure_ascii=False)+'\n')
        for n in range(300):stream.write(json.dumps({'Kind':'telemetry','Frame':make_frame(case,n)},ensure_ascii=False)+'\n')
    subprocess.run([args.dotnet,str(dll),'--replay',str(path),str(output)],check=True,capture_output=True,text=True,encoding='utf-8')
    with output.open(encoding='utf-8') as stream: rows=[json.loads(line) for line in stream]
    driving=[r for r in rows if r['AdvancedFeedbackEnabled']]
    assert driving and rows[-1]['Output']['LeftActuator']==rows[-1]['Output']['RightActuator']==0,case
    assert rows[-1]['Output']['LeftTrigger']['Mode']==rows[-1]['Output']['RightTrigger']['Mode']==5,case
    assert not rows[-1]['Audio']['Enabled'] and all(v==0 for v in rows[-1]['AudioLevels'].values()),case
    assert all(r['SpeakerChannelsSilent'] and r['Transport']['UseAudioHaptics'] for r in rows),case
    assert all(r['Transport']['LeftActuator']==r['Transport']['RightActuator']==0 for r in rows),case
    assert all(r['HidFlag0']==0x0c and r['HidFlag2']==0x02 for r in rows),case
    assert all(r['Transport']['LeftTrigger']==r['Output']['LeftTrigger'] and r['Transport']['RightTrigger']==r['Output']['RightTrigger'] for r in rows),case
    assert all(0<=v<=.850001 for r in rows for v in r['AudioLevels'].values()),case
    left=max(r['Audio']['Left']['RearDrag'] for r in driving)
    right=max(r['Audio']['Right']['RearDrag'] for r in driving)
    if case=='loaded-corner-high':
        assert all(r['Audio']['Left']['FrontSlip']==0 and r['Audio']['Right']['FrontSlip']==0 and
                   r['Audio']['Left']['RearSlip']==0 and r['Audio']['Right']['RearSlip']==0 for r in driving),case
    if case=='rear-recovery':
        assert left>.1 and right>0,case
        during=[r for r in driving if 1.00<=r['At']<1.14]
        assert during and min(r['Audio']['Right']['RearDrag'] for r in during)>0,case
        settled=[r for r in driving if r['At']>=2.7]
        assert settled and max(r['Audio']['Left']['RearDrag'] for r in settled)<.01,case
    else:assert left==right==0,(case,left,right)
    if case.endswith('kerb'):
        assert max(r['Channels']['Kerb'] for r in driving)>.15,case
        same='LeftRms' if case.startswith('left') else 'RightRms'
        other='RightRms' if case.startswith('left') else 'LeftRms'
        assert max(r['AudioLevels'][same] for r in driving)>3*max(r['AudioLevels'][other] for r in driving),case
    if case in ('wheelspin','tc-wheelspin'):
        active=[r for r in driving if r['At']>=1.45]
        assert active and max(r['Audio']['Left']['RearSlip'] for r in active)>0,case
        expected=0x21 if case=='tc-wheelspin' else 5
        assert all(r['Output']['RightTrigger']['Mode']==expected for r in active),case
    results.append({'case':case,'ticks':len(rows),'driving_frames':len({r['Sequence'] for r in driving}),
                    'rear_drag_left_peak':left,'rear_drag_right_peak':right,'terminal_off':True})
    print(case,'ticks=',len(rows),'PASS')
(root/'artifacts/validation/acc-reference-replays.json').write_text(json.dumps(results,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

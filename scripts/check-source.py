"""Static consistency checks. These do NOT compile C# or instantiate WPF."""
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root=Path(__file__).resolve().parents[1]
(root/'artifacts/validation').mkdir(parents=True,exist_ok=True)
src=root/'src/LMUDualSenseHaptics'
results=[]
def check(name, assertion):
    if not assertion: raise AssertionError(name)
    results.append(name)

for path in list(root.rglob('*.xaml'))+list(root.rglob('*.csproj'))+[root/'Directory.Build.props']:
    ET.parse(path)
    check('XML: '+str(path.relative_to(root)),True)
main=(src/'MainWindow.xaml').read_text(encoding='utf-8')
code='\n'.join(p.read_text(encoding='utf-8') for p in src.glob('MainWindow*.cs'))
for handler in sorted(set(re.findall(r'\b(?:Click|Checked|Unchecked|SelectionChanged)="(\w+)"',main))):
    check('WPF event handler: '+handler, bool(re.search(r'\b'+handler+r'\s*\(',code)))
xaml='\n'.join(p.read_text(encoding='utf-8') for p in src.rglob('*.xaml'))
keys=set(re.findall(r'x:Key="(\w+)"',xaml))
references=set(re.findall(r'\{(?:Static|Dynamic)Resource (\w+)\}',xaml))
check('Every named XAML resource is declared', references<=keys)
for panel in ('BrakeSettings','ThrottleSettings','ExtraTriggerSettings','RoadSettings','OtherGripSettings','SpatialSettings','AudioSettings'):
    check('Generated controls have target panel: '+panel, f'x:Name="{panel}"' in main)

fingerprints=json.loads((root/'tests/baselines/baseline-fingerprints.json').read_text(encoding='utf-8'))
# Four old hardware/session files intentionally changed for audio routing and identity.
# Do not silently rewrite their historical hashes and claim preservation.
check('v0.3 preserved TriggerEffect encoder',hashlib.sha256((src/'Hardware/TriggerEffect.cs').read_bytes()).hexdigest()==fingerprints['unchanged_files']['Hardware/TriggerEffect.cs'])
original=json.loads((root/'tests/baselines/v0.4-preserved-fingerprints.json').read_text(encoding='utf-8'))
for filename,expected in original['unchanged_files'].items():
    # v1.2 deliberately replaces L2 mapping and adds brake telemetry/diagnostics.
    if filename in {'Haptics/TriggerEngine.cs','Telemetry/LmuLayout.cs','Runtime/FeedbackLoop.cs'}: continue
    check('v0.4 byte-identical protected source: '+filename,hashlib.sha256((src/filename).read_bytes()).hexdigest()==expected)

triggers=(src/'Haptics/TriggerEngine.cs').read_text(encoding='utf-8')
engine=(src/'Haptics/GripEngine.cs').read_text(encoding='utf-8')
fragments={
    'trigger_map':triggers[triggers.index('    private static TriggerEffect Map'):],
    'engine_expression':next(line.strip() for line in engine.splitlines() if 'double engine=' in line),
}
for name,value in fragments.items():
    check('v0.3 preserved fragment: '+name,hashlib.sha256(value.encode()).hexdigest()==fingerprints['fragments'][name])
check('TC-only path uses the raw intervention flag','rightSelected = f.TcIntervening && p.Tc.Gain > 0 ? 1 : -1;' in triggers)
check('TC inactive early exit writes OFF','if(rightSelected < 0)return (left,TriggerEffect.Off);' in triggers)
check('R2 preset defaults to TC-only','ThrottleFeedbackMode.TcOnly;' in (src/'Core/Models.cs').read_text(encoding='utf-8'))
parser=(src/'Telemetry/LmuLayout.cs').read_text(encoding='utf-8')
check('Wheel surface / material offsets included','b[q+176]<=6?b[q+176]:null,S(b,q+160,16)' in parser)

# Check the text color pairs used by both themes. WCAG contrast is a numerical
# check only, not a claim of a complete accessibility or native rendering audit.
def lum(color):
    rgb=[int(color[i:i+2],16)/255 for i in (1,3,5)]
    rgb=[x/12.92 if x<=.04045 else ((x+.055)/1.055)**2.4 for x in rgb]
    return sum(a*b for a,b in zip(rgb,(.2126,.7152,.0722)))
def contrast(a,b):
    la,lb=lum(a),lum(b)
    return (max(la,lb)+.05)/(min(la,lb)+.05)
theme=(src/'Theme/ThemeManager.cs').read_text(encoding='utf-8')
roles=re.findall(r'"([A-Za-z]+)"',re.search(r'string\[\] keys=\[(.*?)\];',theme,re.S).group(1))
contrasts={}
for mode in ('light','dark'):
    colors=re.findall(r'"(#[0-9A-F]+)"',re.search(r'string\[\] '+mode+r'=\[(.*?)\];',theme,re.S).group(1))
    palette=dict(zip(roles,colors))
    for foreground,background in [('OnSurface','Surface'),('OnSurfaceVariant','Surface'),('OnSurfaceVariant','SurfaceLow'),('OnPrimary','Primary'),('OnPrimaryContainer','PrimaryContainer'),('Error','ErrorContainer'),('Warning','WarningContainer')]:
        value=contrast(palette[foreground],palette[background]);name=mode+' '+foreground+'/'+background
        contrasts[name]=round(value,2);check('Text contrast >= 4.5: '+name,value>=4.5)

report_builder=(src/'Hardware/DualSenseReportBuilder.cs').read_text(encoding='utf-8')
check('HID audio flag0 clears compatible rumble + haptics-select','frame.UseAudioHaptics?(byte)0x0C:(byte)0x0F' in report_builder)
check('HID audio flag2 clears compatible-vibration-v2','frame.UseAudioHaptics?(byte)0x02:(byte)0x06' in report_builder)
command=(src/'Haptics/VoiceCoilCommand.cs').read_text(encoding='utf-8')
check('Independent audio command watchdog','now + .075' in command)
check('Source-age-aware command deadline','frame.CapturedAtSec - frame.SourceAgeSec' in command)
synth=(src/'Haptics/VoiceCoilSynthesizer.cs').read_text(encoding='utf-8')
check('PCM quad buffer cleared before rendering','interleavedQuad.Clear();' in synth)
check('PCM left routes only channel 3','interleavedQuad[4 * n + 2]' in synth)
check('PCM right routes only channel 4','interleavedQuad[4 * n + 3]' in synth)
check('PCM deadline checked inside each buffer','playbackStart + n * dt >= command.ValidUntil' in synth)
check('PCM DC filter and limiter are present','Math.Exp(-2 * Math.PI * 12 * dt)' in synth and 'Math.Tanh' in synth)
interop=(src/'Hardware/WasapiNative.cs').read_text(encoding='utf-8')
check('PROPVARIANT x64 size is 24 bytes','LayoutKind.Explicit, Size = 24' in interop)
check('IMMDeviceCollection SDK IID','0BD7A1BE-7A1A-44DB-8397-CC5392387B5E' in interop)
check('IAudioClient IID','1CB9AD4C-DBFA-4c32-B178-C2F568A703B2' in interop)
check('IAudioRenderClient IID','F294ACFC-3146-4483-A7BF-ADDCA7C260E2' in interop)
client_body=interop.split('internal interface IAudioClient')[1].split('internal interface IAudioRenderClient')[0]
check('IAudioClient vtable declaration order',re.findall(r'\[PreserveSig\] int (\w+)\(',client_body)==[
    'Initialize','GetBufferSize','GetStreamLatency','GetCurrentPadding','IsFormatSupported','GetMixFormat',
    'GetDevicePeriod','Start','Stop','Reset','SetEventHandle','GetService'])
backend=(src/'Hardware/WasapiVoiceCoilDevice.cs').read_text(encoding='utf-8')
check('WASAPI shared/event format uses zero durations','client.Initialize(0, EventCallback, 0, 0, format' in backend)
check('No automatic default-speaker activation','GetDefaultAudioEndpoint(' not in backend)
worker=(src/'Runtime/VoiceCoilOutput.cs').read_text(encoding='utf-8')
check('Audio thread rechecks time and route after slow opens','latest = Volatile.Read(ref request); now = Now;' in worker and 'latest.Route == route ? latest.Command : VoiceCoilCommand.Off' in worker)
for test in ('AUDIO_LEFT','AUDIO_RIGHT','AUDIO_KERB_LEFT','AUDIO_KERB_RIGHT'):
    check('PCM self-test exposed: '+test, 'Tag="'+test+'"' in main)
check('Production tests include PCM format implementation','Hardware/AudioPcmFormat.cs' in (root/'tests/LMUDualSenseHaptics.Tests/LMUDualSenseHaptics.Tests.csproj').read_text(encoding='utf-8'))
check('Production tests invoke v0.5 additions','Revision05Tests.Run(Check);' in (root/'tests/LMUDualSenseHaptics.Tests/Program.cs').read_text(encoding='utf-8'))
runtime=(src/'Runtime/ApplicationCoordinator.cs').read_text(encoding='utf-8')
recording=(src/'Diagnostics/TelemetryRecorder.cs').read_text(encoding='utf-8')
road=(src/'Physics/RoadSignalEngine.cs').read_text(encoding='utf-8')
signal=(src/'Physics/SignalEngine.cs').read_text(encoding='utf-8')
check('Normal recording omits full Base64 snapshots','Convert.ToBase64String' not in runtime and 'Raw=Convert' not in runtime)
check('Telemetry sampled before allocating log object','logSampling.Telemetry(frame.CapturedAtSec))recorder.Record(new' in runtime)
check('Output sampled before allocating log object','logSampling.Output(after))recorder.Record(new OutputRecord' in runtime)
check('Debug sampled separately from UI','if(logSampling.Status(now))debugLog?.Record' in runtime)
check('Recorder reserves a valid final marker inside byte limit','written+bytes.Length+end.Length>maximumBytes' in recording)
check('Road uses shared contact rule','WheelContact.IsGrounded(w, c.ContactMinLoadN)' in road)
check('Learning, kappa and grip slip use shared contact rule',signal.count('WheelContact.IsGrounded(q,c.ContactMinLoad)')==3)
check('Production tests invoke v0.5.2 additions','Revision052Tests.Run(Check);' in (root/'tests/LMUDualSenseHaptics.Tests/Program.cs').read_text(encoding='utf-8'))
# Lexical balance is not a C# type checker, compiler or WPF runtime.
try:
    from pygments import lex
    from pygments.lexers import CSharpLexer
    from pygments.token import Token
except ImportError:
    lexical='SKIPPED: optional Pygments not installed'
else:
    for path in list(src.rglob('*.cs'))+list((root/'tests/LMUDualSenseHaptics.Tests').glob('*.cs')):
        stack=[]
        for kind,value in lex(path.read_text(encoding='utf-8'),CSharpLexer()):
            if kind in Token.Comment or kind in Token.Literal.String:continue
            for ch in value:
                if ch in '({[':stack.append(ch)
                elif ch in ')}]':
                    if not stack or stack.pop()!={')':'(',']':'[','}':'{'}[ch]:raise AssertionError('Lexical delimiter mismatch: '+str(path))
        check('C# lexical delimiters: '+str(path.relative_to(root)),not stack)
    lexical='Checked delimiters only; no type checking'
report={'kind' :'static source/XML/resource consistency checks; NOT a C# build or WPF run',
        'passed':len(results),'checks':results,'contrast_ratios':contrasts,
        'csharp_execution':'Not executed by this script; see artifacts/build-logs or the GitHub Actions run',
        'lexical_validation':lexical,
        'native_ui_execution':'NOT RUN: Linux environment; no WPF desktop'}
(root/'artifacts/validation/static-checks.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(f'{len(results)} static checks passed. No C# compilation or WPF execution is implied.')
print(json.dumps(contrasts,indent=2))

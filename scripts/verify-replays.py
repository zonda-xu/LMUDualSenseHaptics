"""Verify synthetic scenes and optional real Raw recordings after a Release build."""
import argparse
import concurrent.futures
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--recordings', nargs='*', type=Path, default=[])
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
(root/'artifacts/validation').mkdir(parents=True,exist_ok=True)
dll = root / 'tests/LMUDualSenseHaptics.Tests/bin/Release/net8.0/LMUDualSenseHaptics.Tests.dll'
fixtures = root / 'tests/LMUDualSenseHaptics.Tests/fixtures'
raw_dir = root / 'artifacts/replay-raw'
raw_dir.mkdir(parents=True, exist_ok=True)
jobs = [('synthetic', p, p.with_name(p.name.replace('.synthetic.', '.replay.')))
        for p in sorted(fixtures.glob('*.synthetic.jsonl'))]
jobs += [('raw', p, raw_dir / (p.stem + '.replay.jsonl')) for p in args.recordings]

def verify(job):
    mode, source, destination = job
    command = [args.dotnet, str(dll), '--replay-raw' if mode == 'raw' else '--replay', str(source), str(destination)]
    run = subprocess.run(command, check=True, capture_output=True, text=True, encoding='utf-8')
    rows = [json.loads(line) for line in destination.read_text(encoding='utf-8').splitlines()]
    driving = [row for row in rows if row['State'].startswith('Driving')]
    assert driving, source.name
    last = rows[-1]['Output']
    assert last['LeftActuator'] == last['RightActuator'] == 0
    assert last['LeftTrigger']['Mode'] == last['RightTrigger']['Mode'] == 5
    assert all(value == 0 for value in rows[-1]['Channels'].values())
    assert all(row['AdvancedFeedbackEnabled'] for row in driving)
    assert all(row['SpeakerChannelsSilent'] for row in rows)
    assert all(row['Transport']['UseAudioHaptics'] and row['Transport']['LeftActuator']==row['Transport']['RightActuator']==0 for row in rows)
    assert all(row['HidFlag0']==0x0C and row['HidFlag2']==0x02 for row in rows)
    assert all(row['Transport']['LeftTrigger']==row['Output']['LeftTrigger'] and row['Transport']['RightTrigger']==row['Output']['RightTrigger'] for row in rows)
    assert not rows[-1]['Audio']['Enabled'] and all(x==0 for x in rows[-1]['AudioLevels'].values())
    assert all(0<=v<=.850001 for row in rows for v in row['AudioLevels'].values())
    # All bundled scenes and supplied old profiles use the new default TcOnly mode.
    assert all(row['Output']['RightTrigger']['Mode'] == (0x21 if row['RawTc'] else 5) for row in driving)
    peaks = {key: max(row['Channels'][key] for row in driving) for key in driving[0]['Channels']}
    result = dict(recording=source.name, mode=mode, ticks=len(rows), driving_frames=len({row['Sequence'] for row in driving}),
                  driving_ticks=len(driving), terminal_off=True, advanced_algorithms_enabled=True,
                  channel_peaks=peaks, console=run.stdout.strip())
    result.update(pcm_peak_levels={key:max(row['AudioLevels'][key] for row in rows) for key in rows[0]['AudioLevels']},
                  pcm_sample_rate=48000, pcm_frames=len(rows)*192, speaker_channels_silent=True,
                  hid_grip_bytes_zero=True, trigger_encoding_preserved=True, terminal_pcm_silent=True,
                  verification='production telemetry filters, PCM synthesis and HID report; no WASAPI device access')
    scene = source.name.split('.')[0]
    if mode == 'synthetic':
        if scene in ('tagged-left-kerb', 'tagged-right-kerb'):
            assert peaks['Kerb'] > .15 and peaks['Road'] == peaks['Impact'] == 0
            side = 'LeftActuator' if scene == 'tagged-left-kerb' else 'RightActuator'
            other = 'RightActuator' if scene == 'tagged-left-kerb' else 'LeftActuator'
            assert max(row['Output'][side] for row in driving) > 3 * max(row['Output'][other] for row in driving)
            pcm_side='LeftRms' if scene=='tagged-left-kerb' else 'RightRms'
            pcm_other='RightRms' if scene=='tagged-left-kerb' else 'LeftRms'
            assert result['pcm_peak_levels'][pcm_side] > 3*result['pcm_peak_levels'][pcm_other]
            assert result['pcm_peak_levels'][pcm_side]>.03
        if scene == 'tc-toggle':
            assert any(row['RawTc'] for row in driving) and any(not row['RawTc'] for row in driving)
            assert peaks['Tc'] == .75
        if scene in ('left-kerb', 'right-kerb', 'landing'):
            assert peaks['Road'] > 0.05
            if scene != 'landing':
                side = 'LeftActuator' if scene == 'left-kerb' else 'RightActuator'
                other = 'RightActuator' if scene == 'left-kerb' else 'LeftActuator'
                assert max(row['Output'][side] for row in driving) > max(row['Output'][other] for row in driving)
            assert max(result['pcm_peak_levels'].values())>.01
            assert peaks['Impact'] == 0
        if scene in ('clean', 'corner'):
            assert peaks['Road'] == peaks['GripSlip'] == peaks['Impact'] == 0
        if scene == 'heavy-brake':
            assert peaks['Abs'] > 0 and peaks['Impact'] == 0
        if scene == 'collision':
            assert peaks['Impact'] > 0
    else:
        count = old_driving = 0
        speeds = []
        for line in source.open(encoding='utf-8'):
            item = json.loads(line)
            if item.get('Kind') == 'telemetry':
                count += 1
                old_driving += bool(item['Frame']['Driving'])
                speeds.append(item['Frame']['SpeedMps'])
        result.update(source_frames=count, source_driving_frames=old_driving,
                      speed_range_mps=[min(speeds), max(speeds)],
                      non_off_driving_ticks=sum(bool(row['Output']['LeftTrigger']['Mode'] != 5 or row['Output']['RightTrigger']['Mode'] != 5 or row['Output']['LeftActuator'] or row['Output']['RightActuator']) for row in driving))
        assert result['driving_frames'] == count
        assert result['non_off_driving_ticks'] == len(driving)
        # Supplied records are stationary: enabled algorithms must not fabricate road/slip/collision.
        if max(speeds) < .01:
            assert peaks['Road'] == peaks['GripSlip'] == peaks['Impact'] == 0
            result['stationary_advanced_false_positives'] = 0
    return result

with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
    results = list(pool.map(verify, jobs))
(root / 'artifacts/validation/replays.json').write_text(json.dumps(results, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
(root / 'artifacts/validation/replays.txt').write_text('\n'.join(f"{r['recording']}: {r['console']}; assertions and terminal OFF passed" for r in results) + '\n', encoding='utf-8')
if args.recordings:
    real = dict(verification='offline parser and algorithm replay; not a hardware or dynamic calibration claim',
                results=[r for r in results if r['mode'] == 'raw'])
    (root / 'artifacts/validation/recordings.json').write_text(json.dumps(real, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
for result in results:
    print(result['recording'], 'ticks=', result['ticks'], 'driving frames=', result['driving_frames'], 'PASS')

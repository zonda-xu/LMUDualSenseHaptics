"""Audit unique physics frames from production replay, never 250 Hz output counts.
Usage: python scripts/evaluate-brake-reference.py recording.jsonl replay.jsonl
The result is private analysis; do not commit recordings or their derived rows.
"""
import argparse
import json
from pathlib import Path


def evaluate(recording, replay):
    frames = {}
    for line in recording:
        d = json.loads(line)
        if d.get('Kind') == 'telemetry':
            f = d['Frame']; frames[(f['Sequence'], f['CapturedAtSec'])] = f
    n = ready = applied = accepted = 0
    seen = set(); errors = []; samples_by_context = {}; max_context_samples = 0
    previous = None; stop = None; contact_stops = unconfirmed = censored = 0
    for line in replay:
        r = json.loads(line); key = (r['Sequence'], r['FrameCapturedAtSec'])
        if key in seen or not r['State'].startswith('Driving'): continue
        seen.add(key); f = frames[key]
        identity = (f['CarId'], f.get('VehicleClass'), f.get('VehicleModel'))
        # Only the non-ABS renderer supplies learning information.
        b = r['BrakeBudget']; eligible = f['SpeedMps'] >= 5 and f['Gear'] >= 0 and r['BrakeValidWheels'] == 4 and b['Prompt'] > 0
        if eligible:
            n += 1; ready += b['Confidence'] >= .3; applied += r['BrakeReferenceApplied']
        max_context_samples = max(max_context_samples, b['Stops'])
        if b['SampleAccepted']:
            accepted += 1
            samples_by_context[(identity,b['Context'])] = samples_by_context.get((identity,b['Context']), 0) + 1
            # Compare against the actual reference sent BEFORE the onset sample.
            # Same identity, context and a consecutive <=100 ms observation only.
            if previous:
                pr, pf = previous
                gap = f['SourceTimeSec'] - pf['SourceTimeSec']
                if 0 < gap <= .1 and identity == (pf['CarId'], pf.get('VehicleClass'), pf.get('VehicleModel')) and pr['BrakeReferenceApplied'] and pr['BrakeBudget']['Context'] == b['Context']:
                    errors.append(pr['BrakeBudget']['Prompt'] - b['ObservedOnsetBrake'])
        braking = eligible and f['Brake01'] > .02
        if stop and (not braking or stop['identity'] != identity):
            if stop['contact']:
                if f['Brake01'] <= .02 and stop['identity'] == identity:
                    contact_stops += 1; unconfirmed += not stop['slip']
                else: censored += 1
            stop = None
        if braking:
            if stop is None: stop = dict(identity=identity, contact=False, slip=False)
            stop['contact'] |= r['BrakeReferenceApplied'] and f['Brake01'] >= b['Prompt']
            stop['slip'] |= r['BrakeSlip'] >= r['BrakeWarningThreshold']
        previous = (r, f)
    if stop and stop['contact']: censored += 1
    return dict(eligible_physics_frames=n, reference_ready_frames=ready, reference_applied_frames=applied,
                ready_fraction=ready/n if n else None, applied_fraction=applied/n if n else None,
                independent_accepted_onsets=accepted, sampled_contexts=len(samples_by_context), max_stops_in_current_context=max_context_samples,
                pre_onset_prediction_pairs=len(errors), prompt_bias_brake_percentage_points=100*sum(errors)/len(errors) if errors else None,
                prompt_mae_brake_percentage_points=100*sum(map(abs,errors))/len(errors) if errors else None,
                completed_stops_touching_reference=contact_stops, unconfirmed_reference_stops=unconfirmed,
                unconfirmed_fraction=unconfirmed/contact_stops if contact_stops else None, censored_reference_stops=censored,
                false_alert_rate=None, limitation='No independent true grip limit / false-alert labels. A lift can be a successful response; unconfirmed fraction is NOT false-alert rate. Onset is measured slip threshold, not optimum braking.')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('recording', type=Path); parser.add_argument('replay', type=Path)
    args = parser.parse_args()
    with args.recording.open(encoding='utf-8') as a, args.replay.open(encoding='utf-8') as b:
        print(json.dumps(evaluate(a,b), ensure_ascii=False, indent=2))

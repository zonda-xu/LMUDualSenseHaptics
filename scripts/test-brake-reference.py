"""Synthetic checks for reference coverage and held-out prediction accounting."""
import importlib.util
import json
from pathlib import Path
import unittest

spec=importlib.util.spec_from_file_location('audit',Path(__file__).with_name('evaluate-brake-reference.py'))
audit=importlib.util.module_from_spec(spec);spec.loader.exec_module(audit)

def pair(n,brake=.6,slip=0,wall=True,sample=False,onset=None):
    f=dict(Sequence=n,CapturedAtSec=n*.02,SourceTimeSec=n*.02,CarId='synthetic',VehicleClass='Hyper',VehicleModel='synthetic',SpeedMps=30,Gear=3,Brake01=brake)
    r=dict(Sequence=n,FrameCapturedAtSec=n*.02,State='Driving',BrakeValidWheels=4,BrakeReferenceApplied=wall,BrakeSlip=slip,BrakeWarningThreshold=.06,
           BrakeBudget=dict(Prompt=.5,Confidence=.3 if wall else 0,Stops=3 if wall else 0,Context='synthetic',ContextCount=1,SampleAccepted=sample,ObservedOnsetBrake=onset))
    return dict(Kind='telemetry',Frame=f),r

def run(pairs,repeat=1):
    return audit.evaluate([json.dumps(a) for a,b in pairs],[json.dumps(b) for a,b in pairs for _ in range(repeat)])

class AuditTests(unittest.TestCase):
    def test_duplicates_do_not_inflate_coverage_or_samples(self):
        pairs=[pair(0),pair(1,.7,.08,True,True,.6),pair(2,0)]
        a=run(pairs);self.assertEqual(a,run(pairs,6));self.assertEqual(a['independent_accepted_onsets'],1)
    def test_prediction_uses_wall_sent_before_onset(self):
        a=run([pair(0),pair(1,.7,.08,True,True,.6)])
        self.assertEqual(a['pre_onset_prediction_pairs'],1);self.assertAlmostEqual(a['prompt_bias_brake_percentage_points'],-10)
        self.assertAlmostEqual(a['prompt_mae_brake_percentage_points'],10)
    def test_zero_coverage_does_not_claim_zero_error(self):
        a=run([pair(0,wall=False),pair(1,.7,.08,False,True,.6)])
        self.assertEqual(a['applied_fraction'],0);self.assertIsNone(a['prompt_mae_brake_percentage_points']);self.assertIsNone(a['false_alert_rate'])
    def test_unconfirmed_and_censored_stops_are_not_false_alerts(self):
        a=run([pair(0),pair(1,0),pair(2)])
        self.assertEqual(a['completed_stops_touching_reference'],1);self.assertEqual(a['unconfirmed_fraction'],1)
        self.assertEqual(a['censored_reference_stops'],1);self.assertIsNone(a['false_alert_rate'])
    def test_changed_context_cannot_score_an_old_wall_prediction(self):
        pairs=[pair(0),pair(1,.7,.08,True,True,.6)];pairs[1][1]['BrakeBudget']['Context']='different'
        self.assertEqual(run(pairs)['pre_onset_prediction_pairs'],0)

if __name__=='__main__': unittest.main()

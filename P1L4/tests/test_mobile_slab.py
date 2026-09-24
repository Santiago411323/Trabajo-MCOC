import json
import math
import os
import sys
import unittest
import subprocess
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import mobile_slab_worker as mobile


def rectangle():
    nodes=[dict(id=i+1,x=x,y=y,z=0) for i,(x,y) in enumerate([(0,0),(4,0),(4,4),(0,4)])]
    return dict(slabs=[dict(id='S',x0=0,y0=0,x1=4,y1=4,z=0)],nodes=nodes,
                elements=[dict(id=i+1,type='viga',nodeI=a,nodeJ=b) for i,(a,b) in enumerate([(1,2),(2,3),(3,4),(4,1)])])


class TransferTests(unittest.TestCase):
    def test_square_center(self):
        nodes,receivers,error=mobile.transfer(rectangle(),'S',2,2,.8)
        self.assertEqual(len(receivers),4)
        for value in nodes.values(): self.assertAlmostEqual(value,.2)
        self.assertLess(error,1e-10)

    def test_bottom_projection(self):
        nodes,receivers,error=mobile.transfer(rectangle(),'S',1,.1,8)
        self.assertEqual(receivers[0]['side'],'bottom')
        self.assertAlmostEqual(nodes[1],6)
        self.assertAlmostEqual(nodes[2],2)

    def test_reject_invalid(self):
        for x,y,p in [(-1,0,.8),(5,0,.8),(2,2,-1),(float('nan'),0,1)]:
            with self.assertRaises(ValueError): mobile.transfer(rectangle(),'S',x,y,p)
        d=rectangle();d['slabs'][0]['openings']=[dict(x0=1,y0=1,x1=3,y1=3)]
        with self.assertRaises(ValueError): mobile.transfer(d,'S',2,2,.8)

    def test_missing_edge_is_not_silently_redistributed(self):
        d=rectangle();d['elements']=d['elements'][1:]
        with self.assertRaises(ValueError): mobile.transfer(d,'S',2,.1,.8)

    def test_area_conservation(self):
        for x,y in [(4,4),(4,12),(12,4),(4,8)]:
            s=dict(x0=0,y0=0,x1=x,y1=y)
            self.assertAlmostEqual(sum(e[3] for e in mobile.edges(s)),x*y)

    def test_actual_model_superposition(self):
        path=mobile.ROOT/'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json'
        data=json.loads(path.read_text(encoding='utf-8-sig'))
        slab=next(s for s in data['slabs'] if s['id']=='L12')
        request=dict(seq=1,slab='L12',x=(slab['x0']+slab['x1'])/2,y=(slab['y0']+slab['y1'])/2,p=.8)
        solver=mobile.Solver(data);result=solver.solve(request)
        nodal,_,_=mobile.transfer(data,'L12',request['x'],request['y'],.8)
        cv=solver.cv;ops=cv.ops;cv.build_model(data)
        cv.apply_nodal_loads({n:[0,0,-p] for n,p in nodal.items()})
        ops.system('BandGeneral');ops.numberer('RCM');ops.constraints(cv.CONSTRAINT_HANDLER)
        ops.integrator('LoadControl',1);ops.algorithm('Linear');ops.analysis('Static')
        self.assertEqual(ops.analyze(1),0)
        for record in result['forces']:
            expected=ops.eleResponse(record['id'],'localForce')
            for actual,want in zip(record['f'],expected): self.assertAlmostEqual(actual,want,places=6)
        model_nodes=set(ops.getNodeTags())
        for record in result['displacements']:
            if record['node'] not in model_nodes:
                self.assertEqual([record['ux'],record['uy'],record['uz']],[0,0,0])
                continue
            expected=ops.nodeDisp(record['node'])
            for actual,want in zip([record['ux'],record['uy'],record['uz']],expected[:3]): self.assertAlmostEqual(actual,want,places=10)
        request['x']+=.1;request['seq']=2
        moved=solver.solve(request)
        self.assertTrue(any(abs(a-b)>1e-7 for row,new in zip(result['forces'],moved['forces']) for a,b in zip(row['f'],new['f'])))
        request['p']=1.6
        doubled=solver.solve(request)
        for row,new in zip(moved['forces'],doubled['forces']):
            for a,b in zip(row['f'],new['f']): self.assertAlmostEqual(2*a,b,places=7)
        request['p']=0;zero=solver.solve(request)
        self.assertTrue(all(v==0 for row in zero['forces'] for v in row['f']))

    def test_worker_protocol_recovers_after_invalid_request(self):
        path=mobile.ROOT/'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json'
        data=json.loads(path.read_text(encoding='utf-8-sig'))
        s=next(s for s in data['slabs'] if s['id']=='L12')
        request=dict(seq=2,slab='L12',x=(s['x0']+s['x1'])/2,y=(s['y0']+s['y1'])/2,p=.8)
        invalid=dict(request,seq=1,x=9999)
        command=[sys.executable,'-u',str(mobile.ROOT/'P1L4/mobile_slab_worker.py'),str(path)]
        env=os.environ.copy();env['PYTHONPATH']=os.pathsep.join(sys.path)
        process=subprocess.run(command,input=json.dumps(invalid)+'\n'+json.dumps(request)+'\n',text=True,capture_output=True,timeout=30,env=env)
        self.assertEqual(process.returncode,0,process.stderr)
        responses=[json.loads(line) for line in process.stdout.splitlines()]
        self.assertEqual(len(responses),2)
        self.assertFalse(responses[0]['ok']);self.assertTrue(responses[1]['ok'])
        self.assertEqual(responses[1]['seq'],2)


if __name__=='__main__': unittest.main()

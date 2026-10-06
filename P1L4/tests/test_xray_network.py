"""Real moving-load DeltaR, reaction balance and isolated fixture for X-Ray C# checks."""
from pathlib import Path
import hashlib
import json
import math
import sys

BASE=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(BASE))
import mobile_slab_worker as mobile

source=BASE/'desktop_model/estructura_p1l4_desktop.json'
raw=source.read_bytes();signature=hashlib.sha256(raw).hexdigest();data=json.loads(raw.decode('utf-8-sig'))
slab=next(s for s in data['slabs'] if s['id']=='L12')
request=dict(seq=1,slab=slab['id'],x=(slab['x0']+slab['x1'])/2,y=(slab['y0']+slab['y1'])/2,p=.8)
solver=mobile.Solver(data,signature);response=solver.solve(request)
assert response['ok'] and response['sourceHash']==signature and response['reactionsAvailable']
assert abs(sum(r['fz'] for r in response['reactions'])-.8)<1e-6
assert abs(sum(r['fx'] for r in response['reactions']))<1e-6 and abs(sum(r['fy'] for r in response['reactions']))<1e-6
nodes={n['id']:n for n in data['nodes']}
nodal,_,_=mobile.transfer(data,request['slab'],request['x'],request['y'],request['p'])
engine=solver.cv;ops=engine.ops

def direct(loads):
    engine.build_model(data);engine.apply_nodal_loads(loads)
    ops.system('BandGeneral');ops.numberer('RCM');ops.constraints(engine.CONSTRAINT_HANDLER)
    ops.integrator('LoadControl',1);ops.algorithm('Linear');ops.analysis('Static')
    assert ops.analyze(1)==0;ops.reactions()
    return ({int(e):list(ops.eleResponse(e,'localForce')) for e in ops.getEleTags()},
            {int(n):list(ops.nodeDisp(n)) for n in ops.getNodeTags()},
            {int(n):list(ops.nodeReaction(n)) for n in ops.getFixedNodes()})

g=engine.dead_nodal_loads(data);point={node:[0,0,-p] for node,p in nodal.items()}
before=direct(g);after=direct(engine.combine_nodal_loads({'G':g,'P':point},{'G':1,'P':1}))
maximum=0
for record in response['forces']:
    expected=[b-a for a,b in zip(before[0][record['id']],after[0][record['id']])]
    maximum=max(maximum,max(abs(a-b) for a,b in zip(record['f'],expected)))
assert maximum<1e-6
for record in response['reactions']:
    expected=[b-a for a,b in zip(before[2][record['node']],after[2][record['node']])]
    assert max(abs(record[name]-expected[i]) for i,name in enumerate(('fx','fy','fz','mx','my','mz')))<1e-6
# Global moment balance is checked against the APPLIED NODAL approximation, not the actor point.
rx=sum(r['mx']+nodes.get(r['node'],dict(y=0,z=0))['y']*r['fz']-nodes.get(r['node'],dict(y=0,z=0))['z']*r['fy'] for r in response['reactions'] if r['declared'])
ry=sum(r['my']+nodes.get(r['node'],dict(x=0,z=0))['z']*r['fx']-nodes.get(r['node'],dict(x=0,z=0))['x']*r['fz'] for r in response['reactions'] if r['declared'])
assert abs(rx-sum(nodes[n]['y']*p for n,p in nodal.items()))<1e-5
assert abs(ry+sum(nodes[n]['x']*p for n,p in nodal.items()))<1e-5
frame=dict(Sequence=response['seq'],Slab=response['slab'],X=response['x'],Y=response['y'],P=response['p'],SourceHash=signature,
           BaseLabel='G (reference test)',TransferError=response['error'],Transferred=response['transferred'],
           Forces=response['forces'],Displacements=response['displacements'],Reactions=response['reactions'],ReactionsAvailable=True,
           LoadedNodes=[n for n,p in nodal.items() if p>1e-9],
           Receivers=[dict(Beam=r['beam'],Side=r['side'],Load=r['load'],Area=r['area'],Position=r['t']) for r in response['receivers']])
members={e['id']:e for e in data['elements']}
expected={}
for r in response['forces']:
    if members[r['id']]['type']=='brazo_rigido':continue
    f=r['f'];value=max(abs(f[4]),abs(f[10]))
    expected[str(r['id'])]=value
output=BASE/'xray_validation';output.mkdir(exist_ok=True)
for name,payload in [('model.json',data),('frame.json',frame),('expected_my.json',expected)]:
    (output/name).write_text(json.dumps(payload,ensure_ascii=False),encoding='utf-8')
assert signature==hashlib.sha256(source.read_bytes()).hexdigest()
print('PASS: DeltaR worker = R(G+person)-R(G); maximum force error',maximum)
print('PASS: reactions match direct difference; force/moment equilibrium of nodal transfer; live model unchanged.')

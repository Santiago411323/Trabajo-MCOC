"""JSON-lines OpenSees worker for the Unity slab load (kN, m).

Rectangular tributary regions follow the existing one-way (>2) / 45-degree
two-way partition. A point belongs to its nearest active edge; boundary ties
are shared. Projection onto that edge is transferred to beam endpoints with
linear weights. This is a nodal approximation, not a shell or beam point load.
No changes to the permanent G/Q cases are made.
"""
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / '.venv' / 'Lib' / 'site-packages'))
sys.path.insert(0, str(ROOT / 'P1L3'))


def edges(s):
    x0, x1 = sorted((s['x0'], s['x1']))
    y0, y1 = sorted((s['y0'], s['y1']))
    dx, dy = x1-x0, y1-y0
    if min(dx, dy) <= 0:
        raise ValueError('Losa degenerada')
    all_edges = [('bottom', (x0,y0), (x1,y0)), ('right',(x1,y0),(x1,y1)),
                 ('top',(x0,y1),(x1,y1)), ('left',(x0,y0),(x0,y1))]
    if max(dx,dy)/min(dx,dy) > 2:
        names = ('left','right') if dx < dy else ('bottom','top')
        return [(name,a,b,dx*dy/2) for name,a,b in all_edges if name in names]
    short_area = min(dx,dy)**2/4
    return [(name,a,b,short_area if math.dist(a,b)==min(dx,dy) else dx*dy/2-short_area)
            for name,a,b in all_edges]


def contains(s, x, y):
    if not (min(s['x0'],s['x1']) <= x <= max(s['x0'],s['x1']) and
            min(s['y0'],s['y1']) <= y <= max(s['y0'],s['y1'])):
        return False
    # Optional rectangular openings; current exported panels have none.
    return not any(min(h['x0'],h['x1']) <= x <= max(h['x0'],h['x1']) and
                   min(h['y0'],h['y1']) <= y <= max(h['y0'],h['y1'])
                   for h in s.get('openings', []))


def transfer(data, slab_id, x, y, p):
    if not all(math.isfinite(v) for v in (x,y,p)) or p < 0:
        raise ValueError('Posicion y carga deben ser finitas; P >= 0')
    s = next(s for s in data['slabs'] if s['id']==slab_id)
    if not contains(s,x,y):
        raise ValueError('Punto fuera de losa o dentro de un vacio')
    nodes = {n['id']:n for n in data['nodes']}
    distances = []
    for name,a,b,area in edges(s):
        t = max(0,min(1,((x-a[0])*(b[0]-a[0])+(y-a[1])*(b[1]-a[1]))/math.dist(a,b)**2))
        projection = (a[0]+t*(b[0]-a[0]), a[1]+t*(b[1]-a[1]))
        distances.append((math.dist((x,y),projection),name,a,b,area,projection))
    minimum = min(d[0] for d in distances)
    active = [d for d in distances if abs(d[0]-minimum)<1e-7]
    nodal, receivers = {}, []
    for _,name,a,b,area,point in active:
        matches=[]
        for e in data['elements']:
            if e['type']!='viga': continue
            ni,nj=nodes[e['nodeI']],nodes[e['nodeJ']]
            if max(abs(ni['z']-s['z']),abs(nj['z']-s['z']))>0.02: continue
            ai,aj=(ni['x'],ni['y']),(nj['x'],nj['y'])
            length=math.dist(ai,aj)
            if length<1e-8: continue
            # Both endpoints must lie on the edge line; the beam must contain the projection.
            cross=lambda q: abs((q[0]-a[0])*(b[1]-a[1])-(q[1]-a[1])*(b[0]-a[0]))/math.dist(a,b)
            if max(cross(ai),cross(aj))>0.02: continue
            t=((point[0]-ai[0])*(aj[0]-ai[0])+(point[1]-ai[1])*(aj[1]-ai[1]))/length**2
            if -1e-7<=t<=1+1e-7: matches.append((e,max(0,min(1,t))))
        if not matches: raise ValueError('Sin viga receptora en borde '+name+' de '+slab_id)
        # At a shared endpoint two adjacent beams may match; weights still conserve P.
        share=p/len(active)/len(matches)
        for e,t in matches:
            for node,w in ((e['nodeI'],1-t),(e['nodeJ'],t)):
                nodal[node]=nodal.get(node,0)+share*w
            receivers.append(dict(beam=e['id'],side=name,load=share,area=area,t=t))
    error=abs(sum(nodal.values())-p)
    if error>max(1e-6,p*1e-6): raise ValueError('FAIL: conservacion de carga')
    return nodal, receivers, error


class Solver:
    def __init__(self,data):
        import carga_viva_sismo as cv
        self.cv,self.data,self.cache=cv,data,{}

    def unit(self,node):
        if node in self.cache: return self.cache[node]
        cv=self.cv; ops=cv.ops
        cv.build_model(self.data)
        cv.apply_nodal_loads({node:[0,0,-1]})
        # b17841c activa Transformation cuando el modelo contiene equalDOF de
        # diafragma. Reutilizar el handler elegido por build_model evita una
        # matriz singular y mantiene el mismo modelo que los casos base.
        ops.system('BandGeneral'); ops.numberer('RCM'); ops.constraints(cv.CONSTRAINT_HANDLER)
        ops.integrator('LoadControl',1); ops.algorithm('Linear'); ops.analysis('Static')
        if ops.analyze(1)!=0: raise ValueError('OpenSees no converge')
        forces={e['id']:list(ops.eleResponse(e['id'],'localForce')) for e in self.data['elements']}
        # El JSON de b17841c también contiene nodos descriptivos de muros que no
        # se crean como nodos OpenSees. Solo se consultan tags realmente modelados.
        model_nodes=set(ops.getNodeTags())
        disp={n['id']:list(ops.nodeDisp(n['id'])) for n in self.data['nodes'] if n['id'] in model_nodes}
        ops.reactions()
        rz=sum(ops.nodeReaction(node,3) for node in model_nodes)
        if abs(rz-1)>1e-5: raise ValueError('FAIL: equilibrio global de reacciones')
        self.cache[node]=(forces,disp)
        return forces,disp

    def solve(self,request):
        nodal,receivers,error=transfer(self.data,request['slab'],request['x'],request['y'],request['p'])
        forces={e['id']:[0.]*12 for e in self.data['elements']}
        disp={n['id']:[0.]*6 for n in self.data['nodes']}
        for node,p in nodal.items():
            if p==0: continue
            f,u=self.unit(node)
            for k in forces: forces[k]=[a+p*b for a,b in zip(forces[k],f[k])]
            for k in disp:
                if k in u: disp[k]=[a+p*b for a,b in zip(disp[k],u[k])]
        if not all(math.isfinite(v) for rows in (forces,disp) for row in rows.values() for v in row):
            raise ValueError('Respuesta no finita')
        return dict(ok=True,seq=request['seq'],slab=request['slab'],x=request['x'],y=request['y'],p=request['p'],
                    error=error,transferred=sum(nodal.values()),receivers=receivers,
                    nodes=[dict(node=k,p=v) for k,v in nodal.items()],
                    forces=[dict(id=k,f=v) for k,v in forces.items()],
                    displacements=[dict(node=k,ux=v[0],uy=v[1],uz=v[2]) for k,v in disp.items()])


if __name__=='__main__':
    data=json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
    solver=Solver(data)
    for line in sys.stdin:
        request={}
        try:
            request=json.loads(line)
            result=solver.solve(request)
        except Exception as exc:
            result=dict(ok=False,seq=request.get('seq',-1),message=str(exc))
        print(json.dumps(result,allow_nan=False),flush=True)

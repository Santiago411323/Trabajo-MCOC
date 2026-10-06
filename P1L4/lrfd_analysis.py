"""Escenarios LRFD independientes: respuestas elásticas OpenSees, sin editar G/Q/C1/C2/C3.

Presión de cubierta equivalente uniforme por receptor; viento nodal uniforme
en fachada envolvente. Son idealizaciones explícitas, no adopción normativa.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

BASE=Path(__file__).resolve().parent
sys.path.insert(0,str(BASE/'seismic'))
from transient_analysis import load_engine


def subtract(rect, cover):
    x0,y0,x1,y1=rect; cx0,cy0,cx1,cy1=cover
    l,r,b,t=max(x0,cx0),min(x1,cx1),max(y0,cy0),min(y1,cy1)
    if r<=l or t<=b:return [rect]
    return [q for q in [(x0,y0,l,y1),(r,y0,x1,y1),(l,y0,r,b),(l,t,r,y1)] if q[2]>q[0] and q[3]>q[1]]


def roof_load(data,catalog,cvm):
    receivers={s['id']:s for s in catalog['slabs']};totals={};area=0
    for slab in data['slabs']:
        parts=[(min(slab['x0'],slab['x1']),min(slab['y0'],slab['y1']),max(slab['x0'],slab['x1']),max(slab['y0'],slab['y1']))]
        for higher in data['slabs']:
            if slab['id']!='L96' and higher['z']>slab['z']+.01:
                rect=(min(higher['x0'],higher['x1']),min(higher['y0'],higher['y1']),max(higher['x0'],higher['x1']),max(higher['y0'],higher['y1']))
                parts=[q for p in parts for q in subtract(p,rect)]
        for hole in slab.get('openings',[]):
            parts=[q for p in parts for q in subtract(p,(hole['x0'],hole['y0'],hole['x1'],hole['y1']))]
        exposed=sum((p[2]-p[0])*(p[3]-p[1]) for p in parts)
        if exposed<1e-8:continue
        row=receivers.get(slab['id'])
        if row is None:raise ValueError('Cubierta sin receptores: '+slab['id'])
        contributions=[r for edge in row['edges'] for r in edge.get('receivers',[])]
        total=sum(float(r['area']) for r in contributions)
        if total<=0:raise ValueError('Receptores vacíos: '+slab['id'])
        for r in contributions:totals[r['beam']]=totals.get(r['beam'],0)+exposed*float(r['area'])/total
        area+=exposed
    loads=cvm.LoadSet({},element_loads=cvm.beam_gravity_element_loads(data,per_beam_total=lambda element:totals.get(element['id'],0)))
    if abs(cvm.total_load_vector(loads,data)[2]+area)>max(.001,area*1e-6):raise ValueError('No se conserva carga de cubierta')
    return loads,area


def wind_load(data,axis,sign,cvm):
    nodes=cvm.node_map(data);groups={};native=set(cvm.ops.getNodeTags())
    for member in data['elements']:
        groups.setdefault(member.get('sourceBuilding') or 'modelo',set()).update((member['nodeI'],member['nodeJ']))
    loads=cvm.LoadSet({});area=0
    for ids in groups.values():
        ids=ids & native
        points=[nodes[n] for n in ids if n in nodes]
        if not points:continue
        key=('x','y')[axis];cross=('y','x')[axis]
        edge=(min if sign>0 else max)(p[key] for p in points)
        face=[n for n in ids if n in nodes and abs(nodes[n][key]-edge)<.01]
        a=(max(p[cross] for p in points)-min(p[cross] for p in points))*(max(p['z'] for p in points)-min(p['z'] for p in points))
        if not face or a<=0:continue
        for n in face:
            load=loads.setdefault(n,[0.,0.,0.]);load[axis]+=sign*a/len(face)
        area+=a
    if area<=0:raise ValueError('Sin fachada para viento')
    return loads,area


def run_case(data,loads,cvm):
    ops=cvm.ops;nodes=cvm.build_model(data);cvm.apply_nodal_loads(loads)
    ops.system('BandGeneral');ops.numberer('RCM');ops.constraints(cvm.CONSTRAINT_HANDLER)
    ops.integrator('LoadControl',1);ops.algorithm('Linear');ops.analysis('Static')
    if ops.analyze(1)!=0:raise ValueError('OpenSees no converge')
    ops.reactions()
    applied=cvm.total_load_vector(loads,data)
    reaction=[sum(ops.nodeReaction(n)[k] for n in ops.getFixedNodes()) for k in range(3)]
    error=max(abs(applied[i]+reaction[i]) for i in range(3))
    if error>max(.02,max(map(abs,applied))*1e-5):raise ValueError('Equilibrio rechazado: '+str(error))
    native=set(ops.getEleTags());forces=[]
    for element in data['elements']:
        if element['type'] not in ('viga','columna') or element['id'] not in native:continue
        values=list(ops.eleResponse(element['id'],'localForce'))
        if len(values)!=12 or not all(math.isfinite(x) for x in values):raise ValueError('Fuerza local no disponible: '+str(element['id']))
        forces.append({'id':element['id'],'f':values})
    displacements=[dict(node=n,**dict(zip(('ux','uy','uz','rx','ry','rz'),ops.nodeDisp(n)))) for n in ops.getNodeTags()]
    if not all(math.isfinite(x) for d in displacements for k,x in d.items() if k!='node'):raise ValueError('Desplazamiento no finito')
    return {'forces':forces,'displacements':displacements,'applied':applied,'equilibriumError':error,'autoAnchors':list(cvm.AUTO_ANCHORS)}


def source_case(data,name,cvm):
    cvm.build_model(data)
    forces=[]
    for record in data['p1l4']['elementForces']:
        if record['combo']!=name:continue
        f=record['f'];axes=cvm.ELEMENT_AXES.get(record['id'])
        if len(f)!=12 or axes is None:continue
        if data['p1l4'].get('elementForceCoordinates')!='local':
            f=[sum(f[start+k]*axis[k] for k in range(3)) for start in (0,3,6,9) for axis in axes]
        forces.append({'id':record['id'],'f':f})
    disp=[{k:v for k,v in row.items() if k!='combo'} for row in data['p1l4']['displacements'] if row['combo']==name]
    if not forces or not disp:raise ValueError('Caso base ausente: '+name)
    return {'forces':forces,'displacements':disp}


def recipes(s):
    snow=s['snowDepth']*s['snowDensity']*9.80665/1000;rain=s['waterDepth']*9.80665
    pressure=.613*s['windSpeed']**2*s['windCoefficient']/1000
    angle=math.radians(s['windAngle']);x,y=math.cos(angle),-math.sin(angle)
    wind={('WXp' if x>=0 else 'WXm'):pressure*abs(x),('WYp' if y>=0 else 'WYm'):pressure*abs(y)}
    alternatives={'Lr':s['roof'],'S':snow,'R':rain};out=[]
    for u in range(1,8):
        alts=alternatives.items() if u in (2,3,4) else [('S',snow)]
        companions=('L','W') if u==3 else ('L',)
        directions=('EX+','EX-','EY+','EY-') if u in (5,7) else ('EX+',)
        for a,value in alts:
            for companion in companions:
                for direction in directions:
                    f={'G':(1.4 if u==1 else .9 if u>=6 else 1.2)*s['d']}
                    if u in (2,4,5) or u==3 and companion=='L':f['Q']=(1.6 if u==2 else 1)*s['l']
                    if u in (2,3,4):f['Roof']=(1.6 if u==3 else .5)*value
                    if u==5:f['Roof']=.2*snow
                    if u in (4,6) or u==3 and companion=='W':
                        for k,v in wind.items():f[k]=(1.6 if u!=3 else .8)*v
                    if u in (5,7):f[direction[:2]]=1.4*s['e']*(1 if direction.endswith('+') else -1)
                    label=f'U{u} · A={a} · acomp={companion}'+(' · '+direction if u in (5,7) else '')
                    out.append((u,label,f))
    return out


def combine(units,factors):
    active=[(units[k],v) for k,v in factors.items() if v!=0]
    if not active:active=[(units['G'],0)]
    ids=set.intersection(*(set(r['id'] for r in case['forces']) for case,_ in active))
    maps=[({r['id']:r['f'] for r in case['forces']},v) for case,v in active]
    forces=[{'id':i,'f':[sum(m[i][k]*v for m,v in maps) for k in range(12)]} for i in sorted(ids)]
    nodes=set.intersection(*(set(r['node'] for r in case['displacements']) for case,_ in active))
    dm=[({r['node']:r for r in case['displacements']},v) for case,v in active]
    displacements=[dict(node=n,**{k:sum(m[n][k]*v for m,v in dm) for k in ('ux','uy','uz','rx','ry','rz')}) for n in sorted(nodes)]
    return forces,displacements


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--model',type=Path,required=True);parser.add_argument('--scenario',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();data=json.loads(args.model.read_text(encoding='utf-8-sig'));s=json.loads(args.scenario.read_text(encoding='utf-8-sig'))
    if not all(math.isfinite(v) and v>=0 for v in s.values()):raise ValueError('Intensidades inválidas')
    cvm=load_engine();units={k:source_case(data,k,cvm) for k in ('G','Q','EX','EY')}
    catalog_path=args.model.parent/'slab_load_surfaces.json'
    if not catalog_path.exists():catalog_path=BASE/'unity_visualizador/Assets/Resources/slab_load_surfaces.json'
    catalog=json.loads(catalog_path.read_text(encoding='utf-8-sig'))
    cache_key=hashlib.sha256(args.model.read_bytes()+catalog_path.read_bytes()+Path(__file__).read_bytes()).hexdigest()
    cache=BASE/'lrfd_results'/('units_'+cache_key+'.json');cache.parent.mkdir(exist_ok=True)
    if cache.exists():weather=json.loads(cache.read_text(encoding='utf-8'));print('Unidades OpenSees verificadas desde caché.',flush=True)
    else:
        loads,roof_area=roof_load(data,catalog,cvm);print('OpenSees: cubierta distribuida.',flush=True)
        weather={'Roof':run_case(data,loads,cvm),'roofArea':roof_area}
        for axis,name in ((0,'WX'),(1,'WY')):
            for sign,suffix in ((1,'p'),(-1,'m')):
                loads,area=wind_load(data,axis,sign,cvm);print('OpenSees: '+name+suffix,flush=True)
                weather[name+suffix]=run_case(data,loads,cvm);weather[name+suffix]['facadeArea']=area
        cache.write_text(json.dumps(weather,allow_nan=False),encoding='utf-8')
    units.update({k:v for k,v in weather.items() if isinstance(v,dict)})
    variants=[]
    for index,(u,label,factors) in enumerate(recipes(s)):
        forces,disp=combine(units,factors)
        variants.append({'u':u,'name':'LRFD_'+str(index),'label':label,'forces':forces,'displacements':disp})
    out={'modelHash':hashlib.sha256(args.model.read_bytes()).hexdigest(),'scenario':s,'variants':variants,'roofArea':weather['roofArea'],
         'notes':'Análisis lineal; cubierta equivalente repartida por áreas receptoras y fachada envolvente nodal uniforme. E=±EX/±EY exportados, sin simultaneidad ortogonal. Combinaciones de apuntes, no certificación normativa.',
         'equilibriumError':max(weather[k]['equilibriumError'] for k in ('Roof','WXp','WXm','WYp','WYm'))}
    args.output.parent.mkdir(exist_ok=True);temp=args.output.with_suffix('.partial');temp.write_text(json.dumps(out,allow_nan=False),encoding='utf-8');temp.replace(args.output)
    cvm.ops.wipe();print('PASS: U1–U7, '+str(len(variants))+' variantes; equilibrio verificado.',flush=True)

if __name__=='__main__':main()

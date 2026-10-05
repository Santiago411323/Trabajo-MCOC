"""Auditoría geométrica de vigas. No modifica geometría ni resultados estructurales."""
import json
import math
from collections import defaultdict
from itertools import combinations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json'
TOL = 1e-4  # metros

def sub(a, b): return tuple(x-y for x,y in zip(a,b))
def dot(a, b): return sum(x*y for x,y in zip(a,b))
def norm(a): return math.sqrt(dot(a,a))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def key(p): return tuple(round(v,4) for v in p)
def tag(e): return e.get('elementTag', str(e['id']))
def on_segment(p,a,b):
    v=sub(b,a); length=norm(v)
    if length<TOL: return norm(sub(p,a))<TOL
    t=dot(sub(p,a),v)/(length*length)
    return -TOL/length<=t<=1+TOL/length and norm(cross(sub(p,a),v))/length<TOL

def audit(data):
    nodes={n['id']:(n['x'],n['y'],n['z']) for n in data['nodes']}
    elements=data['elements']; beams=[e for e in elements if e['type']=='viga']
    geometry={e['id']:(nodes[e['nodeI']],nodes[e['nodeJ']]) for e in elements}
    overlaps=[]
    for a,b in combinations(beams,2):
        ai,aj=geometry[a['id']]; bi,bj=geometry[b['id']]; u=sub(aj,ai); length=norm(u)
        if length<TOL: continue
        if norm(cross(sub(bi,ai),u))/length>TOL or norm(cross(sub(bj,ai),u))/length>TOL: continue
        t1=dot(sub(bi,ai),u)/length; t2=dot(sub(bj,ai),u)/length
        overlap=min(length,max(t1,t2))-max(0,min(t1,t2))
        if overlap>TOL:
            exact=sorted((key(ai),key(aj)))==sorted((key(bi),key(bj)))
            overlaps.append({'a':tag(a),'b':tag(b),'kind':'exacto' if exact else 'parcial','overlap_m':round(overlap,5),'a_nodes':[a['nodeI'],a['nodeJ']],'b_nodes':[b['nodeI'],b['nodeJ']],'a_points':[ai,aj],'b_points':[bi,bj]})
    ends=defaultdict(list)
    for e in beams:
        for nid in (e['nodeI'],e['nodeJ']): ends[key(nodes[nid])].append((e,nid))
    candidates=[]; uncertain=[]; justified=[]
    for point, incident in ends.items():
        for (a,an),(b,bn) in combinations(incident,2):
            if a['id']==b['id']: continue
            othera=a['nodeJ'] if a['nodeI']==an else a['nodeI']
            otherb=b['nodeJ'] if b['nodeI']==bn else b['nodeI']
            va=sub(nodes[othera],point); vb=sub(nodes[otherb],point)
            la,lb=norm(va),norm(vb)
            if la<TOL or lb<TOL or dot(va,vb)/(la*lb)>-.999999: continue
            blockers=[]; questions=[]
            for e in elements:
                if e['id'] in (a['id'],b['id']): continue
                ei,ej=geometry[e['id']]
                if not on_segment(point,ei,ej): continue
                v=sub(ej,ei); ln=norm(v)
                if e['type']=='columna': blockers.append('columna '+tag(e))
                elif e['type']=='viga' and ln>TOL:
                    cosine=abs(dot(v,va)/(ln*la))
                    if cosine<.02: blockers.append('viga perpendicular '+tag(e))
                    elif cosine<.999999: questions.append('viga oblicua '+tag(e))
                    else: questions.append('otra viga colineal '+tag(e))
                elif e['type'] in ('muro_eq','brazo_rigido'): questions.append('conexion de muro '+tag(e))
            if a.get('sourceBuilding')!=b.get('sourceBuilding'): questions.append('union entre edificios')
            if (a.get('sectionId'),a.get('width_m'),a.get('height_m'))!=(b.get('sectionId'),b.get('width_m'),b.get('height_m')): questions.append('cambio de seccion')
            record={'a':tag(a),'b':tag(b),'ids':[a['id'],b['id']],'node_ids':sorted(set((an,bn))),'point':point,'section':a.get('sectionId'),'building':a.get('sourceBuilding'),'floor':a.get('piso'),'lengths_m':[round(la,5),round(lb,5)],'blockers':sorted(set(blockers)),'questions':sorted(set(questions))}
            if blockers: justified.append(record)
            elif questions: uncertain.append(record)
            else: candidates.append(record)
    parents={e['id']:e['id'] for e in beams}
    def find(i):
        while parents[i]!=i: parents[i]=parents[parents[i]]; i=parents[i]
        return i
    for r in candidates:
        a,b=r['ids']; parents[find(a)]=find(b)
    groups=defaultdict(list)
    for e in beams: groups[find(e['id'])].append(e)
    chains=[]
    for group in groups.values():
        if len(group)<2: continue
        ps=[p for e in group for p in geometry[e['id']]]
        axis=max(range(3),key=lambda k:max(p[k] for p in ps)-min(p[k] for p in ps))
        group.sort(key=lambda e:sum(p[axis] for p in geometry[e['id']]))
        joins=[r for r in candidates if all(i in {e['id'] for e in group} for i in r['ids'])]
        chains.append({'tags':[tag(e) for e in group],'ids':[e['id'] for e in group],'building':group[0].get('sourceBuilding'),'floor':group[0].get('piso'),'points':[min(ps,key=lambda p:p[axis]),max(ps,key=lambda p:p[axis])],'length_m':round(sum(norm(sub(*geometry[e['id']])) for e in group),5),'joins':joins})
    chains.sort(key=lambda c:(c['building'] or '',c['points'][0][2],c['tags'][0]))
    return {'beam_count':len(beams),'overlaps':overlaps,'unjustified_joins':candidates,'continuous_chains':chains,'uncertain_joins':uncertain,'justified_joins':justified}

def main():
    data=json.loads(SOURCE.read_text(encoding='utf-8-sig')); result=audit(data)
    ios=json.loads((ROOT/'P1L4/unity_visualizador_ios/Assets/Resources/estructura_p1l4_unity.json').read_text(encoding='utf-8-sig'))
    result['ios_geometry_matches']=all(data[k]==ios[k] for k in ('nodes','elements'))
    result['coordinates']='OpenSees: X,Y en planta; Z elevacion, metros'
    # Hypothesis for review only: keep the original distributed-load beam segment,
    # and remove the later coincident cantilever edge in a temporary in-memory copy.
    proposed_removals=[r['b'] for r in result['overlaps'] if r['kind']=='exacto']
    hypothetical=dict(data)
    hypothetical['elements']=[e for e in data['elements'] if tag(e) not in proposed_removals]
    after=audit(hypothetical)
    result['hypothesis_without_duplicates']={'removed_tags':proposed_removals,'continuous_chains':after['continuous_chains'],'overlap_count':len(after['overlaps']),'uncertain_count':len(after['uncertain_joins'])}
    analyzed={f['id'] for f in data['p1l4']['elementForces']}
    for overlap in result['overlaps']:
        pair=[e for e in data['elements'] if tag(e) in (overlap['a'],overlap['b'])]
        overlap['both_analyzed']=all(e['id'] in analyzed for e in pair)
    out=ROOT/'revisiones/auditoria_vigas.json'; out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    md=['# Auditoría de vigas — 5 de octubre de 2026','',f"Se revisaron **{result['beam_count']} vigas** del JSON utilizado por P1L4. La geometría iPhone coincide: **{'sí' if result['ios_geometry_matches'] else 'no'}**.",'','Coordenadas OpenSees: X/Y en planta y Z elevación; unidades en metros. Tolerancia geométrica: 0,1 mm. Esta auditoría no cambia barras, cargas, IDs ni resultados.','',f"- Solapes de longitud positiva: {len(result['overlaps'])}.",f"- Uniones colineales sin columna/viga perpendicular ni otra conexión detectada: {len(result['unjustified_joins'])}, agrupadas en {len(result['continuous_chains'])} tramos continuos.",f"- Uniones sin esos separadores pero con conexiones de muro, cambio de sección u otra condición: {len(result['uncertain_joins'])}.",f"- Uniones colineales justificadas por columna o viga perpendicular: {len(result['justified_joins'])}.",'','## Vigas superpuestas','']
    md.extend(['| Vigas | Tipo | Solape (m) |','| --- | --- | --- |']+[f"| {r['a']} / {r['b']} | {r['kind']} | {r['overlap_m']} |" for r in result['overlaps']] if result['overlaps'] else ['No hay vigas con solape exacto ni parcial de sus ejes. Esto no confunde dos vigas contiguas que solo comparten un extremo.'])
    md+=['','## Tramos físicos continuos candidatos','', '| Vigas actuales (orden geométrico) | Nivel Z | Desde (X,Y,Z) | Hasta (X,Y,Z) | Longitud | Nodos intermedios |','| --- | --- | --- | --- | --- | --- |']
    for c in result['continuous_chains']:
        md.append(f"| {' + '.join(c['tags'])} | {c['points'][0][2]} | {c['points'][0]} | {c['points'][1]} | {c['length_m']} | {', '.join(str(n) for j in c['joins'] for n in j['node_ids'])} |")
    md+=['','## Casos que requieren decisión','', '| Vigas | Nodo(s) | Posición | Motivo |','| --- | --- | --- | --- |']
    for r in result['uncertain_joins']: md.append(f"| {r['a']} + {r['b']} | {r['node_ids']} | {r['point']} | {'; '.join(r['questions'])} |")
    md+=['','## Hipótesis después de eliminar duplicadas','',
         f"Evaluación solo en memoria: se excluyen {', '.join(proposed_removals) if proposed_removals else 'ninguna barra (no se detectaron duplicadas)'}. El auditor no recalcula ni escribe el modelo.",
         '', f"Quedan {len(after['continuous_chains'])} tramos candidatos y {len(after['uncertain_joins'])} conexiones ambiguas.", '']
    for c in after['continuous_chains']: md.append(f"- {' + '.join(c['tags'])}: {c['length_m']} m, nivel Z={c['points'][0][2]}.")
    md+=['','Origen de los solapes: el generador `edificio 1/modelo_pasillos.py` añade los bordes del voladizo Y− sobre vigas ya existentes; `P1L3/carga_viva_sismo.py` subdivide las vigas largas en esos nodos, pero no elimina esas vigas coincidentes. Los nodos X=7,51 persisten en las vigas aunque las columnas de esos niveles fueron eliminadas.','']
    md+=['','## Ejemplo E1_113 / E1_114','']
    for r in result['unjustified_joins']:
        if set((r['a'],r['b']))=={'E1_113','E1_114'}: md.append(f"Comparten el nodo {r['node_ids']} en {r['point']}, sin columna ni otra viga. Son {sum(r['lengths_m'])} m continuos y sección {r['section']}.")
    for r in result['justified_joins']:
        if {'E1_112.2','E1_113'}==set((r['a'],r['b'])): md.append(f"La unión E1_112.2 / E1_113 en {r['point']} sí se separa por: {', '.join(r['blockers'])}.")
    md+=['','## Diferencia entre viga física y barra analítica','','Una viga física continua puede estar representada por varias barras OpenSees para conectar otros componentes o distribuir cargas. No se deben sumar ni promediar sus resultados para inventar un nuevo ID. Si se decide fusionar barras del cálculo, hay que conservar cargas y conexiones, reanalizar y regenerar resultados y correspondencias Unity/AR/VR. Una agrupación solo visual debe conservar los IDs analíticos de cada tramo.','','Datos completos, incluidas todas las uniones justificadas y conexiones de muro: `auditoria_vigas.json`.','']
    (ROOT/'revisiones/auditoria_vigas.md').write_text('\n'.join(md),encoding='utf-8')
    print(json.dumps({k:len(result[k]) if isinstance(result[k],list) else result[k] for k in ('beam_count','overlaps','unjustified_joins','continuous_chains','uncertain_joins','justified_joins','ios_geometry_matches')},ensure_ascii=False))
    print('Hipótesis sin duplicadas:',len(after['continuous_chains']),'tramos continuos,',len(after['uncertain_joins']),'casos ambiguos')

if __name__=='__main__': main()

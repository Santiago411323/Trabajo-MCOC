"""Desplaza la abertura 41..45 / 51..55, incluido el subterráneo; exporta un modelo de escritorio aislado."""
import copy
import json
import sys
import os
from pathlib import Path

BASE = Path(__file__).resolve().parent
sys.path.insert(0, str(BASE.parent / "P1L3"))
import carga_viva_sismo as cvm
import exportar_resultados_unity as exporter
import exportar_superficies_carga as surfaces


def shift_opening(data, report):
    nodes = {n["id"]: n for n in data["nodes"]}
    elements = {e["id"]: e for e in data["elements"]}
    next_node = max(nodes) + 1
    next_element = max(elements) + 1
    moved = []
    delta = 1.50
    column=next(e for e in data['elements'] if e.get('elementTag')=='E1_233')
    column_face=nodes[column['nodeI']]['y']-column['width_m']/2
    left_shift=column_face-(-1.10)

    def clone(nid, dy, cache):
        nonlocal next_node
        if nid not in cache:
            n = copy.deepcopy(nodes[nid])
            n.update(id=next_node, y=round(n["y"] + dy, 6), origen="desktop_wall_opening")
            nodes[next_node] = n
            data["nodes"].append(n)
            cache[nid] = next_node
            next_node += 1
        return cache[nid]

    for indices, tip, change in [(range(40,45), "nodeJ", left_shift), (range(50,55), "nodeI", -delta)]:
        displacement=change if tip=='nodeJ' else -change
        panels = [data["walls"][i] for i in indices]
        ids = {eid for wall in panels for eid in wall["analysisElements"]}
        name = elements[min(ids)]["wallName"]
        centers, endpoints = {}, {}
        for wall in panels:
            old = wall[tip]
            wall[tip] = clone(old, displacement, endpoints)
            wall["longitud"] = round(wall["longitud"] + change, 6)
            # Use each changed panel's own t/L interaction curve.
            wall["sourceId"] = "DESKTOP_" + wall.get("sourceId", name)
            moved.append({"wallId": data["walls"].index(wall)+1, "panelZ": wall["panelZ"], "length": wall["longitud"]})
        for eid in sorted(ids):
            e = elements[eid]
            e["nodeI"] = clone(e["nodeI"], displacement/2, centers)
            e["nodeJ"] = clone(e["nodeJ"], displacement/2, centers)
            length = round(e["wallLength_m"] + change, 6)
            e.update(wallLength_m=length, width_m=length, sectionId=f"MURO_{e['wallThickness_m']:.2f}x{length:.2f}")
        for arm in data["elements"]:
            if arm["type"] != "brazo_rigido" or arm.get("sourceId") != name:
                continue
            if arm["nodeI"] in centers:
                arm["nodeI"] = centers[arm["nodeI"]]
                target = nodes[arm["nodeJ"]]
                old_tip = -1.1 if tip == "nodeJ" else 1.3
                if abs(target["y"]-old_tip)<1e-5:
                    arm["nodeJ"] = clone(arm["nodeJ"], displacement, endpoints)
        for support in data["supports"]:
            if support["node"] in centers:
                support["node"]=centers[support["node"]]
        template = next(e for e in data["elements"] if e["type"]=="brazo_rigido" and e.get("sourceId")==name)
        if tip == "nodeJ":
            # The negative-side pier ends at the column face; its rigid link reaches the column axis.
            existing = {(e["nodeI"],e["nodeJ"]) for e in data["elements"] if e["type"]=="brazo_rigido"}
            for old_center,new_center in centers.items():
                z=nodes[old_center]["z"]
                if z<=-4+1e-5:continue
                target=next((n["id"] for n in data["nodes"] if abs(n["x"]+10)<1e-5 and abs(n["y"])<1e-5 and abs(n["z"]-z)<1e-5),None)
                if target is not None and (new_center,target) not in existing:
                    arm=copy.deepcopy(template)
                    arm.update(id=next_element,nodeI=new_center,nodeJ=target,elementTag=f"BRAZO_DESKTOP_{name}_{z:g}_COL")
                    data["elements"].append(arm);next_element+=1
        for row in report["muros"]:
            if row["muro"]==name:
                row["desktopLengthsByLevel"]={"minus4_to_16":panels[0]["longitud"]}
    audit={"gap_before":[-1.1,1.3],"gap_after":[column_face,2.8],"gap_width_m":round(2.8-column_face,6),"shift_model_y_m":delta,
           "column_face_model_y_m":column_face,"clear_opening_from_column_face_m":round(2.8-column['width_m']/2,6),
           "unchanged_outer_edges":[-4.045,8.9],"includes_basement":True,"panels":moved,
           "analysis_note":"Muro izquierdo termina en la cara negativa de E1_233 en cinco niveles, incluido subterraneo; centroide, longitud y brazos actualizados, conservando empotramiento y extremo exterior."}
    data["desktopOpeningEdit"]=audit
    report["desktopOpeningEdit"]=audit
    report["brazos"]=sum(e["type"]=="brazo_rigido" for e in data["elements"])
    return data, report


def main():
    from alinear_eje_edificio2 import prepare
    connectivity=cvm.corregir_conectividad
    def aligned_connectivity(raw,*args,**kwargs):
        aligned,audit=prepare(raw)
        model,report=connectivity(aligned,*args,**kwargs)
        report['desktopE2Alignment']=audit
        return model,report
    cvm.corregir_conectividad=aligned_connectivity
    original=cvm.agregar_muros
    def patched(data, *args, **kwargs):
        return shift_opening(*original(data,*args,**kwargs))
    cvm.agregar_muros=patched
    exporter.JSON_OUT=Path(os.environ.get('MCOC_DESKTOP_STAGE',str(BASE/"desktop_model/estructura_p1l4_desktop.json")))
    exporter.JSON_OUT.parent.mkdir(parents=True,exist_ok=True)
    def desktop_surfaces(data):
        from slab_panels import slab_metadata
        rows=slab_metadata(data,data['slabs'])
        catalog=exporter.JSON_OUT.with_name('slab_load_surfaces.json')
        catalog.write_text(json.dumps(dict(slabs=rows),indent=2,ensure_ascii=False)+'\n',encoding='utf8')
        return rows
    surfaces.export=desktop_surfaces
    exporter.main()


if __name__=="__main__":
    main()

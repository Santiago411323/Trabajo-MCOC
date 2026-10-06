"""Exporta unidades OpenSees verificadas para evaluar LRFD sin Python en móviles."""
import hashlib
import json
from pathlib import Path
import lrfd_analysis as lab

BASE = Path(__file__).resolve().parent

def main():
    model = BASE / 'desktop_model/estructura_p1l4_desktop.json'
    catalog = model.parent / 'slab_load_surfaces.json'
    data = json.loads(model.read_text(encoding='utf-8'))
    key = hashlib.sha256(model.read_bytes()+catalog.read_bytes()+Path(lab.__file__).read_bytes()).hexdigest()
    weather = json.loads((BASE/'lrfd_results'/('units_'+key+'.json')).read_text())
    cvm = lab.load_engine()
    units = {name:lab.source_case(data,name,cvm) for name in ('G','Q','EX','EY')}
    units.update({name:weather[name] for name in ('Roof','WXp','WXm','WYp','WYm')})
    result = {'modelHash':hashlib.sha256(model.read_bytes()).hexdigest(),
              'roofArea':weather['roofArea'],
              'equilibriumError':max(weather[n]['equilibriumError'] for n in ('Roof','WXp','WXm','WYp','WYm')),
              'variants':[dict(name=name,u=0,label=name,forces=value['forces'],displacements=value['displacements']) for name,value in units.items()]}
    assert result['equilibriumError'] < .02
    for project in ('unity_visualizador','unity_visualizador_ios'):
        resources = BASE/project/'Assets/Resources'
        assert (resources/'estructura_p1l4_unity.json').read_bytes() == model.read_bytes()
        (resources/'MobileLrfdUnits.json').write_text(json.dumps(result,allow_nan=False),encoding='utf-8')
    cvm.ops.wipe()
    print('PASS: nueve unidades LRFD, equilibrio y hash del modelo verificados para ambas aplicaciones.')

if __name__ == '__main__':
    main()

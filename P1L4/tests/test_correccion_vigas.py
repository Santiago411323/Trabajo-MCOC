"""Regresión geométrica y de cargas sobre los dos edificios completos."""
import copy
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT / 'P1L3'), str(ROOT / 'P1L4')]
import carga_viva_sismo as cvm
from auditar_vigas import audit


def main():
    raw = cvm.load_json(cvm.JSON_PATH)
    corrected, report = cvm.corregir_conectividad(raw)
    fix = report['correccion_vigas']
    assert len(fix['duplicadas_eliminadas']) == 3
    assert len(fix['pares_unidos']) == 6
    result = audit(corrected)
    assert result['beam_count'] == 444
    assert not result['overlaps']
    assert not result['unjustified_joins']
    lookup = {cvm.element_tag(e): e for e in corrected['elements']}
    assert (lookup['E1_113']['nodeI'], lookup['E1_113']['nodeJ']) == (87, 92)
    assert lookup['E1_112.2']['nodeJ'] == lookup['E1_113']['nodeI']
    assert 'E1_132' in lookup  # mantiene la separación transversal
    for field in ('deadLoad', 'liveLoad', 'areaTributaria'):
        before = sum(float(e.get(field) or 0) for e in raw['elements'] if e['type'] == 'viga')
        after = sum(float(e.get(field) or 0) for e in corrected['elements'] if e['type'] == 'viga')
        assert abs(before - after) < 1e-7, field
    repeated = copy.deepcopy(corrected)
    cvm.corregir_vigas_revisadas(repeated)
    assert repeated == corrected
    for platform in ('unity_visualizador', 'unity_visualizador_ios'):
        exported = json.loads((ROOT / 'P1L4' / platform / 'Assets/Resources/estructura_p1l4_unity.json').read_text(encoding='utf-8'))
        after = audit(exported)
        assert not after['overlaps'] and not after['unjustified_joins']
        assert not after['uncertain_joins']
        assert [e for e in exported['elements'] if e['type'] == 'viga'] == [e for e in corrected['elements'] if e['type'] == 'viga']
    print('PASS: dos edificios; 444 vigas; sin solapes ni divisiones injustificadas; cargas conservadas; E1_112.2 separado; Android/iOS coinciden.')


if __name__ == '__main__':
    main()

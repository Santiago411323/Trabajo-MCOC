"""Actualiza solo metadatos de restricciones, sin ejecutar ni cambiar análisis.

Los grupos se leen del mismo build_model que llama rigidDiaphragm en OpenSees.
No se deducen de las losas visuales ni de diaphragmList (lista histórica).
"""
import argparse
import copy
import json
from pathlib import Path
from seismic.transient_analysis import load_engine

BASE = Path(__file__).resolve().parent

def groups_from_model(data, engine=None):
    engine = engine or load_engine()
    engine.build_model(data)
    return copy.deepcopy(engine.DIAPHRAGMS)

def update(path):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    groups = groups_from_model(data)
    data['p1l4']['analysisModel']['diafragmas'] = groups
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'{path.name}: {len(groups)} diafragmas reales, {sum(g["slaves"] for g in groups)} vínculos; resultados conservados.')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model', type=Path, action='append')
    args = parser.parse_args()
    for path in args.model or [BASE/'desktop_model/estructura_p1l4_desktop.json',
                              BASE/'unity_visualizador/Assets/Resources/estructura_p1l4_unity.json']:
        update(path)

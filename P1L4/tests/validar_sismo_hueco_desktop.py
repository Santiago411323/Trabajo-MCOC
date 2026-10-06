"""Valida los ocho casos del modelo de escritorio usando el contrato sísmico existente."""
import json
import sys
from pathlib import Path

root=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(root/'P1L4/seismic'))
import validate_transient as validation

validation.DEFAULT_MODEL=root/'P1L4/desktop_model/estructura_p1l4_desktop.json'
original_load=validation.load_response
folder=root/'P1L4/seismic/desktop_results'
reports={}
for direction in ('X','Y'):
    def desktop_load(path,identifier):
        meta, samples = original_load(folder,identifier.replace('_X_','_'+direction+'_'))
        # Compare in float64 to avoid rounding the subtraction and multiplication again.
        return meta, samples.astype(validation.np.float64)
    validation.load_response=desktop_load
    reports[direction]=validation.verify_scaling(account_for_float32=True)
output=root/'revisiones/validacion_sismo_hueco_desktop.json'
output.write_text(json.dumps(reports,indent=2)+'\n',encoding='utf-8')
print('PASS: eight complete responses, model/binary hashes, masses, gravity equilibrium, supports, intensity scaling and maximum displacement.')

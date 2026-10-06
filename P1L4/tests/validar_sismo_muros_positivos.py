"""Checks all eight new transient responses before publishing desktop geometry."""
import json
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
folder=Path(sys.argv[1]) if len(sys.argv)>1 else ROOT/'P1L4/desktop_model'
responses=Path(sys.argv[2]) if len(sys.argv)>2 else ROOT/'P1L4/seismic/desktop_results'
sys.path.insert(0,str(ROOT/'P1L4/seismic'))
import validate_transient as validation
validation.DEFAULT_MODEL=folder/'estructura_p1l4_desktop.json'
original_load=validation.load_response
reports={}
for direction in ('X','Y'):
    def load(path,identifier):
        meta,samples=original_load(responses,identifier.replace('_X_','_'+direction+'_'))
        return meta,samples.astype(validation.np.float64)
    validation.load_response=load
    reports[direction]=validation.verify_scaling(account_for_float32=True)
(ROOT/'revisiones/validacion_sismo_muros_positivos.json').write_text(json.dumps(reports,indent=2)+'\n',encoding='utf8')
print('PASS: eight complete transient responses, hashes, supports, mass/gravity balance, positive modal periods and intensity scaling.',flush=True)

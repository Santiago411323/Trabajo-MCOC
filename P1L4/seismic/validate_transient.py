"""Pruebas de datos y de OpenSees; jamás usa una animación como evidencia."""
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

from transient_analysis import BASE, DEFAULT_MODEL, INTENSITIES, dataset_id, load_engine, read_record, run

cvm = load_engine()
import numpy as np


def load_response(folder, identifier):
    path = Path(folder) / (identifier + ".json")
    meta = json.loads(path.read_text(encoding="utf-8"))
    binary = path.parent / meta["binaryFile"]
    content = binary.read_bytes()
    assert content[:8] == b"MCOCSIS1"
    assert struct.unpack("<iii", content[8:20]) == (len(meta["nodeTags"]),len(meta["members"]),meta["frameCount"])
    assert hashlib.sha256(content).hexdigest() == meta["binarySha256"]
    assert len(content) == 20 + 4*meta["frameCount"]*meta["stride"]
    samples = np.memmap(binary,dtype="<f4",offset=20,mode="r",shape=(meta["frameCount"],meta["stride"]))
    assert np.isfinite(samples).all()
    assert np.allclose(samples[:,0],np.arange(meta["frameCount"])*meta["dt"],atol=2e-6)
    return meta,samples


def verify_scaling():
    cases = [load_response(BASE/"results",dataset_id("el_centro_1940_ns","X",factor)) for factor in INTENSITIES]
    ref_meta, ref = cases[2]
    errors = []
    for factor,(meta,samples) in zip(INTENSITIES,cases):
        assert meta["modelHash"] == hashlib.sha256(DEFAULT_MODEL.read_bytes()).hexdigest()
        assert meta["frameCount"] == ref_meta["frameCount"] and not meta["truncated"]
        assert abs(meta["mass"]["conservationError_kN"]) < 1e-7
        assert meta["gravity"]["maximumError_kN"] < .01
        assert abs(meta["pgaApplied_m_s2"]-factor*meta["pgaOriginal_m_s2"]) < 1e-10
        assert all(t>0 and math.isfinite(t) for t in meta["modal"]["periods_s"])
        assert len({m["elementTag"] for m in meta["members"]}) == len(meta["members"])
        # Remove gravity for the scaling check, for nodes AND element actions.
        count = len(meta["nodeTags"])*3
        for begin,end,tolerance in [(1,1+count,2e-7),(1+count,meta["stride"],.005)]:
            expected = (ref[:,begin:end]-ref[0,begin:end])*factor
            actual = samples[:,begin:end]-samples[0,begin:end]
            err = float(np.max(np.abs(expected-actual)))
            assert err < tolerance,(factor,begin,err)
            errors.append(err)
        for nid in [s["node"] for s in json.loads(DEFAULT_MODEL.read_text(encoding="utf-8"))["supports"]]:
            n = meta["nodeTags"].index(nid)
            assert np.max(np.abs(samples[:,1+n*3:4+n*3])) < 1e-10
        displacement=samples[:,1:1+count].reshape(-1,len(meta["nodeTags"]),3)
        magnitudes=np.linalg.norm(displacement,axis=2)
        frame,node=np.unravel_index(np.argmax(magnitudes),magnitudes.shape)
        assert abs(float(magnitudes[frame,node])-meta["maximumDisplacement"]["value_m"])<2e-7
        assert meta["nodeTags"][node] == meta["maximumDisplacement"]["nodeTag"]
    return {"status":"PASS","cases":4,"framesPerCase":ref_meta["frameCount"],
            "maximumScalingErrors":errors,"periods_s":ref_meta["modal"]["periods_s"]}


def verify_short_runs():
    data=json.loads(DEFAULT_MODEL.read_text(encoding="utf-8"))
    model_hash=hashlib.sha256(DEFAULT_MODEL.read_bytes()).hexdigest()
    spec=json.loads((BASE/"records/catalog.json").read_text(encoding="utf-8"))["records"][0]
    output=BASE/"validation"
    output.mkdir(exist_ok=True)
    # Real record: half dt, same physical scenario.
    run(data,model_hash,spec,1.,"X",output,dt_factor=2,max_seconds=4.)
    fine_meta,fine=load_response(output,dataset_id(spec["id"],"X",1.))
    meta,coarse=load_response(BASE/"results",dataset_id(spec["id"],"X",1.))
    count=len(meta["nodeTags"])*3
    coarse_u=coarse[:201,1:1+count]-coarse[0,1:1+count]
    fine_u=fine[::2,1:1+count]-fine[0,1:1+count]
    displacement_error=float(np.max(np.abs(coarse_u-fine_u)))
    relative_error=displacement_error/float(np.max(np.abs(fine_u)))
    assert relative_error < .05,relative_error
    # Zero excitation: total response must stay at the gravity equilibrium.
    zero=output/"zero.at2"
    zero.write_text("VERIFICATION INPUT, NOT AN EARTHQUAKE\nNPTS= 51, DT= .02 SEC\n"+"0 "*51,encoding="utf-8")
    zero_spec={"id":"verification_zero","name":"Entrada nula de verificación","file":"../validation/zero.at2",
               "format":"AT2","units":"m/s2","source":"verification-only"}
    run(data,model_hash,zero_spec,1.,"X",output,max_seconds=1.)
    _,samples=load_response(output,dataset_id(zero_spec["id"],"X",1.))
    assert float(np.max(np.abs(samples[:,1:]-samples[0,1:]))) < .001
    return {"zeroExcitation":"PASS","stepSensitivity":"PASS","dt_s":meta["dt"],
            "refinedDt_s":fine_meta["dt"],"maxDisplacementDifference_m":displacement_error,
            "relativeDisplacementDifference":relative_error}


if __name__ == "__main__":
    report={"scalingAndContract":verify_scaling(),"shortRuns":verify_short_runs()}
    (BASE/"validation/report.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    print(json.dumps(report,indent=2))
    print("PASS: contrato, masas, equilibrio gravitacional, apoyos, intensidad real, entrada nula y sensibilidad temporal.")

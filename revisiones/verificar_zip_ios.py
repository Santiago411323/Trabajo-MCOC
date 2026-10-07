"""Comprueba el entregable Xcode sin extraerlo ni ejecutar su contenido."""
import hashlib
import json
import plistlib
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parents[1]
project = root / "P1L4/unity_visualizador_ios"
archive_path = max((project / "Entregables").glob("*.zip"), key=lambda p: p.stat().st_mtime)
resources = project / "Assets/Resources"
payloads = list((resources / "MobileSeismic").glob("*_payload.bytes"))
metadata = list((resources / "MobileSeismic").glob("*.json"))
assert len(payloads) == len(metadata) == 8
patterns = {p.name: p.read_bytes() for p in payloads + metadata}
patterns["estructura_p1l4_unity.json"] = (resources / "estructura_p1l4_unity.json").read_bytes()
patterns["MobileLrfdUnits.json"] = (resources / "MobileLrfdUnits.json").read_bytes()
found = {}
with zipfile.ZipFile(archive_path) as archive:
    assert archive.testzip() is None, "CRC invalido"
    names = archive.namelist()
    assert any(n.endswith("Unity-iPhone.xcodeproj/project.pbxproj") for n in names)
    plist_name = next(n for n in names if n.endswith("/Info.plist") and n.count("/") == 1)
    plist = plistlib.loads(archive.read(plist_name))
    assert plist["CFBundleVersion"] == "17"
    assert plist.get("NSCameraUsageDescription"), "Falta descripcion de permiso de camara"
    scripts = [entry for entry in archive.infolist() if entry.filename.endswith(".sh")]
    assert scripts and all((entry.external_attr >> 16) & 0o111 for entry in scripts)
    for entry in archive.infolist():
        if "/Data/" not in entry.filename or not entry.filename.endswith((".assets", ".resS", ".resource")):
            continue
        content = archive.read(entry)
        for name, pattern in patterns.items():
            if name not in found and pattern in content:
                found[name] = entry.filename
        del content
    assert set(found) == set(patterns), f"Faltan recursos: {set(patterns) - set(found)}"
report = {
    "status": "PASS", "zip": str(archive_path), "build": "17",
    "size_mib": round(archive_path.stat().st_size / 2**20, 1),
    "sha256": hashlib.file_digest(archive_path.open("rb"), "sha256").hexdigest(),
    "checks": ["CRC completo", "proyecto Xcode", "permiso de camara", "permisos ejecutables", "modelo exacto", "nueve unidades LRFD exactas", "ocho metadatos y ocho binarios exactos"],
    "resources": found,
}
output = root / "revisiones/validacion_zip_ios.json"
output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({k: v for k, v in report.items() if k != "resources"}, ensure_ascii=False, indent=2))

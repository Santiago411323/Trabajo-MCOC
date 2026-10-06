from pathlib import Path
import shutil

root = Path(__file__).resolve().parents[1]
android = root / 'P1L4/unity_visualizador'
ios = root / 'P1L4/unity_visualizador_ios'
for name in ['VisualSiteTerrain', 'VisualCampusSite', 'VisualStairs', 'VisualSalmonRailings', 'VisualInteriorPartitions', 'VisualStudyRoom', 'VisualFrameFacade']:
    for suffix in ['.cs', '.cs.meta']:
        shutil.copy2(android / f'Assets/Scripts/{name}{suffix}', ios / f'Assets/Scripts/{name}{suffix}')

def edit(path, old, new):
    text = path.read_text(encoding='utf-8-sig')
    assert text.count(old) == 1, (path, old, text.count(old))
    path.write_text(text.replace(old, new), encoding='utf-8')

edit(ios / 'Assets/Scripts/StructureViewer.cs',
     'room.AddComponent<VisualStudyRoom>().Build(loadedData);',
     'room.AddComponent<VisualStudyRoom>().Build(loadedData);\n            var partitions=new GameObject("Muros_interiores_ambiente_Y4");\n            partitions.transform.SetParent(transform,false);\n            partitions.AddComponent<VisualInteriorPartitions>().Build(loadedData,room.transform);\n            visualFacadeObjects.Add(partitions);RegisterFloor(partitions,"CIELO_2");')
edit(ios / 'Assets/Scripts/StructureViewer.cs',
     'stairs.BuildTerraceAccess(visualTerrain.GetComponent<VisualSiteTerrain>(),(piece,floor)=>RegisterFloor(piece,floor));',
     'stairs.BuildTerraceAccess(visualTerrain.GetComponent<VisualSiteTerrain>(),(piece,floor)=>RegisterFloor(piece,floor));\n            stairs.BuildRearAccess(loadedData,visualTerrain.GetComponent<VisualSiteTerrain>(),(piece,floor)=>RegisterFloor(piece,floor));')
for project in [android, ios]:
    controller = project / 'Assets/Scripts/StructuralVRController.cs'
    edit(controller,
         'roots.AddRange(world.GetComponentsInChildren<VisualStudyRoom>(true).Select(c=>c.transform));',
         'roots.AddRange(world.GetComponentsInChildren<VisualStudyRoom>(true).Select(c=>c.transform));\n        roots.AddRange(world.GetComponentsInChildren<VisualInteriorPartitions>(true).Select(c=>c.transform));')
    edit(controller,
         'foreach(var room in world.GetComponentsInChildren<VisualStudyRoom>(true))room.gameObject.SetActive(environment);',
         'foreach(var room in world.GetComponentsInChildren<VisualStudyRoom>(true))room.gameObject.SetActive(environment);\n        foreach(var partitions in world.GetComponentsInChildren<VisualInteriorPartitions>(true))partitions.gameObject.SetActive(environment);')
    edit(project / 'Assets/Scripts/StructuralVRMobileUpdates.cs',
         'r.GetComponentInParent<VisualStudyRoom>()!=null;',
         'r.GetComponentInParent<VisualStudyRoom>()!=null || r.GetComponentInParent<VisualInteriorPartitions>()!=null;')
    validation = project / 'Assets/Scripts/Editor/StructuralVRValidation.cs'
    text = validation.read_text(encoding='utf-8-sig')
    import re
    text, count = re.subn(r'FlightCount\s*==\s*4', 'FlightCount==5', text)
    assert count == 1
    validation.write_text(text, encoding='utf-8')
    edit(project / 'Assets/Scripts/Editor/MobileApplicationValidation.cs',
         'Require(room!=null && room.Tables.Count==4,"Updated study room missing in VR");',
         'Require(room!=null && room.Tables.Count==4,"Updated study room missing in VR");\n                MobileEnvironmentRevisionValidation.Validate(world);')
    edit(project / 'Assets/Scripts/Editor/MobileApplicationValidation.cs',
         'Require(!room.gameObject.activeInHierarchy,"Study room environment toggle failed");',
         'Require(!room.gameObject.activeInHierarchy,"Study room environment toggle failed");\n                Require(!world.GetComponentInChildren<VisualInteriorPartitions>(true).gameObject.activeInHierarchy,"Partitions environment toggle failed");')
    edit(project / 'Assets/Scripts/Editor/MobileApplicationValidation.cs',
         'Call(vr,"ToggleEnvironment");Require(world.GetComponentInChildren<VisualCafe>().gameObject.activeInHierarchy,"Cafe restore failed");',
         'Call(vr,"ToggleEnvironment");Require(world.GetComponentInChildren<VisualCafe>().gameObject.activeInHierarchy,"Cafe restore failed");\n                Require(world.GetComponentInChildren<VisualInteriorPartitions>().gameObject.activeInHierarchy,"Partitions restore failed");')
    shutil.copy2(root / 'P1L4/desktop_model/estructura_p1l4_desktop.json', project / 'Assets/Resources/estructura_p1l4_unity.json')
    shutil.copy2(root / 'P1L4/desktop_model/slab_load_surfaces.json', project / 'Assets/Resources/slab_load_surfaces.json')

edit(ios / 'Assets/Scripts/Editor/IOSBuild.cs', 'PlayerSettings.iOS.buildNumber = "15";', 'PlayerSettings.iOS.buildNumber = "16";')
verifier = root / 'revisiones/verificar_zip_ios.py'
text = verifier.read_text(encoding='utf-8').replace('== "15"', '== "16"').replace('"build": "15"', '"build": "16"')
verifier.write_text(text, encoding='utf-8')
print('Updated shared environment, VR integration, model/catalog, mobile validation and iOS build 16.')

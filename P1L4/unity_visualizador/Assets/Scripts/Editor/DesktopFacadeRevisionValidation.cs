using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class DesktopFacadeRevisionValidation
{
    public static void ValidateBatch()
    {
        GameObject model=null,cameraObject=null;
        try
        {
            model=new GameObject("Facade revision validation");
            var viewer=model.AddComponent<StructureViewer>();
            var facade=model.GetComponentInChildren<VisualFrameFacade>();
            Require(!facade.Panels.Any(p=>Pair(p,"E1_243","E1_255")),"Old cafeteria partition window remains");
            foreach(var tags in new[]{new[]{"E1_241","E1_254"},new[]{"E1_254","E1_255"},new[]{"E1_241","E1_243"}})
            {
                var panel=facade.Panels.Single(p=>Pair(p,tags[0],tags[1]));
                Require(panel.transform.Find("Vidrio")!=null && panel.transform.Find("Montante")!=null,"New window does not match facade style");
                Require(panel.GetComponentsInChildren<Collider>().Length==0,"Decorative windows contain colliders");
            }
            var cafe=model.GetComponentInChildren<VisualCafe>();
            var roof=cafe.transform.Find("Cielo_cafeteria_voladizo").GetComponent<Renderer>().bounds;
            var slab=UnityData.Structure.slabs.Single(s=>s.id=="L95");
            Require(Mathf.Abs(roof.min.x-slab.x0)<.001f && Mathf.Abs(roof.max.x-slab.x1)<.001f &&
                Mathf.Abs(roof.min.z-slab.y0)<.001f && Mathf.Abs(roof.max.z-slab.y1)<.001f,"Roof extends beyond real cantilever footprint");
            Require(cafe.transform.Find("Cielo_cafeteria").GetComponent<Renderer>().bounds.min.z>=slab.y1-.001f,"Inside roof still overhangs outside the building");
            var doors=facade.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Paso_puerta_acceso_")).ToArray();
            Require(doors.Length==2 && facade.AccessDoorCount==2 && facade.CafeDoorCount==2,"Original access doors or cafe doors missing");
            foreach(var door in doors)
            {
                Require(door.position.y>5.5f && door.position.y<5.8f,"Door must belong to Y4 story");
                if(door.name.Contains("E1_297"))Require(Mathf.Abs(door.position.x-35)<.001f && door.position.z>.35f && door.position.z<1.3f,"Door must be beside E1_297 towards positive Z");
                else Require(Mathf.Abs(door.position.x-32.5f)<.001f && Mathf.Abs(door.position.z+7.25f)<.001f,"Door not between E1_281 and E1_293");
                foreach(Transform piece in door.parent)
                {
                    if(piece.GetComponent<Renderer>()==null)continue;
                    var p=piece.localPosition;var size=piece.localScale;var center=door.localPosition;
                    Require(!(p.x-size.x/2<center.x+.65f-.001f && p.x+size.x/2>center.x-.65f+.001f &&
                        p.y-size.y/2<center.y+1.1f-.001f && p.y+size.y/2>center.y-1.1f+.001f),"Glass or frame blocks access opening");
                }
            }
            cameraObject=new GameObject("Access test camera");var camera=cameraObject.AddComponent<Camera>();
            camera.transform.position=new Vector3(60,25,-40);
            var walk=model.AddComponent<DesktopWalkthrough>();walk.Configure(viewer,UnityData.Structure);walk.Enter(camera);
            foreach(var door in doors)
            {
                var feet=door.position-Vector3.up*1.1f+Vector3.up*.01f;
                Require(!Physics.CheckCapsule(feet+Vector3.up*.28f,feet+Vector3.up*1.47f,.25f,1<<2),"Standing character cannot pass access door");
            }
            walk.Exit();
            Debug.Log("[Facade revision] PASS: old window removed; three cantilever windows match facade style; cafe roof matches L95; two unobstructed Y4 access doors and original cafe doors preserved; game collision openings clear.");
            UnityEngine.Object.DestroyImmediate(model);UnityEngine.Object.DestroyImmediate(cameraObject);
            DesktopCafeValidation.ValidateBatch();
        }
        catch(Exception error)
        {
            Debug.LogException(error);
            if(model!=null)UnityEngine.Object.DestroyImmediate(model);
            if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);
            EditorApplication.Exit(1);
        }
    }
    private static bool Pair(GameObject panel,string a,string b)=>panel.name.Contains(a+"_") && panel.name.Contains(b+"_");
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}

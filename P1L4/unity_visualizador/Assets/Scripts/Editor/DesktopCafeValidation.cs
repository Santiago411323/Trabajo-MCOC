using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class DesktopCafeValidation
{
    public static void ValidateBatch()
    {
        GameObject model=null;
        try
        {
            model=new GameObject("Desktop cafe validation");
            model.AddComponent<StructureViewer>();
            var cafe=model.GetComponentInChildren<VisualCafe>();
            var terrain=model.GetComponentInChildren<VisualSiteTerrain>();
            var floor=cafe.transform.Find("Piso_cafeteria_nivel_4");
            Require(Mathf.Abs(floor.localPosition.y+floor.localScale.y/2)<.001f,"Cafe floor must be Y0");
            Require(Mathf.Abs(cafe.transform.Find("Cielo_cafeteria").localPosition.y-3.39f)<.001f,"Cafe ceiling must follow floor");
            Require(cafe.transform.Find("Plano_conexion_escalera_existente")==null,"Elevated cafe access remains");
            var ground=terrain.BaseGround.transform.Find("Pasto_nivel_0").GetComponent<MeshFilter>().sharedMesh.bounds;
            var bounds=cafe.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(floor.position,Vector3.zero),(b,r)=>{b.Encapsulate(r.bounds);return b;});
            Require(ground.min.x<=bounds.min.x && ground.max.x>=bounds.max.x && ground.min.z<=bounds.min.z && ground.max.z>=bounds.max.z,"Y0 ground must cover cafe footprint");
            Require(terrain.UpperTerrace!=null && terrain.UpperTerrace.transform.Find("Pasto_terraza_Y4")!=null,"Y4 grass terrace must be restored");
            var campus=model.GetComponentInChildren<VisualCampusSite>();
            string[] paths={"Camino_paralelo_arboles","Camino_bajo_voladizo_Y4","Camino_union_estacionamiento"};
            foreach(string path in paths)
                Require(campus.transform.Find(path)!=null && campus.transform.Find(path).GetComponentsInChildren<MeshFilter>().All(m=>m.sharedMesh.vertexCount>0),"Missing path: "+path);
            var under=campus.transform.Find(paths[1]).GetComponentsInChildren<MeshFilter>().First(m=>m.name.StartsWith("Pavimento"));
            Require(under.sharedMesh.vertices.Skip(under.sharedMesh.vertexCount-2).All(v=>Mathf.Abs(v.y-4.06f)<.001f),"Path must arrive at Y4 terrace");
            var first=campus.transform.Find(paths[0]).GetComponentsInChildren<MeshFilter>().First(m=>m.name.StartsWith("Pavimento"));
            var junction=campus.transform.Find(paths[2]).GetComponentsInChildren<MeshFilter>().First(m=>m.name=="Pavimento_0");
            Require(Vector3.Distance(End(first),Start(under))<.001f && Vector3.Distance(End(under),Start(junction))<.001f,"Disconnected pedestrian paths");
            Require(Mathf.Abs(Start(first).y-.06f)<.001f && Mathf.Abs(End(first).y-4.06f)<.001f,"Promenade must rise from Y0 to Y4");
            var ramp=campus.transform.Find("Pendiente_paseo_y_arboles").GetComponent<MeshFilter>().sharedMesh.bounds;
            var trees=campus.GetComponentsInChildren<Transform>().Where(t=>t.name=="Tronco" && Mathf.Abs(t.localPosition.x-(Start(first).x-4f))<.01f && t.localPosition.z>=ramp.min.z).ToArray();
            Require(trees.Length==12,"Tree row must remain in front of promenade");
            foreach(var tree in trees)
                Require(Mathf.Abs(tree.localPosition.y-1.6f-4f*(1f-Mathf.InverseLerp(ramp.min.z,ramp.max.z,tree.localPosition.z)))<.001f,"Tree not grounded on ramp");
            var keepout=(Vector4)typeof(VisualCampusSite).GetField("hillKeepout",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(campus);
            var hills=campus.GetComponentsInChildren<MeshFilter>().Where(m=>m.name.StartsWith("Cerro_panorama_")).ToArray();
            Require(hills.Length==14,"Hills must remain present");
            foreach(var hill in hills)
            {
                var vertices=hill.sharedMesh.vertices;var triangles=hill.sharedMesh.triangles;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var c=(vertices[triangles[i]]+vertices[triangles[i+1]]+vertices[triangles[i+2]])/3;
                    Require(c.x<keepout.x || c.x>keepout.y || c.z<keepout.z || c.z>keepout.w,"Hill overlaps protected campus");
                }
            }
            VisualCafe.TryLayout(UnityData.Structure,out var cafeLayout);
            var cafeTables=cafe.GetComponentsInChildren<Transform>().Where(t=>t.name=="Mesa").ToArray();
            Require(cafeTables.Length==29,"Cafe table count changed");
            foreach(var table in cafeTables)
                if(table.position.z<cafeLayout.Rear)Require(Mathf.Abs(table.position.x-(cafeLayout.Left+5))>=2.04f,"Cafe table blocks circulation towards doors");
            for(int i=0;i<cafeTables.Length;i++)for(int j=i+1;j<cafeTables.Length;j++)
                Require(Mathf.Abs(cafeTables[i].position.x-cafeTables[j].position.x)>=2.59f || Mathf.Abs(cafeTables[i].position.z-cafeTables[j].position.z)>=2.59f,"Cafe table chairs overlap");
            Debug.Log("[Cafe tables] PASS: 29 tables preserved; chairs separated; corridor towards cafe doors kept clear.");
            var service=cafe.transform.Find("Muro_cafeteria_E1_256_E1_264_cerrado");
            Require(service!=null && service.localPosition.x<9.6f && service.localPosition.x>9.5f && service.localScale.z>15f,"Service wall not between structural columns");
            var realColumn=model.GetComponentsInChildren<ElementSelectable>().Single(e=>e.data!=null && e.data.elementTag=="E1_260");
            Require(realColumn.data.type=="columna" && service.localPosition.x+service.localScale.x/2<10f-realColumn.data.width_m/2,"Wall must cover the real intermediate column without removing it");
            Require(cafe.transform.Find("Muro_fondo_cafe")==null && cafe.transform.Find("Dintel_paso_cafe")==null,"Old wall/opening remains");
            string[] equipment={"Barra_servicio","Meson","Maquina_espresso","Molinillo","Refrigerador","Rotulo_cafeteria"};
            foreach(string name in equipment)
                Require(cafe.transform.Find(name).localPosition.x>8f && cafe.transform.Find(name).localPosition.x<10f,"Equipment not relocated: "+name);
            var facade=model.GetComponentInChildren<VisualFrameFacade>();
            Require(facade.CafeDoorCount==2,"Cafe needs exactly one doorway on each facade: "+facade.CafeDoorCount);
            var doors=facade.GetComponentsInChildren<Transform>().Where(t=>t.name=="Paso_puerta_cafeteria").ToArray();
            Require(doors.Select(t=>Mathf.Round(t.position.z*100f)).Distinct().Count()==2,"Doorways are not on opposite facades");
            foreach(var door in doors)
            {
                var center=door.localPosition;
                foreach(Transform piece in door.parent)
                {
                    if(piece.GetComponent<Renderer>()==null)continue;
                    Vector3 size=piece.localScale,p=piece.localPosition;
                    Require(!(p.x-size.x/2<center.x+.65f-.001f && p.x+size.x/2>center.x-.65f+.001f && p.y-size.y/2<center.y+1.1f-.001f && p.y+size.y/2>center.y-1.1f+.001f),"Glass/frame blocks doorway");
                }
            }
            Debug.Log("[Cafe service] PASS: closed wall between E1_256/E1_264; equipment/sign moved; old opening cleared; two unobstructed visual doorways near tables; structural columns preserved.");
            var structural=UnityData.Structure;
            var nodeMap=structural.nodes.ToDictionary(n=>n.id);
            foreach(int offset in Enumerable.Range(0,5))
            {
                var left=structural.walls.Single(w=>w.id==41+offset);var right=structural.walls.Single(w=>w.id==51+offset);
                Require(Mathf.Abs(nodeMap[left.nodeI].y+4.045f)<.001f && Mathf.Abs(nodeMap[right.nodeJ].y-8.9f)<.001f,"Wall outer connection moved");
                Require(Mathf.Abs(nodeMap[left.nodeJ].y+.35f)<.001f && Mathf.Abs(nodeMap[right.nodeI].y-2.8f)<.001f,"Left wall must stop at column face; right wall must remain in place");
                Require(Mathf.Abs(nodeMap[right.nodeI].y-.35f-2.45f)<.001f,"Clear opening beside the column changed");
                var rendered=model.GetComponentsInChildren<ElementSelectable>().Single(e=>e.isWall && e.wallId==41+offset);
                Require(Mathf.Abs(rendered.GetComponent<Renderer>().bounds.max.z+.35f)<.001f,"Rendered wall crosses the column face");
            }
            Require(structural.elements.Any(e=>e.elementTag=="E1_233"),"Column E1_233 removed");
            Debug.Log("[Wall opening] PASS: five stories including basement terminate at column face Z=-0.35; clear opening 2.45 m, outer endpoints preserved, base supports and E1_233 preserved.");
            Require(VisualCafe.FloorFilter=="CIELO_1S","Wrong cafe floor filter");
            var stairs=model.GetComponentInChildren<VisualStairs>();
            var rails=model.GetComponentInChildren<VisualSalmonRailings>();
            Require(stairs.FlightCount==4,"Expected two existing flights and two terrace access strips");
            Require(rails!=null && rails.BeamTags.OrderBy(t=>t).SequenceEqual(new[]{"E1_206","E1_208","E1_221.1","E1_221.2"}),"Missing beam rails");
            Require(rails.Routes.Count==11 && rails.GetComponentsInChildren<MeshFilter>().Any(m=>m.name.Contains("panel_cerrado")),"Expected solid rails with only outer stair sides and no middle divider");
            var extension=terrain.CantileverExtension;
            Require(Mathf.Abs(extension.min.x-30f)<.001f && Mathf.Abs(extension.max.z+7.25f)<.001f,"Terrace extension must end at E1_280 on cantilever side only");
            var normal=stairs.Paths.Single(p=>p.Name=="Escalera_Y4_cafeteria_normal");
            var wide=stairs.Paths.Single(p=>p.Name=="Gradas_Y4_cafeteria_rectas");
            Require(normal.Start.x>normal.End.x && Mathf.Abs(normal.Start.y-4.03f)<.001f && Mathf.Abs(normal.End.y-.03f)<.001f,"Terrace stair must descend towards cafe");
            Require(Mathf.Abs(normal.Start.z-normal.Width/2-wide.Start.z-wide.Width/2)<.001f && wide.Width>normal.Width*3,"Normal stairs and wider straight seating must be adjacent");
            Require(stairs.Pieces.All(p=>p.GetComponent<Collider>()==null && p.GetComponent<ElementSelectable>()==null),"Decorations must not be structural/selectable/collidable");
            var viewer=model.GetComponent<StructureViewer>();
            var refresh=typeof(StructureViewer).GetMethod("RefreshVisibility",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            viewer.showVisualStairs=false;refresh.Invoke(viewer,null);
            Require(stairs.Pieces.All(p=>!p.activeInHierarchy),"Stairs toggle must hide rails and both new access strips");
            viewer.showVisualStairs=true;refresh.Invoke(viewer,null);
            terrain.SetVisibility(true,false);
            Require(!terrain.UpperTerrace.transform.Find("Pasto_ampliacion_Y4_E1_280").gameObject.activeInHierarchy,"Terrace toggle must hide extension");
            terrain.SetVisibility(true,true);
            Debug.Log("[Terrace stairs and rails] PASS: four beam rails; existing stair rails connected; Y4 extension to E1_280 only on cantilever side; adjacent normal stairs and straight seating descend to Y0; visual pieces only.");
            Debug.Log("[Desktop cafe] PASS: Y0 cafe and ceiling; full Y0 ground coverage; Y4 grass restored; promenade behind trees rises Y0 to Y4; trees follow ramp; hills preserved clear of campus; paths connect to parking.");
            Capture(model);
            CaptureService(model);
            CaptureTerrace(model);
            UnityEngine.Object.DestroyImmediate(model);model=null;
            EditorApplication.Exit(0);
        }
        catch(Exception error)
        {
            Debug.LogException(error);
            if(model!=null)UnityEngine.Object.DestroyImmediate(model);
            EditorApplication.Exit(1);
        }
    }
    private static Vector3 Start(MeshFilter mesh)=> (mesh.sharedMesh.vertices[0]+mesh.sharedMesh.vertices[1])*.5f;
    private static Vector3 End(MeshFilter mesh)=> (mesh.sharedMesh.vertices[mesh.sharedMesh.vertexCount-2]+mesh.sharedMesh.vertices[mesh.sharedMesh.vertexCount-1])*.5f;
    private static void Capture(GameObject model)
    {
        var cameraObject=new GameObject("Campus preview camera");var lightObject=new GameObject("Campus preview light");
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(50,-35,0);
        var camera=cameraObject.AddComponent<Camera>();camera.transform.position=new Vector3(92,48,85);camera.transform.LookAt(new Vector3(22,0,27));camera.farClipPlane=1000;
        var target=new RenderTexture(1280,800,24);camera.targetTexture=target;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(1280,800,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,1280,800),0,0);texture.Apply();
        System.IO.File.WriteAllBytes("Logs/desktop-campus-path-preview.png",texture.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lightObject);
    }
    private static void CaptureService(GameObject model)
    {
        var go=new GameObject("Cafe service preview");var lightObject=new GameObject("Cafe service light");
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(35,-55,0);
        var camera=go.AddComponent<Camera>();camera.transform.position=new Vector3(0,2.5f,1);camera.transform.LookAt(new Vector3(10,1.6f,.825f));camera.farClipPlane=150;
        var target=new RenderTexture(1280,800,24);camera.targetTexture=target;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1280,800,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();System.IO.File.WriteAllBytes("Logs/desktop-cafe-service-preview.png",image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(lightObject);
    }
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void CaptureTerrace(GameObject model)
    {
        var go=new GameObject("Terrace stairs preview");var lighting=new GameObject("Terrace light");
        var light=lighting.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;lighting.transform.rotation=Quaternion.Euler(45,-35,0);
        var camera=go.AddComponent<Camera>();camera.transform.position=new Vector3(-8,28,-45);camera.transform.LookAt(new Vector3(20,5,-10));camera.farClipPlane=250;
        var target=new RenderTexture(1440,900,24);camera.targetTexture=target;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1440,900,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1440,900),0,0);image.Apply();System.IO.File.WriteAllBytes("Logs/desktop-terrace-stairs-preview.png",image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(lighting);
    }
}

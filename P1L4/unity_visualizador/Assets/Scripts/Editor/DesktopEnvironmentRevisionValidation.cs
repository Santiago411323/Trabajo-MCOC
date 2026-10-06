using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class DesktopEnvironmentRevisionValidation
{
    public static void ValidateBatch()
    {
        GameObject model=null,camObject=null;
        try
        {
            model=new GameObject("Environment revision validation");var viewer=model.AddComponent<StructureViewer>();
            var data=UnityData.Structure;var nodes=data.nodes.ToDictionary(n=>n.id,n=>new Vector3(n.x,n.z,n.y));
            var terrain=model.GetComponentInChildren<VisualSiteTerrain>();
            var surfaces=terrain.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Pasto_")).ToArray();
            Require(surfaces.Length>=4 && surfaces.All(r=>r.sharedMaterial.name=="Cemento_plataformas_visuales"),"Platforms must all use concrete");
            var campus=model.GetComponentInChildren<VisualCampusSite>();
            var grade=typeof(VisualCampusSite).GetMethod("Grade",BindingFlags.Instance|BindingFlags.NonPublic);
            float front=data.nodes.Where(n=>data.supports.Any(s=>s.node==n.id)).Min(n=>n.y);
            float previous=(float)grade.Invoke(campus,new object[]{-55f,front-14.8f}),maxSlope=0;
            for(float x=-54.75f;x<=62f;x+=.25f)
            {float y=(float)grade.Invoke(campus,new object[]{x,front-14.8f});maxSlope=Mathf.Max(maxSlope,Mathf.Abs(y-previous)/.25f);previous=y;}
            Require(maxSlope<Mathf.Tan(40*Mathf.Deg2Rad),"Parking/sidewalk still have steep level breaks: slope="+maxSlope);
            var sidewalks=campus.GetComponentsInChildren<MeshFilter>().Where(m=>m.name=="Vereda_en_pendiente").OrderBy(m=>m.sharedMesh.bounds.min.x).ToArray();
            Require(sidewalks.Length==1,"Sidewalk must be one continuous mesh");
            var parking=campus.GetComponentsInChildren<MeshFilter>().Where(m=>m.name=="Estacionamiento_en_pendiente").OrderBy(m=>m.sharedMesh.bounds.min.x).ToArray();
            var leftEdge=parking[0].sharedMesh.vertices.Where(v=>Mathf.Abs(v.x-parking[0].sharedMesh.bounds.max.x)<.001f).OrderBy(v=>v.z).ToArray();
            var rightEdge=parking[1].sharedMesh.vertices.Where(v=>Mathf.Abs(v.x-parking[1].sharedMesh.bounds.min.x)<.001f).OrderBy(v=>v.z).ToArray();
            Require(leftEdge.Length==rightEdge.Length && leftEdge.Zip(rightEdge,(a,b)=>Vector3.Distance(a,b)).All(d=>d<.001f),"Sidewalk mesh edges are disconnected");
            var partitions=model.GetComponentInChildren<VisualInteriorPartitions>();
            Require(partitions!=null && partitions.Pieces.Count>=7,"Missing Y4 partitions");
            Require(partitions.Pieces.All(p=>p.GetComponent<ElementSelectable>()==null && p.GetComponent<Collider>()==null && p.GetComponent<Renderer>().bounds.min.y>4 && p.GetComponent<Renderer>().bounds.max.y<8),"Partitions affect structural IDs, original collisions or other stories");
            var connection=partitions.Pieces.Where(p=>p.name.StartsWith("Tabique_visual_unido_muro53_")).Select(p=>p.GetComponent<Renderer>().bounds).ToArray();
            var endColumn=data.elements.Single(e=>e.elementTag=="E1_297");
            Require(connection.Length>=2 && connection.All(b=>Mathf.Abs(b.center.z-2.8f)<.001f) && Math.Abs(connection.Max(b=>b.max.x)-nodes[endColumn.nodeI].x)<.001f,"Wall53 partition does not reach the end facade window");
            Require(!partitions.Pieces.Any(p=>p.name=="Tabique_visual_central_E1_285_E1_297"),"Unwanted partition between E1_285 and E1_297 remains");
            var wall=data.walls.Single(w=>w.id==53);
            Require(Mathf.Abs(connection.Min(b=>b.min.x)-(nodes[wall.nodeI].x+wall.grosor/2))<.001f,"Partition does not meet wall53 face");
            Require(connection.All(b=>partitions.CoreOpenings.All(g=>b.max.x<=g.x+.001f || b.min.x>=g.y-.001f)),"Core access to walls28/23/18 blocked");
            var room=model.GetComponentInChildren<VisualStudyRoom>();
            var central=partitions.Pieces.Where(p=>p.name.StartsWith("Tabique_visual_central_")).Select(p=>p.GetComponent<Renderer>().bounds).ToArray();
            Require(central.Length>=4 && central.All(b=>Mathf.Abs(b.center.z)<.001f),"Missing central partitions");
            Require(central.All(b=>partitions.CentralCoreOpenings.All(g=>b.max.x<=g.x+.001f || b.min.x>=g.y-.001f)),"Visual partition still blocks access near walls8/3/13");
            foreach(string tag in new[]{"E1_41","E1_33","E1_31","E1_28"})
            {
                var beam=data.elements.Single(e=>e.elementTag==tag);var p=nodes[beam.nodeI];var q=nodes[beam.nodeJ];
                var bounds=partitions.Pieces.Single(o=>o.name=="Tabique_visual_sobre_"+tag).GetComponent<Renderer>().bounds;
                Require(Mathf.Abs(bounds.center.x-p.x)<.001f && Mathf.Abs((Mathf.Min(p.z,q.z)<0?bounds.min.z:bounds.max.z)-(Mathf.Min(p.z,q.z)<0?Mathf.Min(p.z,q.z):Mathf.Max(p.z,q.z)))<.001f &&
                    Mathf.Abs((Mathf.Min(p.z,q.z)<0?bounds.max.z:bounds.min.z)-(Mathf.Min(p.z,q.z)<0?.06f:2.74f))<.001f,"Room divider must meet facade and perpendicular partition: "+tag);
            }
            Require(partitions.Tables.Count==10 && partitions.DoorCenters.Count==5 && partitions.GetComponentsInChildren<Transform>().Count(t=>t.name=="Silla_habitacion")==28,"Incorrect room furniture/doors after clearing core18/23/28");
            Require(!partitions.Tables.Any(t=>t.name.StartsWith("Mesa_habitacion_1_")) && !partitions.DoorCenters.Any(d=>d.x<0),"Furniture/door remains near walls18/23/28");
            Require(partitions.Furniture.All(o=>o.GetComponent<ElementSelectable>()==null && o.GetComponent<Collider>()==null),"Furniture affects structural IDs or permanent collisions");
            var obstacles=model.GetComponentsInChildren<ElementSelectable>().Where(e=>e.isWall || e.data!=null && e.data.type=="columna")
                .Select(e=>e.GetComponent<Renderer>()).Where(r=>r!=null).Select(r=>r.bounds)
                .Concat(partitions.Pieces.Select(p=>p.GetComponent<Renderer>().bounds)).ToArray();
            foreach(var piece in partitions.Furniture.Where(p=>p.name.Contains("mesa_habitacion") || p.name.Contains("silla_habitacion")))
                Require(obstacles.All(b=>!b.Intersects(piece.GetComponent<Renderer>().bounds)),"Furniture intersects a column or wall: "+piece.transform.parent.name+" "+piece.transform.position);
            foreach(var existing in room.GetComponentsInChildren<Renderer>().Where(r=>Mathf.Abs(r.bounds.center.z)<.08f && (r.name.StartsWith("Muro_visual") || r.name.StartsWith("Dintel"))))
                Require(central.All(b=>b.max.x<=existing.bounds.min.x+.001f || b.min.x>=existing.bounds.max.x-.001f),"Duplicate partition over study room/door");
            var stairs=model.GetComponentInChildren<VisualStairs>();var rear=stairs.Paths.Single(p=>p.Name=="Escalera_lateral_E1_288_Y4");
            Require(VisualCafe.TryLayout(data,out var cafeLayout),"Missing cafe patio layout");
            var rearColumn=data.elements.Single(e=>e.elementTag=="E1_288");
            Require(rear.End.x<rear.Start.x-5 && Mathf.Abs(rear.Start.z-rear.End.z)<.001f && rear.Start.z-rear.Width/2>=cafeLayout.Rear+1.99f &&
                Mathf.Abs(rear.Start.y-4.03f)<.001f && Mathf.Abs(rear.End.y+terrain.Clearance-.03f)<.001f &&
                rear.Start.x>terrain.TerraceContactX+.3f && Mathf.Abs(rear.End.x-nodes[rearColumn.nodeI].x)<.001f && Mathf.Abs(rear.Width-3)<.001f,
                "Stairs must be 3m wide, separated from facade and end at E1_288 X");
            Require(stairs.Pieces.Where(p=>p.name.StartsWith(rear.Name)).All(p=>Mathf.Abs(p.GetComponent<Renderer>().bounds.min.y+terrain.Clearance+.05f)<.001f),
                "Void remains beneath stairs or landings");
            Require(room.transform.Find("Puerta_sala_computadores_abierta")!=null,"Computer room still lacks visible door");
            var stoneWall=terrain.UpperTerrace.transform.Find("Muro_piedra_vertical_detras_escalera");
            Require(stoneWall!=null && stoneWall.GetComponentsInChildren<Collider>().Length==0 && stoneWall.GetComponentsInChildren<ElementSelectable>().Length==0,"Missing visual-only stone wall behind stairs");
            var stoneRenderers=stoneWall.GetComponentsInChildren<Renderer>();var stoneBounds=stoneRenderers[0].bounds;
            foreach(var renderer in stoneRenderers.Skip(1))stoneBounds.Encapsulate(renderer.bounds);
            Require(Mathf.Abs(stoneBounds.center.x-terrain.TerraceContactX)<.001f && Mathf.Abs(stoneBounds.min.y+terrain.Clearance)<.001f &&
                Mathf.Abs(stoneBounds.max.y-4)<.001f && stoneBounds.min.z<=rear.Start.z-rear.Width/2 && stoneBounds.max.z>=rear.Start.z+rear.Width/2,"Stone wall does not close the exposed Y4 platform face behind stairs");
            var facade=model.GetComponentInChildren<VisualFrameFacade>();
            Require(facade.AccessDoorCount==2 && !facade.GetComponentsInChildren<Transform>().Any(t=>t.name=="Paso_puerta_escalera_E1_288"),"Unwanted stair opening remains in glazing");
            camObject=new GameObject("Environment preview camera");var camera=camObject.AddComponent<Camera>();
            var walk=model.AddComponent<DesktopWalkthrough>();walk.Configure(viewer,data);walk.Enter(camera);
            foreach(var door in partitions.DoorCenters)
                Require(!Physics.CheckCapsule(door-Vector3.up*.8f,door+Vector3.up*.7f,.25f,1<<2),"New room doorway blocked: "+door);
            foreach(float dz in new[]{-12.3f,-13.3f,-14.8f,-16f})foreach(float x in new[]{-13f,-12f,-10f,28f,30f,32f})
            {
                Require(Physics.Raycast(new Vector3(x,80,front+dz),Vector3.down,out var hit,150,1<<2),"Missing sidewalk");
                Require(hit.collider.name=="Recorrido_Vereda_en_pendiente","Background terrain intersects sidewalk: "+hit.collider.name);
            }
            Require(Physics.Raycast(new Vector3(31.5f,5.6f,10),Vector3.back,out var glassHit,2,1<<2) && glassHit.collider.name.Contains("Vidrio"),"Previous stair opening is not closed by glass");
            walk.Teleport(rear.End+Vector3.up*.08f);walk.Body.transform.rotation=Quaternion.Euler(0,90,0);
            for(int i=0;i<20;i++)walk.Step(Vector2.zero,false,false,.02f);
            int travelSteps=Mathf.CeilToInt((rear.Start.x-rear.End.x+1)/(.02f*walk.walkingSpeed));
            for(int i=0;i<travelSteps;i++)walk.Step(Vector2.up,false,false,.02f);
            Require(walk.Body.transform.position.y>3.9f && walk.Body.transform.position.x>terrain.TerraceContactX,"Cannot climb from cafe to terrace: "+walk.Body.transform.position);
            walk.Teleport(rear.Start+Vector3.up*.08f);walk.Body.transform.rotation=Quaternion.Euler(0,270,0);
            for(int i=0;i<20;i++)walk.Step(Vector2.zero,false,false,.02f);
            for(int i=0;i<travelSteps;i++)walk.Step(Vector2.up,false,false,.02f);
            Require(Mathf.Abs(walk.Body.transform.position.y+terrain.Clearance)<.15f && walk.Body.transform.position.x<rear.End.x,"Cannot descend onto exterior platform: "+walk.Body.transform.position);
            Require(!Physics.CheckCapsule(room.DoorCenter-Vector3.up*.8f,room.DoorCenter+Vector3.up*.7f,.25f,1<<2),"Study-room doorway blocked by new partitions");
            walk.Exit();
            camera.transform.position=new Vector3(48,17,28);camera.transform.LookAt(new Vector3(32,2,12));Capture(camera,"Logs/environment-revision-stairs-solid.png");
            camera.transform.position=new Vector3(55,29,40);camera.transform.LookAt(new Vector3(18,4,2));Capture(camera,"Logs/environment-revision-rear.png");
            camera.transform.position=new Vector3(10,33,-49);camera.transform.LookAt(new Vector3(10,0,-24));Capture(camera,"Logs/environment-revision-parking.png");
            var roofs=model.GetComponentInChildren<VisualFlatRoof>();if(roofs!=null)roofs.gameObject.SetActive(false);
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())if(renderer.bounds.min.y>7.8f)renderer.enabled=false;
            camera.transform.position=new Vector3(42,30,-25);camera.transform.LookAt(new Vector3(10,4.5f,1));Capture(camera,"Logs/environment-revision-rooms.png");
            Debug.Log("[Environment revision] PASS: five clear furnished-room doors plus visible computer-room door; ten tables and twenty-eight chairs, walls18/23/28 access sector cleared of furniture and added door; core access preserved; solid 3m-wide terrace stairs separated 2m from facade, lower end aligned to E1_288 X, traversable both ways; original glazing closed. Max sidewalk slope="+maxSlope);
            Cleanup(model,camObject);EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);Cleanup(model,camObject);EditorApplication.Exit(1);}
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Cleanup(params GameObject[] objects){foreach(var obj in objects)if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);}
    private static void Capture(Camera camera,string path)
    {
        var lamp=new GameObject("Environment preview light");var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;lamp.transform.rotation=Quaternion.Euler(40,-35,0);
        var target=new RenderTexture(1440,900,24);camera.targetTexture=target;camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(1440,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1440,900),0,0);image.Apply();System.IO.File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(lamp);
    }
}

using System;
using System.Linq;
using UnityEngine;

public static class MobileEnvironmentRevisionValidation
{
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Validate(GameObject world)
    {
        var data=UnityData.Structure;
        var facade=world.GetComponentInChildren<VisualFrameFacade>();
        Func<GameObject,string,string,bool> pair=(p,a,b)=>p.name.Contains(a+"_")&&p.name.Contains(b+"_");
        Require(!facade.Panels.Any(p=>pair(p,"E1_271","E1_259")),"Old upper cantilever window remains");
        foreach(var tags in new[]{new[]{"E1_259","E1_310"},new[]{"E1_271","E1_311"},new[]{"E1_311","E1_310"},new[]{"E1_245","E1_259"},new[]{"E1_271","E1_283"}})
            Require(facade.Panels.Any(p=>pair(p,tags[0],tags[1])),"Upper/neighbor window missing: "+tags[0]+" "+tags[1]);
        var analyticalNodes=data.nodes.ToDictionary(n=>n.id);
        Require(data.p1l4.analysisModel.diafragmas.Length==5&&data.p1l4.analysisModel.diafragmas.All(g=>DiaphragmKinematics.Validate(g,analyticalNodes)==null),"Real diaphragm membership invalid");
        Require(MobileDiaphragmInfo.Describe(data.elements.Single(e=>e.elementTag=="E1_259"),"C1").Contains("Maestro"),"Mobile diaphragm report absent");
        var nodes=data.nodes.ToDictionary(n=>n.id,n=>new Vector3(n.x,n.z,n.y));
        foreach(var wall in data.walls.Where(w=>w.id>=31&&w.id<=35))
            Require(Mathf.Abs(nodes[wall.nodeI].z-6.99f)<.001f&&Mathf.Abs(nodes[wall.nodeJ].z-6.99f)<.001f,"Wall33 family not in corrected positive Z position");
        foreach(var wall in data.walls.Where(w=>w.id>=36&&w.id<=40))
            Require(Mathf.Abs(Mathf.Min(nodes[wall.nodeI].z,nodes[wall.nodeJ].z)-4.045f)<.001f&&Mathf.Abs(Mathf.Max(nodes[wall.nodeI].z,nodes[wall.nodeJ].z)-6.99f)<.001f,"Wall38 family not in positive Z position");
        var partitions=world.GetComponentInChildren<VisualInteriorPartitions>();
        Require(partitions!=null&&partitions.Tables.Count==10&&partitions.DoorCenters.Count==5&&partitions.GetComponentsInChildren<Transform>().Count(t=>t.name=="Silla_habitacion")==28,"New furnished rooms incomplete");
        Require(partitions.Pieces.All(p=>p.GetComponent<ElementSelectable>()==null&&p.GetComponent<Collider>()==null),"Visual partitions affect structural IDs/colliders");
        Require(partitions.GetComponentsInChildren<Renderer>(true).All(r=>r.enabled&&r.gameObject.layer==30&&r.sharedMaterials.All(m=>m.shader.name=="MCOC/VR Visual Environment")),"Partitions missing stereo shader/layer");
        Require(!partitions.Pieces.Any(p=>p.name=="Tabique_visual_central_E1_285_E1_297")&&!partitions.Tables.Any(t=>t.name.StartsWith("Mesa_habitacion_1_")),"Removed wall or core furniture remains");
        var terrain=world.GetComponentInChildren<VisualSiteTerrain>();
        var platforms=terrain.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("Pasto_")).ToArray();
        Require(platforms.Length>=4&&platforms.All(r=>r.sharedMaterial.name.StartsWith("Cemento_plataformas_visuales")),"Concrete platforms absent");
        Require(terrain.UpperTerrace.transform.Find("Muro_piedra_vertical_detras_escalera")!=null,"Stone closure behind stairs absent");
        var stairs=world.GetComponentInChildren<VisualStairs>();
        var rear=stairs.Paths.Single(p=>p.Name=="Escalera_lateral_E1_288_Y4");
        Require(stairs.FlightCount==5&&Mathf.Abs(rear.Width-3)<.001f&&rear.End.x<rear.Start.x&&Mathf.Abs(rear.End.x-nodes[data.elements.Single(e=>e.elementTag=="E1_288").nodeI].x)<.001f,"Rear stairs wrong width/direction/end");
        Require(world.GetComponentInChildren<VisualStudyRoom>().transform.Find("Puerta_sala_computadores_abierta")!=null,"Computer room door absent");
        Require(world.GetComponentInChildren<VisualCampusSite>().GetComponentsInChildren<MeshFilter>().Count(m=>m.name=="Vereda_en_pendiente")==1,"Continuous sidewalk absent");
        Debug.Log("[Mobile environment] PASS: corrected walls on all five stories; concrete platforms, stone closure, five stair flights; five furnished rooms, 10 tables, 28 chairs, computer door; stereo material/layer and visual-only partitions.");
    }
}

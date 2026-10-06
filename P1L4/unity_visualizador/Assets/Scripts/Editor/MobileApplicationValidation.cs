using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class MobileApplicationValidation
{
    private static double next;
    private static float referenceMoment;
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static MobileApplicationValidation(){EditorApplication.update+=Tick;}
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    private static object Field(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
    private static void Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
    public static void ValidateBatch()
    {
        VisualCafe.UseDesktopLayout=true;
        AndroidVRBuild.ConfigureAndroid();
        StructuralARValidation.ValidateOrThrow();StructuralARSectorValidation.Validate();
        EditorSceneManager.OpenScene(StructuralARSceneSetup.ScenePath);
        EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView")).Show();
        SessionState.SetInt("MCOCMobileValidation",1);EditorApplication.isPlaying=true;
    }
    private static void Tick()
    {
        int phase=SessionState.GetInt("MCOCMobileValidation",0);
        if(phase==0||!EditorApplication.isPlaying||EditorApplication.timeSinceStartup<next)return;
        try
        {
            var p=MobileSeismicPlayback.Instance;
            var vr=UnityEngine.Object.FindAnyObjectByType<StructuralVRController>();
            if(phase==1)
            {
                Require(p!=null&&vr!=null,"Missing mobile controllers");
                Require(UnityData.TryGetSectionForces(94,"C1",.5f,out var initialForce),"Initial static result missing");
                referenceMoment=initialForce.My;
                MobileEngineeringValidation.ValidateData();
                var arTools=(MobileStructuralTools)typeof(StructuralARController).GetField("engineering",Flags).GetValue(UnityEngine.Object.FindAnyObjectByType<StructuralARController>());
                arTools.Lrfd.Input.d=.5f;Require(arTools.Lrfd.Evaluate(),arTools.Lrfd.Status);
                foreach(string direction in new[]{"X","Y"})
                {
                    if(p.Direction!=direction)p.CycleDirection();
                    for(int i=0;i<4;i++)
                    {
                        Require(p.LoadCurrent(),p.Status);
                        Require(p.Response.Metadata.frameCount==1660&&!p.Response.Metadata.truncated,"Incomplete earthquake");
                        p.Seek(2.18f);
                        Require(p.TrySection(94,.5f,out var section)&&!float.IsNaN(section.My),"Mobile forces absent");
                        var reference=p.Forces(94);
                        UnityData.TryGetFrameGeometry(94,out var geometry);
                        Require(Mathf.Abs(section.My-FrameForces.Evaluate(reference,geometry.Length,.5f).My)<.0001f,"Section mismatch");
                        float force=section.My;p.CycleScale();p.TrySection(94,.5f,out section);
                        Require(force==section.My,"Visual scale changed forces");
                        p.Close();p.CycleIntensity();GC.Collect();
                    }
                }
                Debug.Log("[Mobile application] PASS: eight complete X/Y cases; hashes, IDs, nodes, forces; scale-independent values.");
                vr.StartCoroutine(vr.EnterVR());
            }
            if(phase==2)
            {
                Require(vr.IsVR,"VR did not start");
                var world=(GameObject)Field(vr,"world");
                Require(world.GetComponentInChildren<VisualCafe>()!=null&&world.GetComponentInChildren<VisualCampusSite>()!=null&&
                    world.GetComponentInChildren<VisualFootballCrowd>()!=null,"New campus absent in VR");
                foreach(var root in world.GetComponentsInChildren<VisualCafe>().Select(c=>c.transform).Concat(world.GetComponentsInChildren<VisualCampusSite>().Select(c=>c.transform)))
                    Require(root.GetComponentsInChildren<Renderer>(true).All(r=>r.enabled&&r.gameObject.layer==30&&
                        r.sharedMaterials.All(m=>m.shader.name=="MCOC/VR Visual Environment")),"Campus material/layer mismatch");
                var cafe=world.GetComponentInChildren<VisualCafe>();
                var room=world.GetComponentInChildren<VisualStudyRoom>();
                Require(room!=null && room.Tables.Count==4,"Updated study room missing in VR");
                MobileEnvironmentRevisionValidation.Validate(world);
                var laptopImages=room.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Imagen_laptop").ToArray();
                Require(laptopImages.Length==4 && laptopImages.All(r=>r.gameObject.layer==30 && r.sharedMaterial.shader.name=="MCOC/VR Visual Environment" && r.sharedMaterial.mainTexture!=null),"Laptop texture missing in stereo VR");
                var nodeMap=UnityData.Structure.nodes.ToDictionary(n=>n.id);
                Require(UnityData.Structure.elements.Where(e=>e.type=="columna" && e.sourceId=="C1004").All(e=>Mathf.Abs(nodeMap[e.nodeI].y)<.001f),"E2 center axis not updated");
                Require(Mathf.Abs(nodeMap[UnityData.Structure.walls.Single(w=>w.id==42).nodeJ].y+.35f)<.001f,"Wall 42 still crosses central column");
                var terrain=world.GetComponentInChildren<VisualSiteTerrain>();
                var cafeFloor=cafe.transform.Find("Piso_cafeteria_nivel_4");
                Require(cafeFloor!=null&&Mathf.Abs(cafeFloor.localPosition.y+cafeFloor.localScale.y/2-(VisualCafe.UseDesktopLayout?0f:4.03f))<.001f,"Cafe not on floor 1");
                Require(cafe.transform.Find("Cielo_cafeteria").localPosition.y>(VisualCafe.UseDesktopLayout?3f:7f),"Cafe ceiling not raised");
                var cafeBounds=cafe.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(cafeFloor.position,Vector3.zero),(bounds,r)=>{bounds.Encapsulate(r.bounds);return bounds;});
                if(!VisualCafe.UseDesktopLayout)
                {
                var flat=terrain.UpperTerrace.transform.Find("Pasto_terraza_Y4").GetComponent<MeshFilter>().sharedMesh.bounds;
                var slope=terrain.UpperTerrace.transform.Find("Talud_Y4_hacia_menos_X").GetComponent<MeshFilter>().sharedMesh.bounds;
                Require(flat.min.x<=cafeBounds.min.x&&flat.max.x>=cafeBounds.max.x,$"Terrace does not cover cafe length: flat {flat.min.x}..{flat.max.x}; cafe {cafeBounds.min.x}..{cafeBounds.max.x}");
                Require(slope.max.x<=cafeBounds.min.x+.001f&&Mathf.Abs(slope.max.x-flat.min.x)<.001f,"Slope overlaps cafe or leaves gap");
                Debug.Log("[Cafe layout] PASS: floor Y4, raised ceiling, terrace covers cafe, slope begins outside cafe towards -X.");
                }
                var member=world.GetComponentsInChildren<ElementSelectable>().Single(e=>e.data!=null&&e.data.id==94);
                MobileEngineeringValidation.ValidateVR(vr,member);
                Call(vr,"SelectElement",member);
                Vector3 original=member.startPoint;
                Require(p.LoadCurrent(),p.Status);p.Seek(3);Call(vr,"UpdateMobileWorld");
                Require(Vector3.Distance(member.startPoint,p.Position(member.data.nodeI))<.0001f,"Deformed node mismatch");
                Require(Vector3.Distance(member.startPoint,original)>.001f,"VR structure did not deform");
                Call(vr,"RefreshResult");Require(((TextMesh)Field(vr,"info")).text.Contains("SISMO"),"Dynamic result mislabelled C1");
                var before=p.Forces(94).ToArray();p.CycleScale();Call(vr,"UpdateMobileWorld");
                Require(before.SequenceEqual(p.Forces(94)),"Scale modified analytical values");
                p.ShowCracks=true;Call(vr,"UpdateMobileWorld");
                var history=(SeismicDamageHistory)Field(vr,"mobileHistory");
                Require(history!=null&&history.Samples.Length==1660,"Crack history absent");
                p.Seek(0);Call(vr,"UpdateMobileWorld");
                Require(((TextMesh)Field(vr,"info")).text.Contains("sin colapso"),"Damage interpretation missing");
                p.Close();Call(vr,"UpdateMobileWorld");Require(member.startPoint==original,"Static geometry not restored");
                Require(UnityData.TryGetSectionForces(94,"C1",.5f,out var staticForce)&&Mathf.Abs(staticForce.My-referenceMoment)<.001f,"Static result not restored");
                Call(vr,"ToggleEnvironment");Require(!world.GetComponentInChildren<VisualCafe>(true).gameObject.activeInHierarchy,"Cafe toggle failed");
                Require(!room.gameObject.activeInHierarchy,"Study room environment toggle failed");
                Require(!world.GetComponentInChildren<VisualInteriorPartitions>(true).gameObject.activeInHierarchy,"Partitions environment toggle failed");
                Call(vr,"ToggleEnvironment");Require(world.GetComponentInChildren<VisualCafe>().gameObject.activeInHierarchy,"Cafe restore failed");
                Require(world.GetComponentInChildren<VisualInteriorPartitions>().gameObject.activeInHierarchy,"Partitions restore failed");
                vr.ExitVR();Require(!vr.IsVR&&!MobileSeismicPlayback.IsActive,"AR/static restore failed");
                Require(UnityEngine.Object.FindAnyObjectByType<StructuralARController>().enabled,"AR controller disabled after exit");
                var restored=UnityData.GetElementForces("LRFD_MOBILE_0",94);var gravity=UnityData.GetElementForcesForCase("G",94);
                Require(restored!=null&&gravity!=null&&restored.Where((f,i)=>Mathf.Abs(f-.7f*gravity[i])>.003f).Count()==0,"AR LRFD scenario not restored after VR");
                Debug.Log("[Mobile application] PASS: new campus/cafe/windows; VR playback/deformation/selection/forces/capacity/cracks; static and AR restore. Native phone tracking requires device.");
                SessionState.SetInt("MCOCMobileValidation",0);EditorApplication.Exit(0);return;
            }
            SessionState.SetInt("MCOCMobileValidation",phase+1);next=EditorApplication.timeSinceStartup+3;
        }
        catch(Exception error){Debug.LogException(error);SessionState.SetInt("MCOCMobileValidation",0);EditorApplication.Exit(1);}
    }
}

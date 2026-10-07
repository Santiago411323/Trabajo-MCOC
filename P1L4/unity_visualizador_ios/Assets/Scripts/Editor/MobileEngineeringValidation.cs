using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class MobileEngineeringValidation
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void ValidateData()
    {
        var lab=new MobileLrfd();Require(lab.Load(),lab.Status);Require(lab.Evaluate(),lab.Status);
        Require(lab.Dataset.variants.Length==22&&lab.Current,"Missing 22 mobile variants");
        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../lrfd_results/validation_results.json"));
        var reference=JsonUtility.FromJson<LrfdDataset>(File.ReadAllText(path));
        Require(reference.modelHash==lab.Dataset.modelHash,"Reference model mismatch");
        for(int i=0;i<22;i++)
        {
            var a=lab.Dataset.variants[i];var b=reference.variants[i];Require(a.u==b.u,"Variant order mismatch");
            var lookup=b.forces.ToDictionary(f=>f.id);
            Require(a.forces.Length==b.forces.Length,"Missing LRFD member");
            foreach(var row in a.forces)for(int k=0;k<12;k++)Require(Mathf.Abs(row.f[k]-lookup[row.id].f[k])<Mathf.Max(.003f,Mathf.Abs(lookup[row.id].f[k])*2e-5f),"Force superposition mismatch");
            var nodes=b.displacements.ToDictionary(d=>d.node);Require(a.displacements.Length==b.displacements.Length,"Missing LRFD node");
            foreach(var row in a.displacements){var expected=nodes[row.node];Require(Mathf.Abs(row.ux-expected.ux)<.00001f&&Mathf.Abs(row.uz-expected.uz)<.00001f&&Mathf.Abs(row.rx-expected.rx)<.00001f,"LRFD displacement/rotation mismatch");}
        }
        var member=UnityData.Structure.elements.First(e=>e.id==94);
        using(var tools=new MobileStructuralTools())
        {
            Require(tools.Inspect(member,"C1").Contains("As="),"Reinforcement unavailable");
            var ranking=tools.Rank(UnityData.Structure.elements);Require(ranking.Count>5,"Radar empty");
            Require(ranking.Zip(ranking.Skip(1),(a,b)=>a.Reading.Value>=b.Reading.Value).All(v=>v),"Radar not sorted");
            var rows=lab.Rows(member);Require(rows.All(r=>r!=null&&r.HasCapacity),"Design rows incomplete");
        }
        lab.Input.windSpeed+=5;Require(!lab.Current,"Changed scenario still current");
        Debug.Log("[Mobile engineering] PASS: all 22 variants agree with OpenSees, member forces, nodes, rotations, reinforcement, radar, capacities and invalidation.");
    }
    public static void ValidateVR(StructuralVRController vr,ElementSelectable member)
    {
        void Call(string name,params object[] args)=>typeof(StructuralVRController).GetMethod(name,Flags).Invoke(vr,args);
        object Field(string name)=>typeof(StructuralVRController).GetField(name,Flags).GetValue(vr);
        void Set(string name,object value)=>typeof(StructuralVRController).GetField(name,Flags).SetValue(vr,value);
        Call("SelectElement",member);Call("ToggleEngineeringVR");
        var tools=(MobileStructuralTools)Field("vrEngineering");tools.Progress=.4f;
        Call("UpdateEngineeringVR");Require(tools.Preview?.Texture!=null&&tools.Plot!=null,"Missing mobile 3D/curves");
        tools.Preview.RenderNow();
        var screen=(Renderer)Field("engineeringScreen");Require(screen.enabled&&screen.sharedMaterial.mainTexture==tools.Preview.Texture,"Stereo inspection texture absent");
        Require(screen.sharedMaterial.shader.name=="MCOC/VR Inspector Surface","RGB inspection shader missing");
        tools.PlotMode=2;tools.UpdatePreview(member.data,"C1",0);Require(tools.Plot!=null,"Design P-My plot absent");tools.PlotMode=0;tools.UpdatePreview(member.data,"C1",0);
        Capture((Camera)Field("vrCamera"),"armadura");
        Set("vrEngineeringPage",3);Set("nextEngineering",0f);Call("UpdateEngineeringVR");
        Require(((TextMesh)Field("engineeringInfo")).text.Contains("Maestro"),"VR diaphragm page missing real membership");
        Require(!((Transform)Field("engineeringRoot")).Find("Accion 0").gameObject.activeInHierarchy,"Diaphragm page retained unrelated actions");
        Set("vrEngineeringPage",1);Set("nextEngineering",0f);Call("UpdateEngineeringVR");Call("EngineeringAction",6);Set("nextEngineering",0f);Call("UpdateEngineeringVR");
        Require(((Transform)Field("engineeringRoot")).Find("Accion 0").gameObject.activeInHierarchy,"Actions not restored after diaphragm page");
        Call("EngineeringAction",4);Require(((ElementSelectable)Field("selected"))!=null,"Gaze radar selection failed");
        Capture((Camera)Field("vrCamera"),"radar");
        Set("vrEngineeringPage",2);Call("EngineeringAction",3);Call("EngineeringAction",6);
        Require(((string)Field("combo")).StartsWith("LRFD_"),"LRFD diagram not applied");
        Call("EngineeringAction",9);Set("nextEngineering",0f);Call("UpdateEngineeringVR");
        var world=(GameObject)Field("world");var loads=world.transform.Find("Laboratorio_LRFD_solo_visual");
        Require(loads!=null&&loads.GetComponentsInChildren<Renderer>(true).All(r=>r.gameObject.layer==30),"VR load layer mismatch");
        Require(loads.GetComponentsInChildren<Collider>(true).All(c=>!c.enabled),"Visual loads block movement");
        Capture((Camera)Field("vrCamera"),"lrfd");
        Call("EngineeringAction",2);Require(!tools.Lrfd.Current&&((string)Field("combo"))=="C1","Scenario change did not restore static case");
        Call("ToggleEngineeringVR");Require(!(bool)Field("engineeringOpen"),"Engineering menu did not close");
        UnityData.UseBaseCaseFactors=false;Set("combo","C1");Call("SelectElement",member);
        Debug.Log("[Mobile engineering] PASS: gaze controls, 3D render texture, curves, radar selection/map, LRFD diagrams, visuals, invalidation and restored movement menu.");
    }
    private static void Capture(Camera camera,string label)
    {
        var target=new RenderTexture(1440,900,24);target.Create();var old=camera.targetTexture;var active=RenderTexture.active;
        var texture=new Texture2D(1440,900,TextureFormat.RGB24,false);
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1440,900),0,0);texture.Apply();
            string project=Path.GetFileName(Path.GetDirectoryName(Application.dataPath));
            File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../../revisiones/"+project+"_ingenieria_"+label+".png")),texture.EncodeToPNG());}
        finally{camera.targetTexture=old;RenderTexture.active=active;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(texture);}
    }
}

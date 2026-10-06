using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Decorative rails only; never add structural IDs, loads or colliders.
public sealed class VisualSalmonRailings : MonoBehaviour
{
    private Material salmon;
    private readonly List<Mesh> panels=new List<Mesh>();
    public readonly List<string> BeamTags=new List<string>();
    public readonly List<Vector3[]> Routes=new List<Vector3[]>();
    private System.Action<GameObject,string> register;

    public void Build(StructureData data,VisualStairs stairs,System.Action<GameObject,string> onPiece)
    {
        register=onPiece;
        salmon=new Material(Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit"))
            {name="Barandas_naranjo_salmon_visuales",color=new Color(.91f,.49f,.35f)};
        if(salmon.HasProperty("_Glossiness"))salmon.SetFloat("_Glossiness",.12f);
        var nodes=data.nodes.ToDictionary(n=>n.id,n=>new Vector3(n.x,n.z,n.y));
        foreach(string tag in new[]{"E1_208","E1_206","E1_221.1","E1_221.2"})
        {
            var beam=data.elements.FirstOrDefault(e=>e.elementTag==tag);
            if(beam==null)continue;
            Vector3 lift=Vector3.up*(beam.height_m/2+.14f);
            Route("Baranda_"+tag,nodes[beam.nodeI]+lift,nodes[beam.nodeJ]+lift,beam.piso);
            BeamTags.Add(tag);
        }
        foreach(var flight in stairs.Paths.Where(p=>p.Name.StartsWith("Escalera_E1_")))
        {
            Vector3 direction=flight.End-flight.Start;direction.y=0;direction.Normalize();
            Vector3 side=Vector3.Cross(Vector3.up,direction)*flight.Width/2;
            // Keep only the outward edge (negative Unity Z), aligned with the beams.
            if(side.z>0)side=-side;
            Route("Baranda_"+flight.Name+"_exterior",flight.Start+side,flight.End+side,flight.Floor);
            // Close the short landing gaps to the outer beam rail, leaving the
            // walking route through the centre of each landing unobstructed.
            string startTag=flight.Name.Contains("207")?"E1_206":"E1_221.2";
            string endTag=flight.Name.Contains("207")?"E1_221.1":null;
            Landing(startTag,flight.Start,nodes,data,flight.Width,flight.Floor);
            if(endTag!=null)Landing(endTag,flight.End,nodes,data,flight.Width,flight.Floor);
        }
        var normal=stairs.Paths.FirstOrDefault(p=>p.Name=="Escalera_Y4_cafeteria_normal");
        var wide=stairs.Paths.FirstOrDefault(p=>p.Name=="Gradas_Y4_cafeteria_rectas");
        if(normal!=null && wide!=null)
        {
            // Retain the two outer borders; no divider between stairs and seating.
            Vector3 offset=Vector3.forward*(normal.Width/2);
            Route("Baranda_acceso_Y4_exterior",normal.Start+offset,normal.End+offset,normal.Floor,24);
            Route("Baranda_gradas_borde_exterior",wide.Start-Vector3.forward*wide.Width/2,
                wide.End-Vector3.forward*wide.Width/2,wide.Floor,8);
        }
        var rear=stairs.Paths.FirstOrDefault(p=>p.Name=="Escalera_lateral_E1_288_Y4");
        if(rear!=null)
        {
            var direction=rear.End-rear.Start;direction.y=0;
            var side=Vector3.Cross(Vector3.up,direction.normalized)*rear.Width/2;
            Route("Baranda_E1_288_izquierda",rear.Start-side,rear.End-side,rear.Floor);
            Route("Baranda_E1_288_derecha",rear.Start+side,rear.End+side,rear.Floor);
        }

    }

    private void Landing(string tag,Vector3 landing,Dictionary<int,Vector3> nodes,StructureData data,float width,string floor)
    {
        var beam=data.elements.FirstOrDefault(e=>e.elementTag==tag);if(beam==null)return;
        Vector3 a=nodes[beam.nodeI],b=nodes[beam.nodeJ];
        Vector3 end=Vector3.Distance(a,landing)<Vector3.Distance(b,landing)?a:b;
        end.y+=beam.height_m/2+.14f;
        Route("Baranda_descanso_"+tag,end,landing-Vector3.forward*width/2,floor);
    }

    private void Route(string name,Vector3 a,Vector3 b,string floor,int supportSteps=0)
    {
        if(Vector3.Distance(a,b)<.02f)return;
        Routes.Add(new[]{a,b});
        ClosedPanels(name,a,b,floor,supportSteps);
        Vector3 horizontal=b-a;horizontal.y=0;
        int spans=Mathf.Max(1,Mathf.CeilToInt(horizontal.magnitude/1.15f));
        for(int i=0;i<=spans;i++)
        {
            Vector3 p=Vector3.Lerp(a,b,(float)i/spans);
            float foot=p.y;
            if(supportSteps>0)foot=Mathf.Lerp(a.y,b.y,Mathf.Ceil((float)i/spans*supportSteps)/supportSteps);
            float height=p.y+1.05f-foot;
            Vector3 center=p;center.y=foot+height/2;
            Box(name+"_poste_"+i,center,new Vector3(.07f,height,.07f),Quaternion.identity,floor);
        }
        foreach(float h in new[]{.53f,1.05f})
        {
            Vector3 start=a+Vector3.up*h,end=b+Vector3.up*h;
            Box(name+"_pasamanos_"+h,(start+end)/2,new Vector3(.07f,.07f,Vector3.Distance(start,end)),
                Quaternion.LookRotation(end-start,Vector3.up),floor);
        }
    }

    private void ClosedPanels(string name,Vector3 a,Vector3 b,string floor,int steps)
    {
        Vector3 along=b-a;along.y=0;
        Vector3 side=Vector3.Cross(Vector3.up,along.normalized)*.05f;
        int count=Mathf.Max(1,steps);
        for(int i=0;i<count;i++)
        {
            Vector3 p=Vector3.Lerp(a,b,(float)i/count),q=Vector3.Lerp(a,b,(i+1f)/count);
            Vector3 lowP=p-Vector3.up*.18f,lowQ=q-Vector3.up*.18f;
            if(steps>0)lowP.y=lowQ.y=Mathf.Lerp(a.y,b.y,(i+1f)/count)-.03f;
            Vector3 highP=p+Vector3.up*1.02f,highQ=q+Vector3.up*1.02f;
            var mesh=new Mesh{name=name+"_panel_cerrado_"+i};
            mesh.vertices=new[]{lowP-side,lowQ-side,highQ-side,highP-side,lowP+side,lowQ+side,highQ+side,highP+side};
            mesh.triangles=new[]{0,1,2,0,2,3,4,6,5,4,7,6,0,7,4,0,3,7,1,6,2,1,5,6,3,6,7,3,2,6,0,5,1,0,4,5};
            mesh.RecalculateNormals();mesh.RecalculateBounds();panels.Add(mesh);
            var piece=new GameObject(mesh.name);piece.layer=2;piece.transform.SetParent(transform,false);
            piece.AddComponent<MeshFilter>().sharedMesh=mesh;piece.AddComponent<MeshRenderer>().sharedMaterial=salmon;
            register(piece,floor);
        }
    }

    private void Box(string name,Vector3 center,Vector3 size,Quaternion rotation,string floor)
    {
        var piece=GameObject.CreatePrimitive(PrimitiveType.Cube);piece.name=name;piece.layer=2;
        piece.transform.SetParent(transform,false);piece.transform.localPosition=center;
        piece.transform.localRotation=rotation;piece.transform.localScale=size;
        piece.GetComponent<Renderer>().sharedMaterial=salmon;
        var collider=piece.GetComponent<Collider>();collider.enabled=false;
        if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);
        register(piece,floor);
    }

    private void OnDestroy()
    {
        foreach(var mesh in panels)
            if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);
        if(salmon==null)return;
        if(Application.isPlaying)Destroy(salmon);else DestroyImmediate(salmon);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Context is an owned transparent copy. Original materials, colliders and model data are untouched.
public sealed class StructuralXRayOverlay : IDisposable
{
    private sealed class Stroke
    {
        public GameObject Object;public LineRenderer Line;public int Id,Stage;public bool Dashed;
        public float Intensity,Target;public Color Color;public string Purpose;
    }
    private GameObject root;
    private Material pulseMaterial,contextMaterial;
    private MaterialPropertyBlock block;
    private readonly List<Stroke> strokes=new List<Stroke>();
    private readonly Dictionary<Renderer,bool> hidden=new Dictionary<Renderer,bool>();
    private Mesh structuralMesh,decorationMesh,regionMesh;
    private Renderer regionRenderer,decorationRenderer;
    private int used;
    private Transform parent;

    public StructuralXRayOverlay(Transform owner)
    {
        parent=owner;root=new GameObject("Structural_XRay_sin_efecto_en_OpenSees");root.hideFlags=HideFlags.DontSave;root.transform.SetParent(owner,false);
        var pulse=Resources.Load<Shader>("StructuralXRayPulse");var context=Resources.Load<Shader>("StructuralXRayContext");
        pulseMaterial=new Material(pulse!=null?pulse:Shader.Find("Sprites/Default"));
        contextMaterial=new Material(context!=null?context:Shader.Find("Sprites/Default"));block=new MaterialPropertyBlock();
        var structural=new List<MeshFilter>();var decorations=new List<MeshFilter>();
        foreach(var mesh in owner.GetComponentsInChildren<MeshFilter>(true))
        {
            if(mesh.transform.IsChildOf(root.transform)||!mesh.gameObject.activeInHierarchy||mesh.sharedMesh==null||!mesh.sharedMesh.isReadable)continue;
            var renderer=mesh.GetComponent<Renderer>();if(renderer==null||!renderer.enabled||mesh.GetComponentInParent<TextMesh>()!=null)continue;
            bool core=mesh.GetComponent<ElementSelectable>()!=null||mesh.GetComponent<SlabSelectable>()!=null;
            bool decorative=mesh.GetComponentInParent<VisualFrameFacade>()!=null||mesh.GetComponentInParent<VisualFlatRoof>()!=null||
                mesh.GetComponentInParent<VisualStairs>()!=null||mesh.GetComponentInParent<VisualCafe>()!=null||mesh.GetComponentInParent<VisualStudyRoom>()!=null||mesh.GetComponentInParent<VisualInteriorPartitions>()!=null;
            if(!core&&!decorative)continue;
            hidden[renderer]=renderer.enabled;renderer.enabled=false;
            if(core)structural.Add(mesh);else decorations.Add(mesh);
        }
        structuralMesh=ContextMesh(structural,"Contexto_estructura",.13f,out _);
        decorationMesh=ContextMesh(decorations,"Acabados_tenues",.065f,out decorationRenderer);
        regionMesh=new Mesh{name="Regiones_del_reparto_real"};MeshObject(regionMesh,"Región_tributaria_activa",out regionRenderer);
    }
    private Mesh ContextMesh(List<MeshFilter> sources,string name,float alpha,out Renderer renderer)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();var colors=new List<Color>();
        foreach(var source in sources)
        {
            var mesh=source.sharedMesh;int start=vertices.Count;var matrix=root.transform.worldToLocalMatrix*source.transform.localToWorldMatrix;
            Color native=new Color(.4f,.65f,.76f);var mat=source.GetComponent<Renderer>().sharedMaterial;
            if(mat!=null&&mat.HasProperty("_Color"))native=mat.color;
            Color color=Color.Lerp(native,new Color(.5f,.7f,.78f),.5f);color.a=alpha;
            foreach(var v in mesh.vertices){vertices.Add(matrix.MultiplyPoint3x4(v));colors.Add(color);}
            foreach(int index in mesh.triangles)triangles.Add(index+start);
        }
        var combined=new Mesh{name=name,indexFormat=IndexFormat.UInt32};combined.SetVertices(vertices);combined.SetColors(colors);combined.SetTriangles(triangles,0);combined.RecalculateBounds();
        MeshObject(combined,name,out renderer);return combined;
    }
    private void MeshObject(Mesh mesh,string name,out Renderer renderer)
    {
        var go=new GameObject(name);go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=contextMaterial;
    }
    private static Vector3 Position(NodeData n)=>new Vector3(n.x,n.z,n.y);
    private Stroke GetStroke(int id,Vector3 a,Vector3 b,Color color,float intensity,int stage,bool dashed,string purpose)
    {
        Stroke stroke;
        if(used<strokes.Count)stroke=strokes[used];else
        {
            var go=new GameObject("Pulso_reutilizable_"+used);go.transform.SetParent(root.transform,false);var line=go.AddComponent<LineRenderer>();
            line.sharedMaterial=pulseMaterial;line.useWorldSpace=true;line.positionCount=2;line.textureMode=LineTextureMode.Stretch;
            line.numCapVertices=4;stroke=new Stroke{Object=go,Line=line};strokes.Add(stroke);
        }
        stroke.Id=id;stroke.Stage=stage;stroke.Color=color;stroke.Target=intensity;stroke.Dashed=dashed;stroke.Purpose=purpose;
        stroke.Line.SetPosition(0,a);stroke.Line.SetPosition(1,b);stroke.Object.SetActive(true);used++;return stroke;
    }
    public void Bind(IncrementalResponseNetwork graph,IncrementalResponseNetwork.Result result,bool showConstraints)
    {
        used=0;var origin=new Vector3(result.Frame.X,graph.Slabs[result.Frame.Slab].z,result.Frame.Y);
        GetStroke(0,origin+Vector3.up*1.6f,origin+Vector3.up*.06f,new Color(.25f,.9f,1f),1,0,false,"origen");
        foreach(int id in result.Visible)
        {
            if(!graph.Members.TryGetValue(id,out var edge))continue;
            Vector3 a=Position(graph.Nodes[edge.I]),b=Position(graph.Nodes[edge.J]);
            bool reverse=graph.SupportDepth.TryGetValue(edge.I,out var i)&&graph.SupportDepth.TryGetValue(edge.J,out var j)&&i<j;
            if(result.Path!=null)
                for(int k=0;k<result.Path.Edges.Count;k++)if(result.Path.Edges[k].Id==id){reverse=result.Path.Nodes[k]!=edge.I;break;}
            if(reverse){var swap=a;a=b;b=swap;}
            result.ById.TryGetValue(id,out var reading);float intensity=reading?.Intensity??0;
            Color color=edge.Link?new Color(.66f,.7f,.8f):edge.Type=="columna"?new Color(.95f,.69f,.32f):edge.Type=="muro_eq"?new Color(.7f,.57f,.95f):new Color(.18f,.84f,.95f);
            GetStroke(id,a,b,color,intensity,edge.Type=="viga"?2:3,edge.Link,
                result.ReceiverIds.Contains(id)?"receptor":result.Path?.Edges.Exists(e=>e.Id==id)==true?"conexión al apoyo":"respuesta");
        }
        if(showConstraints)
        {
            var touched=new HashSet<int>();foreach(int id in result.Visible)if(graph.Members.TryGetValue(id,out var e)){touched.Add(e.I);touched.Add(e.J);}
            int count=0;foreach(var e in graph.Constraints)if(touched.Contains(e.J))
            {GetStroke(e.Id,Position(graph.Nodes[e.I]),Position(graph.Nodes[e.J]),new Color(.65f,.68f,.8f,.5f),0,3,true,"MPC XY/Rz · no transferencia vertical");if(++count==12)break;}
        }
        var supportIds=new HashSet<int>();if(result.Path!=null)supportIds.Add(result.Path.Support);if(result.ReactionSupport!=0)supportIds.Add(result.ReactionSupport);
        foreach(int node in supportIds)if(graph.Nodes.TryGetValue(node,out var n))
        {
            var p=Position(n)+Vector3.up*.15f;Color color=new Color(.32f,.95f,.62f);
            GetStroke(0,p-Vector3.right*.5f,p+Vector3.right*.5f,color,1,4,false,"apoyo real N"+node);
            GetStroke(0,p-Vector3.forward*.5f,p+Vector3.forward*.5f,color,1,4,false,"apoyo real N"+node);
            GetStroke(0,p-Vector3.up*.15f,p+Vector3.up*.9f,color,1,4,false,"reacción/apoyo");
        }
        var slab=graph.Slabs[result.Frame.Slab];float z=slab.z+.025f;var corners=new[]{new Vector3(slab.x0,z,slab.y0),new Vector3(slab.x1,z,slab.y0),new Vector3(slab.x1,z,slab.y1),new Vector3(slab.x0,z,slab.y1)};
        for(int k=0;k<4;k++)GetStroke(0,corners[k],corners[(k+1)%4],new Color(.2f,.8f,1f,.65f),.1f,1,false,"losa origen");
        var vertices=new List<Vector3>();var triangles=new List<int>();var colors=new List<Color>();var sides=new HashSet<string>();
        foreach(var receiver in result.Frame.Receivers??Array.Empty<IncrementalResponseNetwork.Receiver>())sides.Add(receiver.Side);
        foreach(string side in sides)foreach(var polygon in XrayTributaryRegion.Build(slab,side))
        {
            int start=vertices.Count;foreach(var p in polygon){vertices.Add(root.transform.InverseTransformPoint(new Vector3(p.x,z+.01f,p.y)));colors.Add(new Color(.18f,.88f,.92f,.22f));}
            for(int k=1;k<polygon.Count-1;k++){triangles.Add(start);triangles.Add(start+k);triangles.Add(start+k+1);}
        }
        regionMesh.Clear();regionMesh.SetVertices(vertices);regionMesh.SetColors(colors);regionMesh.SetTriangles(triangles,0);regionMesh.RecalculateBounds();
        for(int k=used;k<strokes.Count;k++)strokes[k].Object.SetActive(false);
    }
    public void Animate(float time,int stage,int selected,bool inside)
    {
        if(decorationRenderer!=null)decorationRenderer.enabled=!inside;
        regionRenderer.enabled=stage>=1;
        for(int k=0;k<used;k++)
        {
            var s=strokes[k];s.Intensity=Mathf.Lerp(s.Intensity,s.Target,Mathf.Clamp01(Time.unscaledDeltaTime*7));
            bool highlighted=s.Id>0&&s.Id==selected;
            var color=highlighted?new Color(1f,.87f,.24f):s.Color;
            if(s.Id>0&&!s.Dashed)color.a=.4f+.6f*s.Intensity;
            s.Line.startColor=s.Line.endColor=color;
            float width=s.Dashed ? .045f : .055f+.15f*s.Intensity;if(highlighted)width=.22f;
            s.Line.startWidth=s.Line.endWidth=width;
            s.Line.GetPropertyBlock(block);block.SetFloat("_Pulse",Mathf.Repeat(time*.28f-k*.061f,1));
            block.SetFloat("_Dashed",s.Dashed?1:0);block.SetFloat("_Stage",stage>=s.Stage?1:0);s.Line.SetPropertyBlock(block);
        }
    }
    public void Dispose()
    {
        if(root!=null)root.SetActive(false);
        foreach(var pair in hidden)if(pair.Key!=null)pair.Key.enabled=pair.Value;hidden.Clear();
        Release(structuralMesh);Release(decorationMesh);Release(regionMesh);Release(pulseMaterial);Release(contextMaterial);Release(root);root=null;
        strokes.Clear();
    }
    private static void Release(UnityEngine.Object obj){if(obj==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(obj);else UnityEngine.Object.DestroyImmediate(obj);}
}

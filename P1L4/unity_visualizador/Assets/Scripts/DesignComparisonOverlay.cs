using System;
using System.Collections.Generic;
using UnityEngine;

// Batched wireframes: two calculated endpoints + explicitly visual interpolation.
public sealed class DesignComparisonOverlay : IDisposable
{
    private GameObject root;
    private Material material;
    private Mesh oldMesh,newMesh,blendMesh;
    private LineRenderer oldCurve,newCurve,blendCurve;
    private readonly Dictionary<Renderer,MaterialPropertyBlock> saved=new Dictionary<Renderer,MaterialPropertyBlock>();
    private MaterialPropertyBlock block;
    private int revision=-1,component=-1;
    private float blend=-1,scale=-1;
    private string key;
    private bool paint,capacity;
    private Vector3[] wireBefore,wireAfter,wireBlend;
    private sealed class SectionMorph {public Transform transform;public Vector3 original;public float widthRatio,heightRatio;}
    private readonly List<SectionMorph> sections=new List<SectionMorph>();

    public void Show(Transform parent,ElementSelectable selected,float amount,float visualScale,int forceComponent,bool effects,bool useCapacity)
    {
        if(!DesignComparisonSession.Ready||selected==null)return;
        if(root==null)
        {
            DesignComparisonSession.Rendering=true;
            parent.GetComponent<StructuralDemandRadar>()?.SuspendForComparison();
            root=new GameObject("Duelo_diseno_ANTES_DESPUES");root.hideFlags=HideFlags.DontSave;
            root.transform.SetParent(parent,false);
            var shader=Resources.Load<Shader>("DiaphragmOverlay");material=new Material(shader!=null?shader:Shader.Find("Sprites/Default"));
            oldMesh=MeshObject("ANTES_calculado",new Color(.15f,.85f,1f,.3f));
            newMesh=MeshObject("DESPUES_calculado",new Color(1f,.3f,.75f,.45f));
            blendMesh=MeshObject("Transicion_solo_grafica",new Color(1f,.84f,.2f,.65f));
            oldCurve=CurveObject("Diagrama_ANTES",new Color(.15f,.85f,1f,.7f),.055f);
            newCurve=CurveObject("Diagrama_DESPUES",new Color(1f,.3f,.75f,.8f),.055f);
            blendCurve=CurveObject("Diagrama_interpolado",new Color(1f,.84f,.2f),.095f);
            block=new MaterialPropertyBlock();
            foreach(var element in parent.GetComponentsInChildren<ElementSelectable>(true))
            {
                if(element.isWall||element.data==null)continue;
                string id=DesignComparisonSnapshot.Key(element.data);
                if(!DesignComparisonSession.Before.Members.TryGetValue(id,out var oldMember)||!DesignComparisonSession.After.Members.TryGetValue(id,out var newMember))continue;
                if(newMember.width_m<=0||newMember.height_m<=0 || Math.Abs(oldMember.width_m-newMember.width_m)+Math.Abs(oldMember.height_m-newMember.height_m)<1e-6)continue;
                sections.Add(new SectionMorph{transform=element.transform,original=element.transform.localScale,
                    widthRatio=oldMember.width_m/newMember.width_m,heightRatio=oldMember.height_m/newMember.height_m});
            }
        }
        string target=selected.isWall?"W_"+selected.wallId:DesignComparisonSnapshot.Key(selected.data);
        bool changed=revision!=DesignComparisonSession.Revision || key!=target || component!=forceComponent;
        bool wireChanged=changed || Math.Abs(scale-visualScale)>.001f;
        foreach(var section in sections)if(section.transform!=null)
            section.transform.localScale=new Vector3(section.original.x*Mathf.Lerp(section.widthRatio,1,amount),section.original.y,
                section.original.z*Mathf.Lerp(section.heightRatio,1,amount));
        if(wireChanged||Math.Abs(blend-amount)>.0001f)BuildWires(amount,visualScale,wireChanged);
        if(!selected.isWall && (wireChanged||Math.Abs(blend-amount)>.0001f))BuildDiagram(target,forceComponent,amount);
        oldCurve.gameObject.SetActive(!selected.isWall);newCurve.gameObject.SetActive(!selected.isWall);blendCurve.gameObject.SetActive(!selected.isWall);
        if(changed||effects!=paint||useCapacity!=capacity)
        {
            RestoreColors();
            if(effects)Paint(parent,selected,useCapacity);
        }
        revision=DesignComparisonSession.Revision;key=target;component=forceComponent;blend=amount;scale=visualScale;paint=effects;capacity=useCapacity;
    }
    private Mesh MeshObject(string name,Color color)
    {
        var go=new GameObject(name);go.transform.SetParent(root.transform,false);
        var mesh=new Mesh{name=name};mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial=material;return mesh;
    }
    private LineRenderer CurveObject(string name,Color color,float width)
    {
        var go=new GameObject(name);go.transform.SetParent(root.transform,false);var line=go.AddComponent<LineRenderer>();
        line.sharedMaterial=material;line.positionCount=61;line.startWidth=line.endWidth=width;line.startColor=line.endColor=color;line.useWorldSpace=true;
        return line;
    }
    private void BuildWires(float amount,float amplification,bool endpoints)
    {
        if(!endpoints && wireBefore!=null)
        {
            for(int i=0;i<wireBlend.Length;i++)wireBlend[i]=Vector3.Lerp(wireBefore[i],wireAfter[i],amount);
            blendMesh.vertices=wireBlend;blendMesh.RecalculateBounds();return;
        }
        var before=DesignComparisonSession.Before;var after=DesignComparisonSession.After;var loads=DesignComparisonSession.Loads;
        var a=new List<Vector3>();var b=new List<Vector3>();var blended=new List<Vector3>();
        foreach(var pair in before.Members)
        {
            var e=pair.Value;if(e.type!="viga"&&e.type!="columna")continue;
            if(!after.Members.ContainsKey(pair.Key)||!before.TryDisplacement(e.nodeI,loads,out _)||!before.TryDisplacement(e.nodeJ,loads,out _)||
                !after.TryDisplacement(e.nodeI,loads,out _)||!after.TryDisplacement(e.nodeJ,loads,out _))continue;
            for(int j=0;j<5;j++)for(int k=0;k<2;k++)
            {
                float t=(j+k)/5f;var p=before.MemberPoint(pair.Key,loads,t,amplification);var q=after.MemberPoint(pair.Key,loads,t,amplification);
                a.Add(p);b.Add(q);blended.Add(Vector3.Lerp(p,q,amount));
            }
        }
        wireBefore=a.ToArray();wireAfter=b.ToArray();wireBlend=blended.ToArray();
        if(endpoints){Write(oldMesh,a,new Color(.15f,.85f,1f,.3f));Write(newMesh,b,new Color(1f,.3f,.75f,.45f));}
        Write(blendMesh,blended,new Color(1f,.84f,.2f,.65f));
    }
    private static void Write(Mesh mesh,List<Vector3> points,Color color)
    {
        var indices=new int[points.Count];var colors=new Color[points.Count];for(int i=0;i<indices.Length;i++){indices[i]=i;colors[i]=color;}
        mesh.Clear();mesh.SetVertices(points);mesh.colors=colors;mesh.SetIndices(indices,MeshTopology.Lines,0);mesh.RecalculateBounds();
    }
    private void BuildDiagram(string target,int comp,float amount)
    {
        var a=DesignComparisonSession.Before;var b=DesignComparisonSession.After;var loads=DesignComparisonSession.Loads;
        var fa=a.Forces(target,loads);var fb=b.Forces(target,loads);
        if(!FrameForces.IsValid(fa)||!FrameForces.IsValid(fb)||!a.TryFrame(target,out var e,out var frame))return;
        int[] indices={0,1,2,4,5};int index=indices[comp];float maximum=.0001f;
        var valuesA=new float[61];var valuesB=new float[61];
        for(int i=0;i<=60;i++)
        {
            valuesA[i]=FrameForces.Evaluate(fa,frame.Length,i/60f).Component(index);
            valuesB[i]=FrameForces.Evaluate(fb,frame.Length,i/60f).Component(index);
            maximum=Math.Max(maximum,Math.Max(Math.Abs(valuesA[i]),Math.Abs(valuesB[i])));
        }
        double[] direction=(index==2||index==4)?frame.Z:frame.Y;var offset=new Vector3((float)direction[0],(float)direction[2],(float)direction[1]);
        var start=a.Position(a.Nodes[e.nodeI]);var end=a.Position(a.Nodes[e.nodeJ]);
        for(int i=0;i<=60;i++)
        {
            var p=Vector3.Lerp(start,end,i/60f);
            oldCurve.SetPosition(i,p+offset*(valuesA[i]/maximum*2));
            newCurve.SetPosition(i,p+offset*(valuesB[i]/maximum*2));
            blendCurve.SetPosition(i,p+offset*(Mathf.Lerp(valuesA[i],valuesB[i],amount)/maximum*2));
        }
    }
    private void Paint(Transform parent,ElementSelectable selected,bool useCapacity)
    {
        foreach(var element in parent.GetComponentsInChildren<ElementSelectable>(true))
        {
            if(element.isWall||element.data==null||(element.data.type!="viga"&&element.data.type!="columna"))continue;
            var renderer=element.GetComponent<Renderer>();if(renderer==null)continue;
            var savedBlock=new MaterialPropertyBlock();renderer.GetPropertyBlock(savedBlock);saved[renderer]=savedBlock;
            string target=DesignComparisonSnapshot.Key(element.data);
            var a=DesignComparisonSession.Reading(target,false);var b=DesignComparisonSession.Reading(target,true);
            Color color=new Color(.45f,.5f,.55f);
            if(a.Available&&b.Available&&(!useCapacity||(a.CapacityAvailable&&b.CapacityAvailable&&!a.OutsideCurve&&!b.OutsideCurve)))
            {
                float change=useCapacity?b.DCR-a.DCR:b.Moment-a.Moment;
                float baseValue=useCapacity?a.DCR:a.Moment;
                float intensity=Mathf.Clamp01(Math.Abs(change)/Mathf.Max(.05f,Math.Abs(baseValue))/.25f);
                color=Color.Lerp(new Color(.5f,.55f,.6f),change>=0?new Color(1f,.25f,.17f):new Color(.18f,.85f,1f),intensity);
            }
            if(element==selected)color=Color.yellow;
            renderer.GetPropertyBlock(block);block.SetColor("_Color",color);block.SetColor("_BaseColor",color);renderer.SetPropertyBlock(block);
        }
    }
    private void RestoreColors(){foreach(var pair in saved)if(pair.Key!=null)pair.Key.SetPropertyBlock(pair.Value);saved.Clear();}
    public void Dispose()
    {
        RestoreColors();Release(oldMesh);Release(newMesh);Release(blendMesh);Release(material);Release(root);root=null;
        foreach(var section in sections)if(section.transform!=null)section.transform.localScale=section.original;
        sections.Clear();
        DesignComparisonSession.Rendering=false;revision=-1;
        wireBefore=wireAfter=wireBlend=null;
    }
    private static void Release(UnityEngine.Object obj){if(obj==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(obj);else UnityEngine.Object.DestroyImmediate(obj);}
}

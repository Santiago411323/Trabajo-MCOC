using UnityEngine;

// Schematic surface traces, driven by threshold history, not constitutive damage.
// No random failure, fracture, debris or modification of the FE geometry/stiffness.
public sealed class SeismicCrackVisual
{
    private GameObject root;
    private LineRenderer[] cracks;
    private LineRenderer outline, limitBand;
    private Material material;
    private int lastFrame=-1;
    private float[] growth;
    public void Replay() { if(growth!=null) System.Array.Clear(growth,0,growth.Length); }
    public void ConfigureRendering(int layer,Shader shader)
    {
        if(root==null)return;
        foreach(Transform t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
        if(shader!=null && material!=null)material.shader=shader;
    }
    public void Clear()
    {
        if(root != null) {if(Application.isPlaying) Object.Destroy(root);else Object.DestroyImmediate(root);}
        if(material != null) {if(Application.isPlaying) Object.Destroy(material);else Object.DestroyImmediate(material);}
        root=null;material=null;cracks=null;lastFrame=-1;growth=null;
    }
    private LineRenderer Line(string name,Color color,float width,int count)
    {
        var obj=new GameObject(name);obj.transform.SetParent(root.transform,false);
        var line=obj.AddComponent<LineRenderer>();line.sharedMaterial=material;
        line.useWorldSpace=true;line.positionCount=count;line.startColor=line.endColor=color;
        line.startWidth=line.endWidth=width;
        line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
        return line;
    }
    public void Update(Transform bar,SeismicDamageHistory history,int frame,float width,float height,bool visible)
    {
        if(!visible || bar == null || history == null)
        { if(root != null) root.SetActive(false);return; }
        if(root == null)
        {
            root=new GameObject("Fisuracion_ilustrativa_elemento_seleccionado");
            material=new Material(Shader.Find("Sprites/Default"));
            float stroke=Mathf.Clamp(Mathf.Min(width,height)*.022f,.006f,.028f);
            cracks=new LineRenderer[SeismicDamageHistory.CrackSlots];
            growth=new float[cracks.Length];
            for(int i=0;i<cracks.Length;i++) cracks[i]=Line("Fisura_"+i,new Color(.06f,.025f,.02f),stroke,7);
            outline=Line("Seleccion",new Color(1f,.8f,.1f),stroke*.65f,16);
            limitBand=Line("Umbral_PM_no_fractura",new Color(1f,.23f,.13f),stroke*1.4f,5);
        }
        root.SetActive(true);
        if(frame<lastFrame) Replay(); // A backward seek cannot retain a future event.
        lastFrame=frame;
        for(int slot=0;slot<cracks.Length;slot++)
        {
            var demand=history.CrackAt(frame,slot);
            LineRenderer line=cracks[slot];
            line.enabled=demand.Ratio>1.000001f;
            if(!line.enabled) {growth[slot]=0;continue;}
            growth[slot]=Mathf.MoveTowards(growth[slot],1,Time.unscaledDeltaTime/1.5f);
            float eased=Mathf.SmoothStep(0,1,growth[slot]);
            int station=slot/4,face=slot%4;
            float axial=(station/(float)(SeismicDamageHistory.CrackStations-1)-.5f)*.99f;
            float severity=Mathf.Clamp01((demand.Ratio-1)/3);
            float span=demand.Depth*(.08f+.92f*severity)*eased;
            for(int j=0;j<7;j++)
            {
                float progress=j/6f;
                float transverse=demand.StartEdge*(.5f-span*progress);
                // Deterministic decorative zigzag. Location/activation come from demand.
                float wobble=Mathf.Sin(slot*1.7f+j*2.2f)*.006f*progress;
                Vector3 point=face<2 ? new Vector3(transverse,axial+wobble,face==0 ? -.503f : .503f) :
                    new Vector3(face==2 ? .503f : -.503f,axial+wobble,transverse);
                line.SetPosition(j,bar.TransformPoint(point));
            }
        }
        Vector3[] corners={new Vector3(-.505f,-.505f,-.505f),new Vector3(.505f,-.505f,-.505f),
            new Vector3(.505f,.505f,-.505f),new Vector3(-.505f,.505f,-.505f),
            new Vector3(-.505f,-.505f,.505f),new Vector3(.505f,-.505f,.505f),
            new Vector3(.505f,.505f,.505f),new Vector3(-.505f,.505f,.505f)};
        int[] path={0,1,2,3,0,4,5,1,5,6,2,6,7,3,7,4};
        for(int k=0;k<path.Length;k++) outline.SetPosition(k,bar.TransformPoint(corners[path[k]]));
        int peak=history.PeakDCRFrame[frame];
        var sample=history.Samples[peak];
        limitBand.enabled=history.FirstExceeded>=0 && frame>=history.FirstExceeded;
        if(limitBand.enabled)
        {
            float y=(float)sample.Station-.5f;
            Vector3[] ring={new Vector3(-.51f,y,-.51f),new Vector3(.51f,y,-.51f),
                new Vector3(.51f,y,.51f),new Vector3(-.51f,y,.51f),new Vector3(-.51f,y,-.51f)};
            for(int k=0;k<ring.Length;k++) limitBand.SetPosition(k,bar.TransformPoint(ring[k]));
        }
    }
}

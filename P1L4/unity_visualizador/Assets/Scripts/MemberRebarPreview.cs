using System;
using System.Collections.Generic;
using UnityEngine;

// Cámara aislada en RenderTexture; no modifica el elemento ni sus resultados.
public sealed class MemberRebarPreview : IDisposable
{
    private const int Layer=31, Rings=33;
    private GameObject root, member;
    private Camera camera;
    private RenderTexture texture;
    private Mesh concrete;
    private readonly List<Material> materials=new List<Material>();
    private readonly List<LineRenderer> bars=new List<LineRenderer>(), stirrups=new List<LineRenderer>();
    private readonly List<LineRenderer> cracks=new List<LineRenderer>();
    private readonly List<Vector2> barCoordinates=new List<Vector2>();
    private LineRenderer cut;
    private Vector3[] vertices;
    private Color[] concreteColors;
    private SectionMaterialData section;
    private float length;
    private string key;
    public Texture Texture => texture;
    public float Length => length;

    public void Bind(ElementSelectable element,SectionMaterialData material)
    {
        float l=Vector3.Distance(element.startPoint,element.endPoint);
        string next=element.data.id+":"+element.data.type+":"+JsonUtility.ToJson(material)+":"+l;
        if(next==key) return;
        Dispose(); key=next; section=material; length=Mathf.Max(.01f,l);
        root=new GameObject("Vista aislada de armadura") {hideFlags=HideFlags.HideAndDontSave,layer=Layer};
        root.transform.position=new Vector3(10000,10000,10000);
        member=Child("Elemento seleccionado");
        if(element.data!=null && element.data.type=="columna") member.transform.localRotation=Quaternion.Euler(0,0,90);
        var camObject=Child("Cámara de resultados");camera=camObject.AddComponent<Camera>();
        texture=new RenderTexture(800,450,24,RenderTextureFormat.ARGB32){hideFlags=HideFlags.HideAndDontSave};
        texture.Create();camera.targetTexture=texture;camera.cullingMask=1<<Layer;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.055f,.08f);
        camera.nearClipPlane=.01f;camera.farClipPlane=200;camera.fieldOfView=35;
        var body=Child("Hormigón transparente",member.transform);
        concrete=new Mesh {name="Sección longitudinal deformable",hideFlags=HideFlags.HideAndDontSave};
        vertices=new Vector3[Rings*4];concreteColors=new Color[vertices.Length];var triangles=new List<int>();
        for(int i=0;i<Rings-1;i++) for(int j=0;j<4;j++)
        {int a=i*4+j,b=i*4+(j+1)%4,c=b+4,d=a+4;triangles.AddRange(new[]{a,b,c,a,c,d});}
        triangles.AddRange(new[]{0,2,1,0,3,2,(Rings-1)*4,(Rings-1)*4+1,(Rings-1)*4+2,(Rings-1)*4,(Rings-1)*4+2,(Rings-1)*4+3});
        concrete.vertices=vertices;concrete.triangles=triangles.ToArray();
        body.AddComponent<MeshFilter>().sharedMesh=concrete;
        body.AddComponent<MeshRenderer>().sharedMaterial=MaterialFor(Color.white,true);
        float cover=material.cover_mm/1000, left=-material.b_m/2+cover,right=material.b_m/2-cover;
        AddRow(material.topBars,material.h_m/2-cover,left,right);
        AddRow(material.bottomBars,-material.h_m/2+cover,left,right);
        for(int i=1;i<=material.sideBarsEach;i++)
        {float y=Mathf.Lerp(-material.h_m/2+cover,material.h_m/2-cover,i/(float)(material.sideBarsEach+1));AddBar(left,y);AddBar(right,y);}
        // Conserva count/spacing; el origen del tramo es ilustrativo porque no se exporta.
        for(int i=0;i<Mathf.Clamp(material.stirrupCount,0,500);i++)
            stirrups.Add(Line("Estribo doble "+i,Mathf.Max(.003f,material.stirrupDiameter_mm/1000),new Color(.8f,.83f,.87f)));
        cut=Line("Sección de lectura",.012f,Color.cyan);
        for(int i=0;i<8;i++)cracks.Add(Line("Grieta didáctica "+i,.006f,new Color(.12f,.08f,.06f)));
        UpdateGeometry(t=>Vector3.zero,0,0,null,.5f);
    }

    private GameObject Child(string name,Transform parent=null)
    {var g=new GameObject(name){layer=Layer,hideFlags=HideFlags.HideAndDontSave};g.transform.SetParent(parent??root.transform,false);return g;}
    private Material MaterialFor(Color color,bool transparent=false)
    {
        var shader=transparent?Resources.Load<Shader>("MemberPreviewConcrete"):Shader.Find("Unlit/Color");
        if(shader==null) shader=Shader.Find("Sprites/Default");
        var m=new Material(shader){hideFlags=HideFlags.HideAndDontSave,color=color};materials.Add(m);return m;
    }
    private LineRenderer Line(string name,float width,Color color)
    {
        var line=Child(name,member.transform).AddComponent<LineRenderer>();
        line.useWorldSpace=false;line.widthMultiplier=width;line.numCapVertices=6;line.numCornerVertices=4;
        line.sharedMaterial=MaterialFor(color);line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        return line;
    }
    private void AddRow(int count,float y,float left,float right)
    {for(int i=0;i<count;i++) AddBar(Mathf.Lerp(left,right,count==1?.5f:i/(float)(count-1)),y);}
    private void AddBar(float z,float y)
    {barCoordinates.Add(new Vector2(z,y));bars.Add(Line("Barra longitudinal",section.barDiameter_mm/1000,new Color(.65f,.8f,.95f)));}

    public void UpdateGeometry(Func<float,Vector3> displacement,float phi,float amplification,
                               MemberMaterialPlayback.State state,float slice,bool illustrateCracks=false)
    {
        if(root==null) return;
        Func<float,float,float,Vector3> map=(t,y,z)=>{
            float s=length*(t-.5f),k=phi*amplification;
            Vector3 center;
            if(Mathf.Abs(k)<1e-7f) center=new Vector3(s,0,0);
            else center=new Vector3(Mathf.Sin(k*s)/k,(1-Mathf.Cos(k*s))/k-(1-Mathf.Cos(k*length/2))/k,0);
            float angle=k*s;
            return center+new Vector3(-Mathf.Sin(angle)*y,Mathf.Cos(angle)*y,z)+displacement(t);
        };
        for(int i=0;i<Rings;i++)
        {
            float t=i/(float)(Rings-1);
            vertices[i*4]=map(t,-section.h_m/2,-section.b_m/2);
            vertices[i*4+1]=map(t,section.h_m/2,-section.b_m/2);
            vertices[i*4+2]=map(t,section.h_m/2,section.b_m/2);
            vertices[i*4+3]=map(t,-section.h_m/2,section.b_m/2);
            for(int j=0;j<4;j++)
            {
                float yy=j==0 || j==3?-section.h_m/2:section.h_m/2;
                float eps=state!=null?state.StrainAt(yy):0;
                concreteColors[i*4+j]=eps>0?Color.Lerp(new Color(1f,.64f,.22f,.15f),new Color(1f,.2f,.12f,.45f),Mathf.Clamp01(eps/.003f)):
                    new Color(.37f,.7f,.92f,state==null?.17f:.065f);
            }
        }
        concrete.vertices=vertices;concrete.colors=concreteColors;concrete.RecalculateNormals();concrete.RecalculateBounds();
        float epsY=section.fy_MPa/(section.Es_MPa>0?section.Es_MPa:200000);
        for(int b=0;b<bars.Count;b++)
        {
            var coord=barCoordinates[b];bars[b].positionCount=Rings;
            for(int i=0;i<Rings;i++)bars[b].SetPosition(i,map(i/(float)(Rings-1),coord.y,coord.x));
            float eps=state!=null?state.StrainAt(coord.y):0;
            Color color=state==null || Mathf.Abs(eps)<1e-9f?new Color(.65f,.8f,.95f):Mathf.Abs(eps)>=epsY?Color.red:
                eps<0?new Color(.15f,1f,.48f):new Color(1f,.56f,.15f);
            bars[b].sharedMaterial.color=color;
        }
        float cover=section.cover_mm/1000,y0=-section.h_m/2+cover,y1=-y0,z0=-section.b_m/2+cover,z1=-z0;
        float span=(stirrups.Count-1)*section.stirrupSpacing_mm/1000;
        for(int i=0;i<stirrups.Count;i++)
        {
            float t=.5f+(i*section.stirrupSpacing_mm/1000-span/2)/length;
            var line=stirrups[i]; bool inside=t>=0 && t<=1;line.gameObject.SetActive(inside);if(!inside)continue;
            var points=new List<Vector3>{map(t,y0,z0),map(t,y1,z0),map(t,y1,z1),map(t,y0,z1),map(t,y0,z0)};
            // Ramas interiores adicionales del estribo doble, conectadas en el borde.
            for(int leg=1;leg<section.stirrupLegs-1;leg++)
            {float z=Mathf.Lerp(z0,z1,leg/(float)(section.stirrupLegs-1));points.Add(map(t,y0,z));points.Add(map(t,y1,z));points.Add(map(t,y0,z));}
            line.positionCount=points.Count;line.SetPositions(points.ToArray());
        }
        cut.positionCount=5;cut.SetPositions(new[]{map(slice,-section.h_m/2,-section.b_m/2),map(slice,section.h_m/2,-section.b_m/2),map(slice,section.h_m/2,section.b_m/2),map(slice,-section.h_m/2,section.b_m/2),map(slice,-section.h_m/2,-section.b_m/2)});
        for(int i=0;i<cracks.Count;i++)
        {
            bool show=illustrateCracks && state!=null && state.Moment>state.CrackMoment;
            cracks[i].gameObject.SetActive(show);if(!show)continue;
            float t=.12f+i*.105f;
            float depth=section.h_m*Mathf.Lerp(.08f,.45f,Mathf.Clamp01(state.MaxSteel/Mathf.Max(.0001f,epsY)));
            cracks[i].positionCount=3;
            cracks[i].SetPositions(new[]{map(t,section.h_m/2,-section.b_m/2-.004f),
                map(t+.006f,section.h_m/2-depth*.5f,-section.b_m/2-.004f),
                map(t,section.h_m/2-depth,-section.b_m/2-.004f)});
        }
    }

    public void Show(float yaw,float pitch,float zoom)
    {
        if(root==null)return;root.SetActive(true);
        float distance=Mathf.Max(length,section.h_m,section.b_m)*2/Mathf.Max(.5f,zoom);
        camera.transform.localPosition=Quaternion.Euler(pitch,yaw,0)*new Vector3(0,0,-distance);
        camera.transform.LookAt(root.transform.position);
        camera.aspect=800f/450;
    }
    public void Hide(){if(root!=null)root.SetActive(false);}
    public void Dispose()
    {
        if(camera!=null){camera.enabled=false;camera.targetTexture=null;}
        if(root!=null)UnityEngine.Object.Destroy(root);
        if(texture!=null){texture.Release();UnityEngine.Object.Destroy(texture);}
        if(concrete!=null)UnityEngine.Object.Destroy(concrete);
        foreach(var m in materials)if(m!=null)UnityEngine.Object.Destroy(m);
        materials.Clear();bars.Clear();stirrups.Clear();cracks.Clear();barCoordinates.Clear();root=null;camera=null;texture=null;concrete=null;key=null;
    }
}

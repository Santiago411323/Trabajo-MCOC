using System.Collections.Generic;
using UnityEngine;

public sealed class LrfdLoadVisuals : System.IDisposable
{
    private GameObject root;
    private LrfdWeatherEnvironment atmosphere;
    private readonly List<GameObject> snow=new List<GameObject>(),water=new List<GameObject>();
    private readonly List<float> roofY=new List<float>();
    private readonly List<GameObject>[] arrows=new List<GameObject>[7];
    private readonly List<Material> materials=new List<Material>();
    private readonly List<GameObject> weights=new List<GameObject>(),people=new List<GameObject>(),workers=new List<GameObject>();
    private readonly TextMesh[] labels=new TextMesh[7];
    private ParticleSystem rainParticles,snowParticles,windParticles;
    private Texture2D particleTexture;
    private Bounds bounds;
    public float RoofArea {get;private set;}
    private static readonly Color[] colors={new Color(.65f,.65f,.8f),new Color(1f,.62f,.16f),Color.yellow,Color.white,new Color(.2f,.7f,1),new Color(.35f,1,.75f),new Color(1f,.3f,.4f)};
    public void Build(StructureViewer viewer,StructureData data)
    {
        Dispose();root=new GameObject("Laboratorio_LRFD_solo_visual"){hideFlags=HideFlags.DontSave};root.transform.SetParent(viewer.transform,false);
        for(int i=0;i<7;i++)arrows[i]=new List<GameObject>();
        bool first=true;
        foreach(var node in data.nodes??new NodeData[0])
        {Vector3 p=new Vector3(node.x,node.z,node.y);if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}
        atmosphere=new LrfdWeatherEnvironment();atmosphere.Build(root.transform,bounds);
        Material personMaterial=MaterialFor(colors[1]),workerMaterial=MaterialFor(colors[2]),skinMaterial=MaterialFor(new Color(.92f,.73f,.55f)),weightMaterial=MaterialFor(colors[0]);
        var roofs=viewer.GetComponentsInChildren<VisualFlatRoof>(true);
        int roofIndex=0;
        foreach(var roof in roofs)foreach(var piece in roof.Pieces)
        {
            if(piece==null)continue;var r=piece.GetComponent<Renderer>();if(r==null)continue;
            Bounds b=r.bounds;RoofArea+=b.size.x*b.size.z;
            GameObject s=Box("Nieve acumulada",b,Color.white),w=Box("Agua retenida",b,new Color(.13f,.52f,.87f,.5f));
            snow.Add(s);water.Add(w);roofY.Add(b.max.y+.012f);
            if(roofIndex++<30)
            {
                Vector3 p=new Vector3(b.center.x,b.max.y+1.8f,b.center.z);
                AddArrow(2,p,Vector3.down);AddArrow(3,p+Vector3.right*.3f,Vector3.down);AddArrow(4,p+Vector3.forward*.3f,Vector3.down);
                if(workers.Count<8 && b.size.x>1 && b.size.z>1)workers.Add(Person("Mantenimiento de cubierta",new Vector3(b.center.x,b.max.y,b.center.z),workerMaterial,skinMaterial));
            }
        }
        int slabIndex=0;
        foreach(var slab in data.slabs??new SlabData[0])
        {
            if(slabIndex++%4!=0 || arrows[0].Count>=35)continue;
            float x=(slab.x0+slab.x1)/2,z=(slab.y0+slab.y1)/2;
            if(!slab.Contains(x,z))continue;
            Vector3 p=new Vector3(x,slab.z+1.2f,z);AddArrow(0,p,Vector3.down);AddArrow(1,p+Vector3.right*.45f,Vector3.down);
            if(weights.Count<16)weights.Add(Primitive("Peso permanente simbólico",PrimitiveType.Cube,new Vector3(x-.4f,slab.z+.35f,z),new Vector3(.65f,.55f,.6f),weightMaterial,root.transform));
            if(people.Count<16)people.Add(Person("Ocupación del piso",new Vector3(x+.5f,slab.z+.12f,z),personMaterial,skinMaterial));
        }
        for(int i=0;i<8;i++)
        {
            Vector3 p=new Vector3(bounds.min.x-3,Mathf.Lerp(bounds.min.y,bounds.max.y,(i+1)/9f),bounds.center.z);
            AddArrow(5,p,Vector3.right);AddArrow(6,new Vector3(bounds.center.x,bounds.min.y+1,bounds.min.z+i*bounds.size.z/8),Vector3.right);
        }
        for(int type=0;type<7;type++)
        {
            var tag=new GameObject("Rótulo carga "+type){layer=2};tag.transform.SetParent(root.transform,false);
            tag.transform.position=new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,(type+1)/8f),bounds.max.y+3,bounds.min.z-2);
            var text=tag.AddComponent<TextMesh>();text.fontSize=36;text.characterSize=.23f;text.anchor=TextAnchor.MiddleCenter;text.color=colors[type];labels[type]=text;
        }
        particleTexture=new Texture2D(16,16){hideFlags=HideFlags.DontSave};
        for(int x=0;x<16;x++)for(int y=0;y<16;y++)
        {float d=Vector2.Distance(new Vector2(x,y),new Vector2(7.5f,7.5f))/7.5f;particleTexture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(1-d)));}
        particleTexture.Apply();
        rainParticles=Particles("Lluvia",false);snowParticles=Particles("Nieve",false);windParticles=Particles("Corrientes de viento",true);
        root.SetActive(false);
    }
    private GameObject Primitive(string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material,Transform parent)
    {
        var g=GameObject.CreatePrimitive(type);g.name=name;g.layer=2;g.transform.SetParent(parent,false);g.transform.position=position;g.transform.localScale=scale;
        var collider=g.GetComponent<Collider>();collider.enabled=false;Object.Destroy(collider);
        g.GetComponent<Renderer>().sharedMaterial=material;return g;
    }
    private GameObject Person(string name,Vector3 feet,Material bodyMaterial,Material skin)
    {
        var g=new GameObject(name){layer=2};g.transform.SetParent(root.transform,false);g.transform.position=feet;
        Primitive("Cuerpo",PrimitiveType.Capsule,feet+Vector3.up*.68f,new Vector3(.4f,.6f,.35f),bodyMaterial,g.transform);
        Primitive("Cabeza",PrimitiveType.Sphere,feet+Vector3.up*1.45f,Vector3.one*.3f,skin,g.transform);
        return g;
    }
    private Material MaterialFor(Color color,bool transparent=false)
    {
        var shader=Shader.Find(transparent?"Sprites/Default":"Unlit/Color")??Shader.Find("Sprites/Default");
        var m=new Material(shader){color=color,hideFlags=HideFlags.DontSave};materials.Add(m);return m;
    }
    private GameObject Box(string name,Bounds b,Color color)
    {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.layer=2;g.transform.SetParent(root.transform,false);
        g.transform.position=new Vector3(b.center.x,b.max.y,b.center.z);g.transform.localScale=new Vector3(b.size.x,.01f,b.size.z);
        var c=g.GetComponent<Collider>();c.enabled=false;Object.Destroy(c);
        g.GetComponent<Renderer>().sharedMaterial=MaterialFor(color,color.a<1);return g;
    }
    private void AddArrow(int type,Vector3 origin,Vector3 direction)
    {
        var g=new GameObject("Carga "+new[]{"D","L","Lr","S","R","W","E"}[type]);g.layer=2;g.transform.SetParent(root.transform,false);g.transform.position=origin;
        var line=g.AddComponent<LineRenderer>();line.useWorldSpace=false;line.widthMultiplier=.075f;line.numCapVertices=3;line.sharedMaterial=MaterialFor(colors[type]);
        Vector3 side=Vector3.Cross(direction,Vector3.forward).normalized*.18f;if(side.sqrMagnitude<.01f)side=Vector3.right*.18f;
        line.positionCount=5;line.SetPositions(new[]{Vector3.zero,direction,direction*.7f+side,direction,direction*.7f-side});arrows[type].Add(g);
    }
    private ParticleSystem Particles(string name,bool wind)
    {
        var g=new GameObject(name);g.transform.SetParent(root.transform,false);g.layer=2;
        g.transform.position=wind?bounds.center-Vector3.right*(bounds.size.x/2+5):new Vector3(bounds.center.x,bounds.max.y+12,bounds.center.z);
        g.transform.rotation=wind?Quaternion.LookRotation(Vector3.right):Quaternion.Euler(90,0,0);
        var p=g.AddComponent<ParticleSystem>();p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=p.main;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=1800;
        main.startLifetime=wind?6:8;main.startSpeed=wind?8:12;main.startSize=.12f;main.startColor=Color.white;
        var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=wind?new Vector3(bounds.size.z,bounds.size.y,1):new Vector3(bounds.size.x,bounds.size.z,1);
        var renderer=g.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=MaterialFor(Color.white,true);renderer.sharedMaterial.mainTexture=particleTexture;
        renderer.renderMode=name=="Nieve"?ParticleSystemRenderMode.Billboard:ParticleSystemRenderMode.Stretch;
        if(name!="Nieve"){renderer.velocityScale=.04f;renderer.lengthScale=wind?4:2;}
        if(wind)main.startColor=new Color(.35f,1,.75f,.55f);
        return p;
    }
    public void Update(bool visible,int precipitation,float snowDepth,float waterDepth,float windSpeed,float windAngle,float[] intensities,float precipitationRate,bool[] shown,bool showZero)
    {
        if(root==null)return;root.SetActive(visible);atmosphere?.Update(visible,precipitation,precipitationRate,windSpeed,windAngle);if(!visible)return;
        for(int i=0;i<snow.Count;i++)
        {
            Surface(snow[i],roofY[i],shown[3]?snowDepth:0);Surface(water[i],roofY[i]+(shown[3]?snowDepth:0),shown[4]?waterDepth:0);
        }
        for(int type=0;type<7;type++)foreach(var arrow in arrows[type])
        {
            arrow.SetActive(shown[type] && (intensities[type]>0 || showZero));
            arrow.transform.localScale=Vector3.one*Mathf.Clamp(.6f+intensities[type]*.3f,.6f,2.5f);
            arrow.GetComponent<LineRenderer>().sharedMaterial.color=intensities[type]>0?colors[type]:new Color(.32f,.37f,.42f);
            if(type==5)arrow.transform.rotation=Quaternion.Euler(0,windAngle,0);
        }
        for(int i=0;i<weights.Count;i++)weights[i].SetActive(shown[0] && i<Mathf.CeilToInt(intensities[0]*8));
        for(int i=0;i<people.Count;i++)people[i].SetActive(shown[1] && i<Mathf.CeilToInt(intensities[1]*8));
        for(int i=0;i<workers.Count;i++)workers[i].SetActive(shown[2] && i<Mathf.CeilToInt(intensities[2]*3));
        string[] names={"D · PERMANENTE","L · PERSONAS","Lr · CUBIERTA","S · NIEVE","R · AGUA","W · VIENTO","E · SISMO"};
        var camera=Camera.main;
        for(int i=0;i<7;i++)
        {
            labels[i].gameObject.SetActive(shown[i] && (showZero || intensities[i]>0));
            labels[i].text=names[i]+"\n"+intensities[i].ToString("0.##")+(i<2 || i==6?"x":" kN/m²");
            labels[i].color=intensities[i]>0?colors[i]:new Color(.55f,.58f,.61f);
            if(camera!=null)labels[i].transform.rotation=camera.transform.rotation;
        }
        SetParticles(rainParticles,shown[4] && precipitation==1 && precipitationRate>0,precipitationRate*3,12,.08f);
        SetParticles(snowParticles,shown[3] && precipitation==2 && precipitationRate>0,precipitationRate,1.8f,.2f);
        SetParticles(windParticles,shown[5] && windSpeed>0,Mathf.Max(15,windSpeed*3),Mathf.Max(1,windSpeed),.15f);
        windParticles.transform.rotation=Quaternion.Euler(0,windAngle,0)*Quaternion.LookRotation(Vector3.right);
        windParticles.transform.position=bounds.center-(Quaternion.Euler(0,windAngle,0)*Vector3.right)*(Mathf.Max(bounds.size.x,bounds.size.z)/2+5);
    }
    private static void Surface(GameObject g,float baseY,float depth)
    {g.SetActive(depth>.0001f);if(depth<=.0001f)return;var p=g.transform.position;p.y=baseY+depth/2;g.transform.position=p;var s=g.transform.localScale;s.y=depth;g.transform.localScale=s;}
    private static void SetParticles(ParticleSystem p,bool enabled,float rate,float speed,float size)
    {
        var emission=p.emission;emission.rateOverTime=rate;
        var main=p.main;main.startSpeed=speed;main.startSize=size;
        if(enabled && !p.isPlaying)p.Play();else if(!enabled && p.isPlaying)p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    public void Dispose()
    {
        atmosphere?.Dispose();atmosphere=null;
        if(root!=null){root.SetActive(false);Object.Destroy(root);}root=null;
        foreach(var m in materials)if(m!=null)Object.Destroy(m);materials.Clear();
        if(particleTexture!=null)Object.Destroy(particleTexture);particleTexture=null;snow.Clear();water.Clear();roofY.Clear();weights.Clear();people.Clear();workers.Clear();RoofArea=0;
    }
}

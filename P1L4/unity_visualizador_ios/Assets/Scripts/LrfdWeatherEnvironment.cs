using System.Collections.Generic;
using UnityEngine;

// Ambiente visual reversible. No modifica geometría ni cargas del modelo.
public sealed class LrfdWeatherEnvironment : System.IDisposable
{
    private GameObject clouds;
    private Mesh mesh;
    private Material material;
    private Camera camera;
    private Light sun;
    private Color sky,ambientSky,ambientEquator,ambientGround,fogColor;
    private CameraClearFlags clearFlags;
    private bool fog,captured;
    private FogMode fogMode;
    private float fogDensity,ambientIntensity,sunIntensity,blend;
    private Vector3 origin;
    public void Build(Transform parent,Bounds building)
    {
        Dispose();camera=Camera.main;
        if(camera!=null){sky=camera.backgroundColor;clearFlags=camera.clearFlags;}
        ambientSky=RenderSettings.ambientSkyColor;ambientEquator=RenderSettings.ambientEquatorColor;
        ambientGround=RenderSettings.ambientGroundColor;ambientIntensity=RenderSettings.ambientIntensity;
        fog=RenderSettings.fog;fogColor=RenderSettings.fogColor;fogMode=RenderSettings.fogMode;fogDensity=RenderSettings.fogDensity;
        foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if(light.type==LightType.Directional && light.enabled){sun=light;sunIntensity=sun.intensity;break;}
        captured=true;
        clouds=new GameObject("Nubes_LRFD"){layer=2,hideFlags=HideFlags.DontSave};clouds.transform.SetParent(parent,false);
        origin=new Vector3(building.center.x,building.max.y+21,building.center.z);clouds.transform.position=origin;
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var triangles=new List<int>();
        // Geometría combinada: una sola malla para las nubes, sin cientos de objetos.
        float spanX=Mathf.Max(35,building.size.x*1.45f),spanZ=Mathf.Max(35,building.size.z*1.45f);
        for(int c=0;c<12;c++)for(int puff=0;puff<6;puff++)
        {
            float angle=puff*Mathf.PI/3;
            Vector3 center=new Vector3((c%4-1.5f)*spanX/3+Mathf.Cos(angle)*4,
                Mathf.Sin(puff*1.8f+c)*1.4f,(c/4-1)*spanZ/2+Mathf.Sin(angle)*4);
            Vector3 radius=new Vector3(6+(puff%2)*2,2.4f+(puff%3)*.6f,5+(puff%2));
            int start=vertices.Count;
            for(int lat=0;lat<=8;lat++)for(int lon=0;lon<=12;lon++)
            {
                float a=lat*Mathf.PI/8,b=lon*Mathf.PI*2/12;
                var n=new Vector3(Mathf.Sin(a)*Mathf.Cos(b),Mathf.Cos(a),Mathf.Sin(a)*Mathf.Sin(b));
                vertices.Add(center+Vector3.Scale(n,radius));normals.Add(new Vector3(n.x/radius.x,n.y/radius.y,n.z/radius.z).normalized);
            }
            for(int lat=0;lat<8;lat++)for(int lon=0;lon<12;lon++)
            {int a=start+lat*13+lon,b=a+13;triangles.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
        }
        mesh=new Mesh{name="Nubosidad combinada LRFD",hideFlags=HideFlags.DontSave};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
        clouds.AddComponent<MeshFilter>().sharedMesh=mesh;
        var shader=Resources.Load<Shader>("LrfdClouds")??Shader.Find("Sprites/Default");
        material=new Material(shader){hideFlags=HideFlags.DontSave};clouds.AddComponent<MeshRenderer>().sharedMaterial=material;
        clouds.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        clouds.SetActive(false);
    }
    public void Update(bool visible,int weather,float rate,float windSpeed,float windAngle)
    {
        if(!captured)return;
        if(!visible){Restore();return;}
        float target=weather==0?0:1;
        blend=Mathf.MoveTowards(blend,target,Time.unscaledDeltaTime*.45f);
        var overcast=weather==2?new Color(.53f,.58f,.65f):new Color(.25f,.3f,.37f);
        if(camera!=null)
        {
            camera.backgroundColor=Color.Lerp(sky,overcast,blend);
            camera.clearFlags=blend>.001f?CameraClearFlags.SolidColor:clearFlags;
        }
        RenderSettings.ambientSkyColor=Color.Lerp(ambientSky,overcast,blend*.65f);
        RenderSettings.ambientEquatorColor=Color.Lerp(ambientEquator,overcast*.8f,blend*.65f);
        RenderSettings.ambientGroundColor=Color.Lerp(ambientGround,overcast*.55f,blend*.5f);
        RenderSettings.ambientIntensity=Mathf.Lerp(ambientIntensity,ambientIntensity*.65f,blend);
        if(sun!=null)sun.intensity=Mathf.Lerp(sunIntensity,sunIntensity*(weather==2?.55f:.32f),blend);
        RenderSettings.fog=blend>.05f || fog;RenderSettings.fogMode=blend>.05f?FogMode.ExponentialSquared:fogMode;
        RenderSettings.fogColor=Color.Lerp(fogColor,overcast,blend);
        RenderSettings.fogDensity=Mathf.Lerp(fogDensity,.0015f,blend);
        clouds.SetActive(blend>.01f);
        if(clouds.activeSelf)
        {
            material.color=Color.Lerp(new Color(.75f,.8f,.86f,.01f),
                weather==2?new Color(.76f,.79f,.85f,.92f):new Color(.32f,.37f,.44f,.93f),blend);
            float drift=Mathf.Sin(Time.unscaledTime*.045f)*Mathf.Min(4,1+windSpeed*.08f);
            clouds.transform.position=origin+Quaternion.Euler(0,windAngle,0)*new Vector3(drift,0,0);
        }
    }
    private void Restore()
    {
        if(!captured)return;
        if(camera!=null){camera.backgroundColor=sky;camera.clearFlags=clearFlags;}
        RenderSettings.ambientSkyColor=ambientSky;RenderSettings.ambientEquatorColor=ambientEquator;RenderSettings.ambientGroundColor=ambientGround;
        RenderSettings.ambientIntensity=ambientIntensity;RenderSettings.fog=fog;RenderSettings.fogMode=fogMode;
        RenderSettings.fogColor=fogColor;RenderSettings.fogDensity=fogDensity;
        if(sun!=null)sun.intensity=sunIntensity;
        if(clouds!=null)clouds.SetActive(false);blend=0;
    }
    public void Dispose()
    {Restore();captured=false;if(clouds!=null)Object.Destroy(clouds);if(mesh!=null)Object.Destroy(mesh);if(material!=null)Object.Destroy(material);clouds=null;mesh=null;material=null;}
}

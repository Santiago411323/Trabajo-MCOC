using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

// Mobile consumes complete OpenSees runs; it never launches Python or edits the model.
public sealed class MobileSeismicPlayback : MonoBehaviour
{
    public static MobileSeismicPlayback Instance { get; private set; }
    public static bool IsActive => Instance != null && Instance.Response != null;
    public SeismicResponseData Response { get; private set; }
    public float TimeSeconds { get; private set; }
    public float VisualScale { get; private set; } = 10f;
    public bool Playing { get; private set; }
    public bool ShowCracks { get; set; }
    public int Frame => Response == null ? 0 : Response.FrameAt(TimeSeconds);
    public string Direction { get; private set; } = "X";
    public float Intensity => intensities[intensityIndex];
    public string Status { get; private set; } = "Resultados precalculados · modelo elastico";
    public string Summary => IsActive ? "SISMO " + Direction + " · " + Intensity.ToString("0.00") + "x · t=" +
        TimeSeconds.ToString("0.00") + "/" + Response.Metadata.duration.ToString("0.00") + " s" : Status;
    private readonly float[] intensities = { .25f, .5f, 1f, 1.5f };
    private int intensityIndex = 2, cachedFrame = -1;
    private readonly Dictionary<int,float[]> forceCache = new Dictionary<int,float[]>();
    private bool expanded;
    private Vector2 scroll;
    private bool InVR => FindAnyObjectByType<StructuralVRController>()?.IsVR == true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<StructuralARController>() != null && Instance == null)
            new GameObject("Resultados sismicos precalculados movil").AddComponent<MobileSeismicPlayback>();
    }
    private void Awake() { Instance = this; }
    private void OnDestroy() { Close(); if (Instance == this) Instance = null; }
    private void Update()
    {
        if (!IsActive || !Playing) return;
        TimeSeconds = Mathf.Min(Response.Metadata.duration, TimeSeconds + Time.unscaledDeltaTime);
        if (TimeSeconds >= Response.Metadata.duration) Playing = false;
    }
    public void Toggle() { if (IsActive) Close(); else LoadCurrent(); }
    public void Close() { Playing = false; Response = null; forceCache.Clear(); cachedFrame = -1; TimeSeconds = 0; }
    public void TogglePlay() { if (IsActive) { if(TimeSeconds >= Response.Metadata.duration) Seek(0); Playing = !Playing; } }
    public void Seek(float time) { TimeSeconds = Response == null ? 0 : Mathf.Clamp(time,0,Response.Metadata.duration); }
    public void CycleDirection() { Direction = Direction == "X" ? "Y" : "X"; if(IsActive) LoadCurrent(); }
    public void CycleIntensity() { intensityIndex = (intensityIndex+1)%intensities.Length; if(IsActive) LoadCurrent(); }
    public void CycleScale() { VisualScale = VisualScale == 1 ? 10 : VisualScale == 10 ? 50 : 1; }
    public bool LoadCurrent()
    {
        Close();
        string id = "el_centro_1940_ns_" + Direction + "_i" + Mathf.RoundToInt(Intensity*100).ToString("000");
        TextAsset metadata = null, compressed = null;
        try
        {
            metadata = Resources.Load<TextAsset>("MobileSeismic/"+id);
            compressed = Resources.Load<TextAsset>("MobileSeismic/"+id+"_payload");
            if(metadata == null || compressed == null) throw new InvalidDataException("Falta el caso sismico precalculado " + id);
            var header = JsonUtility.FromJson<SeismicMetadata>(metadata.text);
            if(header == null || header.truncated || !header.complete) throw new InvalidDataException("Registro incompleto.");
            long length = checked(20L + 4L*header.stride*header.frameCount);
            if(length < 20 || length > 128L*1024*1024) throw new InvalidDataException("Tamano sismico incompatible.");
            byte[] payload = new byte[(int)length];
            using(var stream = new GZipStream(new MemoryStream(compressed.bytes),CompressionMode.Decompress))
            {
                int offset = 0;
                while(offset < payload.Length) { int read = stream.Read(payload,offset,payload.Length-offset); if(read == 0) break; offset += read; }
                if(offset != payload.Length || stream.ReadByte() != -1) throw new InvalidDataException("Binario sismico incompleto.");
            }
            TextAsset model = Resources.Load<TextAsset>("estructura_p1l4_unity");
            Response = SeismicResponseData.LoadBytes(metadata.text,payload,UnityData.Structure,SeismicResponseData.Hash(model.bytes));
            Status = "OpenSees precalculado · fuerzas totales con gravedad · sin colapso";
            return true;
        }
        catch(Exception error) { Close(); Status = error.Message; Debug.LogWarning("[Mobile seismic] " + error.Message); return false; }
        finally { if(metadata != null) Resources.UnloadAsset(metadata); if(compressed != null) Resources.UnloadAsset(compressed); }
    }
    public float[] Forces(int id)
    {
        if(!IsActive || !Response.MemberIndex.TryGetValue(id,out int member)) return null;
        if(cachedFrame != Frame) { forceCache.Clear(); cachedFrame = Frame; }
        if(!forceCache.TryGetValue(id,out float[] forces))
        {
            forces = new float[12]; for(int i=0;i<12;i++) forces[i]=Response.ReadEndForce(Frame,member,i);
            forceCache.Add(id,forces);
        }
        return forces;
    }
    public bool TrySection(int id,float t,out FrameSectionForces section)
    {
        section = default; float[] forces = Forces(id);
        if(forces == null || !UnityData.TryGetFrameGeometry(id,out var frame)) return false;
        section = FrameForces.Evaluate(forces,frame.Length,t); return true;
    }
    public Vector3 Position(int node)
    {
        int index = Response.NodeIndex[node];
        int a=Frame,b=Mathf.Min(a+1,Response.Metadata.frameCount-1);
        float blend=b==a?0:Mathf.Clamp01((TimeSeconds-Response.Time(a))/(Response.Time(b)-Response.Time(a)));
        return Response.OriginalNode(index)+Vector3.Lerp(Response.ReadNode(a,index),Response.ReadNode(b,index),blend)*VisualScale;
    }
    public static string Reinforcement(ElementData e)
    {
        SectionMaterialData m = UnityData.GetMaterial(e.sectionId);
        return m == null ? "Sin armadura exportada" : "Acero exportado: " + m.topBars + "/" + m.bottomBars +
            "/" + m.sideBarsEach + " · diam " + m.barDiameter_mm.ToString("0.#") + " mm\nAs=" + m.Ast_mm2.ToString("0.#") +
            " mm2 · estribos " + m.stirrupCount + " / " + m.stirrupDiameter_mm.ToString("0.#") + " @ " + m.stirrupSpacing_mm.ToString("0.#") + " mm";
    }
    private void OnGUI()
    {
        if(InVR) return;
        float scale=Mathf.Max(1,Mathf.Min(Screen.width,Screen.height)/390f);
        Matrix4x4 previous=GUI.matrix; GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
        Rect p=Screen.safeArea; float right=p.xMax/scale, top=(Screen.height-p.yMax)/scale;
        if(GUI.Button(new Rect(right-130,top+12,118,40),"Sismo calculado")) expanded=!expanded;
        if(expanded)
        {
            float w=Mathf.Min(430,p.width/scale-16), h=Mathf.Min(190,p.height/scale*.28f);
            Rect panel=new Rect(right-w-8,top+58,w,h);
            GUI.Box(panel,GUIContent.none); GUILayout.BeginArea(new Rect(panel.x+8,panel.y+6,w-16,h-12));
            scroll=GUILayout.BeginScrollView(scroll);
            GUILayout.Label(Summary);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(IsActive?"Cerrar sismo":"Cargar")) Toggle();
            if(GUILayout.Button(Playing?"Pausa":"Play")) TogglePlay();
            if(GUILayout.Button("Inicio")) Seek(0);
            if(GUILayout.Button("X/Y")) CycleDirection();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Intensidad "+Intensity.ToString("0.00"))) CycleIntensity();
            if(GUILayout.Button("Escala VR "+VisualScale.ToString("0"))) CycleScale();
            GUILayout.EndHorizontal();
            if(IsActive) Seek(GUILayout.HorizontalSlider(TimeSeconds,0,Response.Metadata.duration));
            GUILayout.Label("AR: esfuerzos en el elemento anclado. VR: deformada visual.\nModelo elastico; capacidades nominales. No predice colapso.",new GUIStyle(GUI.skin.label){wordWrap=true});
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
        GUI.matrix=previous;
    }
}

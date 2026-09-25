using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

// Slabs are load surfaces. The worker computes incremental FRAME responses;
// no slab stress, shell mesh, or wall FE response is synthesized.
public class MobileLoadController : MonoBehaviour
{
    public static MobileLoadController Instance { get; private set; }
    public float loadKN = 0.8f;
    public bool visible = true;
    public float speed = 1f;
    public string pythonExecutable = "";
    public bool active;
    public bool followPerson;
    private SlabData slab;
    private SlabLoadMetadata slabMetadata;
    private GameObject slabObject, person;
    private GameObject directionRoot;
    private readonly List<MobileDirectionArrow> directionArrows = new List<MobileDirectionArrow>();
    private Transform leftArm, rightArm, leftLeg, rightLeg;
    private Vector3 lastVisualPosition = new Vector3(float.NaN,float.NaN,float.NaN);
    private float walkPhase;
    private Renderer slabRenderer;
    private Color slabColor;
    private Vector2 position, destination, origin, scroll;
    private bool playing, place, chooseDestination, dragging, directionalWalking;
    private Vector2 walkDirection;
    private bool routeComplete;
    private ElementSelectable observed;
    private float nextRequest, sentAt, clock;
    private int sequence, selectionVersion, sentVersion;
    private Vector2 sentPosition;
    private float sentLoad = float.NaN;
    private Task<string> pending;
    private System.Diagnostics.Process worker;
    private string workerError = "", status = "Seleccione una losa; luego active la carga.";
    private Response response;
    private readonly Dictionary<int,float[]> forces = new Dictionary<int,float[]>();
    private readonly Dictionary<int,Vector3> displacements = new Dictionary<int,Vector3>();
    private readonly List<string> history = new List<string>();
    private readonly Dictionary<string,Peak> peaks = new Dictionary<string,Peak>();
    private readonly string[] results = { "N", "Vy", "Vz", "T", "My", "Mz", "Uz" };
    private int result;
    private string pText = "0.80", xText = "0", yText = "0";
    private string exportMessage = "";
    private float lastValue, lastMin, lastMax;
    private int critical;
    private bool hasValue;
    private bool colorMap;
    private readonly Dictionary<Renderer,Color> originalColors = new Dictionary<Renderer,Color>();
    private string lastObservation="";
    private string lastRecorded="";
    private ElementPicker picker;
    private int panelTab;
    private bool showAdvanced;
    private float displayedValue, displayedMin, displayedMax;
    private readonly List<HistoryPoint> chartHistory = new List<HistoryPoint>();
    private GUIStyle wrap, titleStyle, subtitleStyle, cardStyle, metricStyle, smallStyle;
    private GUIStyle successStatus, warningStatus, errorStatus;
    private Texture2D panelTexture, cardTexture, successTexture, warningTexture, errorTexture;
    [Serializable] private class Request { public int seq; public string slab; public float x,y,p; }
    [Serializable] public class Nodal { public int node; public float p; }
    [Serializable] public class Receiver { public int beam; public string side; public float load,area,t; }
    [Serializable] public class Response
    {
        public bool ok; public int seq; public string slab,message; public float x,y,p,error,transferred;
        public Nodal[] nodes; public Receiver[] receivers; public ElementForceRecord[] forces;
        public DisplacementRecord[] displacements;
    }
    private class Peak { public float value; public Vector2 at; public int element; }
    private class HistoryPoint
    {
        public float time,value,x,y;
        public int element,result;
        public bool hasValue;
    }
    public static Rect PanelRect() => PanelLayout.Get("MobileLoad",new Rect(330,55,500,Mathf.Min(735,Screen.height-70)));
    public bool IsPanelVisible() => visible;
    public bool IsPanelReady() => active && response != null && response.ok;
    public bool IsActiveFor(ElementSelectable e) => IsPanelReady() && e != null && e.data != null;
    public bool SameElement(ElementSelectable e) => observed == e;
    public void SetSelectedElement(ElementSelectable e) { observed=e; hasValue=false; }
    public float[] Increment(int id) => IsPanelReady() && forces.TryGetValue(id,out var f) ? f : null;
    public Vector3 IncrementDisplacement(int id) => IsPanelReady() && displacements.TryGetValue(id,out var d) ? d : Vector3.zero;
    // Read-only bridge for the professional results dashboard. Movement remains
    // owned by this controller; the dashboard only observes its current state.
    public SlabData CurrentSlab => slab;
    public SlabLoadMetadata CurrentSlabMetadata => slabMetadata;
    public Vector2 CurrentPosition => position;
    public Response CurrentResponse => response;
    public GameObject PersonObject => person;
    public ElementSelectable ObservedElement => observed;
    public bool IsMoving => playing || directionalWalking;
    public bool RouteComplete => routeComplete;
    public bool HasCurrentResults => response != null && response.ok &&
        Vector2.Distance(new Vector2(response.x,response.y),position) <= .01f && Mathf.Abs(response.p-loadKN) <= 1e-5f;
    public Vector2 CurrentWalkDirection => walkDirection;
    public string AnalysisStatus => status;
    public void PauseFromResults()
    {
        playing=false; directionalWalking=false; UpdateDirectionArrowColors();
        status="Recorrido pausado desde el panel de resultados.";
    }
    public bool MoveToRecordedPosition(string slabId,Vector2 point)
    {
        if(UnityData.Structure==null||UnityData.Structure.slabs==null)return false;
        SlabData target=null;
        foreach(var candidate in UnityData.Structure.slabs) if(candidate.id==slabId) {target=candidate;break;}
        if(slab==null||target==null||!target.Contains(point.x,point.y))return false;
        PauseFromResults();
        if(slab.id!=target.id) SwitchWalkingSlab(target);
        position=destination=point;SyncCoordinates();sentLoad=float.NaN;DrawPerson();
        status="Posición crítica restaurada en "+slab.id+".";
        return true;
    }
    public void ReplayFrom(string slabId,Vector2 point,Vector2 direction)
    {
        if(!MoveToRecordedPosition(slabId,point))return;
        routeComplete=false;
        StartDirectionalWalk(direction.sqrMagnitude>.5f?direction:Vector2.right);
    }
    public static bool CapturesPointer => Instance != null && Instance.active &&
        (Instance.place || Instance.chooseDestination || Instance.dragging || Instance.PointerOnPerson() || Instance.PointerOnDirectionArrow());
    private bool PointerOnPerson()
    {
        if(person==null||Camera.main==null||!Input.GetMouseButton(0)) return false;
        Vector3 screen=Camera.main.WorldToScreenPoint(person.transform.position+Vector3.up*.7f);
        return screen.z>0 && Vector2.Distance(new Vector2(screen.x,screen.y),Input.mousePosition)<22;
    }
    private bool PointerOnDirectionArrow() => DirectionArrowUnderPointer()!=null;
    private MobileDirectionArrow DirectionArrowUnderPointer()
    {
        if(Camera.main==null||!Input.GetMouseButton(0)) return null;
        Ray ray=Camera.main.ScreenPointToRay(Input.mousePosition);
        foreach(var hit in Physics.RaycastAll(ray,500f,~0,QueryTriggerInteraction.Ignore))
        {
            var arrow=hit.collider.GetComponent<MobileDirectionArrow>();
            if(arrow!=null) return arrow;
        }
        return null;
    }
    private void OnEnable() { Instance=this; }
    private void OnDisable()
    {
        RestoreColors();
        ClearResponse(); RestoreSlab();
        if(person!=null) Destroy(person);
        if(directionRoot!=null) Destroy(directionRoot);
        if(worker!=null) { try { worker.StandardInput.Close(); if(!worker.HasExited) worker.Kill(); } catch {} worker.Dispose(); worker=null; }
        if(Instance==this) Instance=null;
    }
    private void RestoreSlab() { if(slabRenderer!=null) slabRenderer.material.color=slabColor; }
    public void SetLoadOnSlabPanel(GameObject obj,Vector3 point)
    {
        SlabData found=null;
        foreach(var s in UnityData.Structure.slabs)
            if(obj.name=="Losa_"+s.id+"_"+s.nivel) { found=s; break; }
        if(found==null) return;
        var metadata=Resources.Load<TextAsset>("slab_load_surfaces");
        slabMetadata=null;
        if(metadata!=null)
            foreach(var row in JsonUtility.FromJson<SlabLoadCatalog>(metadata.text).slabs)
                if(row.id==found.id) {slabMetadata=row;break;}
        if(slabObject!=obj)
        {
            RestoreSlab(); slabObject=obj; slab=found;
            slabRenderer=obj.GetComponent<Renderer>(); slabColor=slabRenderer.material.color;
            slabRenderer.material.color=new Color(1,0.8f,0.1f,slabColor.a);
            selectionVersion++; playing=false; directionalWalking=false; ClearResponse();
        }
        Vector2 p=new Vector2(point.x,point.z);
        if(Contains(p)) { position=origin=destination=p; SyncCoordinates(); }
        playing=directionalWalking=false;UpdateDirectionArrowColors();
        visible=true; active=true; sentLoad=float.NaN; routeComplete=false;
        DrawPerson();
        status="Persona colocada. Elija una de las cuatro flechas para caminar por losas conectadas.";
    }
    private bool Contains(Vector2 p) => slab != null && slab.Contains(p.x,p.y);
    private bool PathValid(Vector2 a,Vector2 b) => slab != null && slab.CanMove(a.x,a.y,b.x,b.y);
    private void Move(Vector2 p)
    {
        if(!PathValid(position,p)) { playing=false; directionalWalking=false; status="Movimiento rechazado: contorno o vacio."; return; }
        position=p; SyncCoordinates();
    }

    private void StartDirectionalWalk(Vector2 direction)
    {
        if(slab==null||!active) return;
        walkDirection=direction.normalized;directionalWalking=true;playing=true;routeComplete=false;
        place=chooseDestination=dragging=false;
        status="Caminando hacia "+DirectionName(walkDirection)+" por losas conectadas.";
        UpdateDirectionArrowColors();
    }

    private void AdvanceDirectional(float distance)
    {
        float remaining=distance;
        int guard=0;
        // Small substeps prevent narrow load panels from being skipped at high speed.
        while(remaining>.00001f&&directionalWalking&&guard++<256)
        {
            float step=Mathf.Min(remaining,.01f);
            Vector2 target=position+walkDirection*step;
            if(PathValid(position,target)) {position=target;remaining-=step;continue;}
            if(!CanReachBoundaryWithoutOpening(walkDirection)) {StopAtBuildingEnd("vacio o borde no transitable");break;}
            SlabData next=SlabNavigation.FindAdjacent(slab,UnityData.Structure.slabs,target.x,target.y,walkDirection.x,walkDirection.y);
            if(next==null) {StopAtBuildingEnd("fin del edificio");break;}
            if(!next.Contains(target.x,target.y)) {StopAtBuildingEnd("conexion incompleta");break;}
            SwitchWalkingSlab(next);position=target;remaining-=step;
        }
        SyncCoordinates();
    }

    private bool CanReachBoundaryWithoutOpening(Vector2 direction)
    {
        float distance=float.MaxValue;
        if(direction.x>0) distance=(Mathf.Max(slab.x0,slab.x1)-position.x)/direction.x;
        else if(direction.x<0) distance=(Mathf.Min(slab.x0,slab.x1)-position.x)/direction.x;
        if(direction.y>0) distance=Mathf.Min(distance,(Mathf.Max(slab.y0,slab.y1)-position.y)/direction.y);
        else if(direction.y<0) distance=Mathf.Min(distance,(Mathf.Min(slab.y0,slab.y1)-position.y)/direction.y);
        Vector2 justInside=position+direction*Mathf.Max(0,distance-.002f);
        return slab.CanMove(position.x,position.y,justInside.x,justInside.y);
    }

    private void SwitchWalkingSlab(SlabData next)
    {
        string previous=slab.id;RestoreSlab();slab=next;slabObject=GameObject.Find("Losa_"+slab.id+"_"+slab.nivel);
        slabRenderer=slabObject!=null?slabObject.GetComponent<Renderer>():null;
        if(slabRenderer!=null) {slabColor=slabRenderer.material.color;slabRenderer.material.color=new Color(1,.8f,.1f,slabColor.a);}
        LoadSlabMetadata();selectionVersion++;ClearResponse();sentLoad=float.NaN;
        status=$"Cambio de losa: {previous} → {slab.id}. Actualizando resultados…";
    }

    private void StopAtBuildingEnd(string reason)
    {
        playing=false;directionalWalking=false;routeComplete=true;UpdateDirectionArrowColors();
        status=$"Recorrido terminado en {slab.id}: {reason}.";
    }

    private string DirectionName(Vector2 direction)
    {
        if(direction.x>.5f)return "+X / derecha";if(direction.x<-.5f)return "-X / izquierda";
        return direction.y>.5f?"+Y / adelante":"-Y / atrás";
    }

    private void LoadSlabMetadata()
    {
        var metadata=Resources.Load<TextAsset>("slab_load_surfaces");slabMetadata=null;
        if(metadata==null)return;
        foreach(var row in JsonUtility.FromJson<SlabLoadCatalog>(metadata.text).slabs)
            if(row.id==slab.id) {slabMetadata=row;break;}
    }
    private void SyncCoordinates()
    {
        xText=(position.x-Mathf.Min(slab.x0,slab.x1)).ToString("0.###",CultureInfo.InvariantCulture);
        yText=(position.y-Mathf.Min(slab.y0,slab.y1)).ToString("0.###",CultureInfo.InvariantCulture);
    }
    private void ClearResponse()
    {
        UnityData.MobileForces.Clear(); UnityData.MobileDisplacements.Clear(); RestoreColors();
        response=null; forces.Clear(); displacements.Clear(); hasValue=false;
        var diagrams=GetComponent<DiagramController>(); if(diagrams!=null) diagrams.Refresh();
    }
    private void Update()
    {
        if(slab==null) return;
        if(!active)
        {
            if(person!=null) person.SetActive(false);
            if(directionRoot!=null) directionRoot.SetActive(false);
            return;
        }
        clock+=Time.deltaTime;
        HandlePointer();
        if(directionalWalking)
            AdvanceDirectional(speed*Time.deltaTime);
        else if(playing)
        {
            Move(Vector2.MoveTowards(position,destination,speed*Time.deltaTime));
            if(Vector2.Distance(position,destination)<0.001f) {playing=false;UpdateDirectionArrowColors();}
        }
        DrawPerson();
        if(followPerson && Camera.main!=null)
        {
            var orbit=Camera.main.GetComponent<OrbitCamera>();
            if(orbit!=null && orbit.target!=null)
                orbit.target.position=Vector3.Lerp(orbit.target.position,person.transform.position,1-Mathf.Exp(-3*Time.deltaTime));
        }
        Poll();
        string observation=(observed?.data==null?"":observed.data.id.ToString())+"/"+result+"/"+UnityData.GetActiveLoadLabel();
        if(response!=null && observation!=lastObservation) {lastObservation=observation; Record();}
        if(pending!=null && Time.unscaledTime-nextRequest>30)
        {
            status="ERROR: tiempo de analisis excedido. Reintente."; ClearResponse();
            try {worker.Kill();} catch {} pending=null;
        }
        if(pending==null && Time.unscaledTime>=nextRequest &&
            (float.IsNaN(sentLoad)||Mathf.Abs(loadKN-sentLoad)>1e-6f||Vector2.Distance(position,sentPosition)>0.005f)) Send();
        float smooth=1-Mathf.Exp(-8*Time.unscaledDeltaTime);
        displayedValue=Mathf.Lerp(displayedValue,lastValue,smooth);
        displayedMin=Mathf.Lerp(displayedMin,lastMin,smooth);
        displayedMax=Mathf.Lerp(displayedMax,lastMax,smooth);
    }
    private void HandlePointer()
    {
        if(Input.GetKeyDown(KeyCode.Escape)) {place=chooseDestination=dragging=playing=directionalWalking=false;UpdateDirectionArrowColors();return;}
        if(Camera.main==null) return;
        if(Input.GetMouseButtonDown(0))
        {
            var selectedArrow=DirectionArrowUnderPointer();
            if(selectedArrow!=null) {StartDirectionalWalk(new Vector2(selectedArrow.x,selectedArrow.y));return;}
        }
        Vector2 mouse=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y);
        if(PanelRect().Contains(mouse)||SelectedBeamDiagramPanel.BlocksPointer()) return;
        if(picker==null) picker=FindObjectOfType<ElementPicker>();
        if(picker!=null && picker.IsMouseOverViewerGui()) return;
        if(Input.GetMouseButtonDown(0) && (Input.GetKey(KeyCode.LeftShift)||PointerOnPerson())) {dragging=true;directionalWalking=playing=false;UpdateDirectionArrowColors();}
        if(!place&&!chooseDestination&&!dragging) return;
        Ray ray=Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane plane=new Plane(Vector3.up,new Vector3(0,slab.z,0));
        if(plane.Raycast(ray,out float distance) && Input.GetMouseButton(0))
        {
            Vector3 hit=ray.GetPoint(distance); Vector2 p=new Vector2(hit.x,hit.z);
            if(chooseDestination)
            {
                if(PathValid(position,p)) { destination=p; chooseDestination=false;directionalWalking=false; status="Destino listo. PLAY para avanzar."; }
                else status="Destino o trayectoria invalidos.";
            }
            else if(Contains(p))
            {
                if(place) {position=origin=destination=p;SyncCoordinates();place=false;dragging=true;}
                else Move(p);
            }
        }
        if(Input.GetMouseButtonUp(0)) dragging=false;
    }
    private void DrawPerson()
    {
        if(person==null)
        {
            person=new GameObject("Person slab load");
            Part(PrimitiveType.Cylinder,new Vector3(0,.025f,0),new Vector3(.7f,.025f,.7f),new Color(1f,.8f,.05f));
            Part(PrimitiveType.Capsule,new Vector3(0,1.05f,0),new Vector3(.48f,.58f,.34f),new Color(.05f,.55f,1f));
            Part(PrimitiveType.Sphere,new Vector3(0,1.82f,0),Vector3.one*.38f,new Color(1f,.72f,.5f));
            GameObject hair=Part(PrimitiveType.Sphere,new Vector3(0,1.98f,-.02f),new Vector3(.39f,.17f,.39f),new Color(.15f,.07f,.03f));
            hair.transform.localScale=new Vector3(.39f,.17f,.39f);
            leftArm=Part(PrimitiveType.Capsule,new Vector3(-.38f,1.08f,0),new Vector3(.16f,.48f,.16f),new Color(1f,.72f,.5f)).transform;
            rightArm=Part(PrimitiveType.Capsule,new Vector3(.38f,1.08f,0),new Vector3(.16f,.48f,.16f),new Color(1f,.72f,.5f)).transform;
            leftLeg=Part(PrimitiveType.Capsule,new Vector3(-.18f,.42f,0),new Vector3(.19f,.55f,.19f),new Color(.08f,.1f,.22f)).transform;
            rightLeg=Part(PrimitiveType.Capsule,new Vector3(.18f,.42f,0),new Vector3(.19f,.55f,.19f),new Color(.08f,.1f,.22f)).transform;
            var arrow=new GameObject("Vertical load arrow"); arrow.transform.SetParent(person.transform,false);
            var line=arrow.AddComponent<LineRenderer>(); line.useWorldSpace=false; line.positionCount=5;
            line.SetPositions(new[]{new Vector3(.72f,2.55f,0),new Vector3(.72f,.12f,0),new Vector3(.47f,.48f,0),new Vector3(.72f,.12f,0),new Vector3(.97f,.48f,0)});
            line.startWidth=line.endWidth=.075f; line.material=new Material(Shader.Find("Sprites/Default")); line.startColor=line.endColor=Color.red;
            CreateDirectionArrows();
        }
        if(directionRoot!=null) directionRoot.SetActive(true);
        person.SetActive(true);
        Vector3 next=new Vector3(position.x,slab.z+.03f,position.y);
        Vector3 movement=float.IsNaN(lastVisualPosition.x)?Vector3.zero:next-lastVisualPosition;
        if(movement.sqrMagnitude>.000001f)
        {
            person.transform.rotation=Quaternion.LookRotation(new Vector3(movement.x,0,movement.z));
            walkPhase+=movement.magnitude*10f;
            float swing=Mathf.Sin(walkPhase)*24f;
            leftArm.localRotation=Quaternion.Euler(swing,0,0); rightArm.localRotation=Quaternion.Euler(-swing,0,0);
            leftLeg.localRotation=Quaternion.Euler(-swing,0,0); rightLeg.localRotation=Quaternion.Euler(swing,0,0);
        }
        else if(!playing)
        {
            leftArm.localRotation=rightArm.localRotation=leftLeg.localRotation=rightLeg.localRotation=Quaternion.identity;
        }
        person.transform.position=next; lastVisualPosition=next;
        if(directionRoot!=null) directionRoot.transform.position=new Vector3(position.x,slab.z+.12f,position.y);
    }

    private void CreateDirectionArrows()
    {
        directionRoot=new GameObject("Mobile walk direction controls");
        CreateDirectionArrow("+X",Vector2.right,new Color(.15f,.8f,1f));
        CreateDirectionArrow("-X",Vector2.left,new Color(.15f,.8f,1f));
        CreateDirectionArrow("+Y",Vector2.up,new Color(.15f,.8f,1f));
        CreateDirectionArrow("-Y",Vector2.down,new Color(.15f,.8f,1f));
    }

    private void CreateDirectionArrow(string label,Vector2 direction,Color color)
    {
        var root=new GameObject("Walk "+label);root.transform.SetParent(directionRoot.transform,false);
        Vector3 d=new Vector3(direction.x,0,direction.y),perp=new Vector3(-direction.y,0,direction.x);
        var line=root.AddComponent<LineRenderer>();line.useWorldSpace=false;line.positionCount=5;
        Vector3 end=d*2.15f;
        line.SetPositions(new[]{d*.9f,end,end-d*.42f+perp*.28f,end,end-d*.42f-perp*.28f});
        line.startWidth=line.endWidth=.09f;line.material=new Material(Shader.Find("Sprites/Default"));line.startColor=line.endColor=color;
        var head=GameObject.CreatePrimitive(PrimitiveType.Cube);head.name="Flecha "+label;head.transform.SetParent(root.transform,false);
        head.transform.localPosition=end;head.transform.localScale=new Vector3(.48f,.16f,.48f);
        head.GetComponent<Renderer>().material.color=color;
        var control=head.AddComponent<MobileDirectionArrow>();control.x=direction.x;control.y=direction.y;control.line=line;control.head=head.GetComponent<Renderer>();control.baseColor=color;
        directionArrows.Add(control);
        var textObject=new GameObject("Label "+label);textObject.transform.SetParent(root.transform,false);textObject.transform.localPosition=end+Vector3.up*.28f;
        var text=textObject.AddComponent<TextMesh>();text.text=label;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.characterSize=.22f;text.fontSize=42;text.color=Color.white;
        textObject.transform.rotation=Quaternion.Euler(90,0,0);
    }

    private void UpdateDirectionArrowColors()
    {
        foreach(var arrow in directionArrows)
        {
            if(arrow==null)continue;
            bool selected=directionalWalking&&Vector2.Dot(new Vector2(arrow.x,arrow.y),walkDirection)>.99f;
            Color color=selected?new Color(.25f,1f,.35f):arrow.baseColor;
            arrow.head.material.color=color;arrow.line.startColor=arrow.line.endColor=color;
        }
    }
    private GameObject Part(PrimitiveType kind,Vector3 p,Vector3 scale,Color color)
    {
        var obj=GameObject.CreatePrimitive(kind); obj.transform.SetParent(person.transform,false);
        obj.transform.localPosition=p; obj.transform.localScale=scale; Destroy(obj.GetComponent<Collider>());
        obj.GetComponent<Renderer>().material.color=color;
        return obj;
    }
    private void StartWorker()
    {
        if(worker!=null && !worker.HasExited) return;
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
        string script=Path.Combine(root,"P1L4/mobile_slab_worker.py");
        if(string.IsNullOrWhiteSpace(pythonExecutable))
        {
            string bundled=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe");
            pythonExecutable=File.Exists(bundled)?bundled:Path.Combine(root,".venv/Scripts/python.exe");
        }
        if(!File.Exists(script)||!File.Exists(pythonExecutable)) throw new IOException("Configure Python y mobile_slab_worker.py (requiere OpenSeesPy).");
        worker=new System.Diagnostics.Process(); worker.StartInfo=new System.Diagnostics.ProcessStartInfo {
            FileName=pythonExecutable, Arguments="-u \""+script+"\" \""+Path.Combine(Application.dataPath,"Resources/estructura_p1l4_unity.json")+"\"",
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
        worker.ErrorDataReceived+=(s,e)=> { if(e.Data!=null) workerError=e.Data; };
        worker.Start(); worker.BeginErrorReadLine();
    }
    private void Send()
    {
        nextRequest=Time.unscaledTime+.15f;
        try
        {
            StartWorker(); sentVersion=selectionVersion; sentPosition=position; sentLoad=loadKN; sentAt=clock;
            var request=new Request {seq=++sequence,slab=slab.id,x=position.x,y=position.y,p=loadKN};
            worker.StandardInput.WriteLine(JsonUtility.ToJson(request)); worker.StandardInput.Flush();
            pending=worker.StandardOutput.ReadLineAsync(); status="Calculando respuesta OpenSees...";
        }
        catch(Exception ex) { status="ERROR: "+ex.Message; ClearResponse(); sentLoad=loadKN; sentPosition=position; }
    }
    private void Poll()
    {
        if(pending==null||!pending.IsCompleted) return;
        var completed=pending; pending=null;
        if(sentVersion!=selectionVersion) { sentLoad=float.NaN; return; }
        try
        {
            string line=completed.GetAwaiter().GetResult();
            if(string.IsNullOrEmpty(line)) throw new IOException("Worker detenido. "+workerError);
            var r=JsonUtility.FromJson<Response>(line);
            if(r.seq!=sequence) throw new IOException("Respuesta fuera de secuencia");
            if(!r.ok) throw new IOException(r.message);
            if(r.error>Mathf.Max(1e-6f,r.p*1e-6f)||Mathf.Abs(r.transferred-r.p)>Mathf.Max(1e-6f,r.p*1e-6f))
                throw new IOException("FAIL: conservacion");
            response=r; forces.Clear(); displacements.Clear();
            UnityData.MobileForces.Clear(); UnityData.MobileDisplacements.Clear();
            foreach(var f in r.forces) {forces[f.id]=f.f;UnityData.MobileForces[f.id]=f.f;}
            foreach(var u in r.displacements) {displacements[u.node]=new Vector3(u.ux,u.uz,u.uy);UnityData.MobileDisplacements[u.node]=displacements[u.node];}
            status="PASS — respuesta global incremental OpenSees. Aproximacion nodal.";
            GetComponent<DiagramController>()?.Refresh(); Record();
        }
        catch(Exception ex) { ClearResponse(); status="ERROR: "+ex.Message; }
    }
    private float Component(ElementData e,float t)
    {
        if(result==6) return Mathf.Lerp(UnityData.GetNodeDisplacement(UnityData.ActiveCombo,e.nodeI).y,
            UnityData.GetNodeDisplacement(UnityData.ActiveCombo,e.nodeJ).y,t)*1000;
        return UnityData.TryGetSectionForces(e.id,UnityData.ActiveCombo,t,out var f)?f.Component(result):float.NaN;
    }
    private string Units => result==6?"mm":result>=3?"kN m":"kN";
    private void Record()
    {
        string recordKey=response.seq+"/"+(observed?.data==null?"":observed.data.id.ToString())+"/"+result+"/"+UnityData.GetActiveLoadLabel();
        if(recordKey==lastRecorded) return;
        lastRecorded=recordKey;
        if(observed==null||observed.data==null)
        {
            hasValue=false;
            chartHistory.Add(new HistoryPoint {time=sentAt,x=response.x,y=response.y,result=result,hasValue=false});
            history.Add(string.Join(",",sentAt.ToString("R",CultureInfo.InvariantCulture),slab.id,
                (response.x-Mathf.Min(slab.x0,slab.x1)).ToString("R",CultureInfo.InvariantCulture),
                (response.y-Mathf.Min(slab.y0,slab.y1)).ToString("R",CultureInfo.InvariantCulture),
                response.p.ToString("R",CultureInfo.InvariantCulture),"",results[result],"",Units,""));
            return;
        }
        lastValue=Component(observed.data,.5f); lastMin=float.PositiveInfinity; lastMax=float.NegativeInfinity;
        foreach(var e in UnityData.Structure.elements)
            for(int i=0;i<=10;i++)
            {
                float value=Component(e,i/10f); if(float.IsNaN(value)) continue;
                lastMin=Mathf.Min(lastMin,value); lastMax=Mathf.Max(lastMax,value);
                if(Mathf.Abs(value)>=Mathf.Max(Mathf.Abs(lastMin),Mathf.Abs(lastMax))) critical=e.id;
            }
        hasValue=!float.IsNaN(lastValue);
        bool firstChartPoint=chartHistory.Count==0;
        if(firstChartPoint || !hasValue)
        {
            displayedValue=lastValue; displayedMin=lastMin; displayedMax=lastMax;
        }
        chartHistory.Add(new HistoryPoint {time=sentAt,x=response.x,y=response.y,value=lastValue,
            element=observed.data.id,result=result,hasValue=hasValue});
        if(chartHistory.Count>800) chartHistory.RemoveRange(0,chartHistory.Count-800);
        if(colorMap) ApplyColors();
        string key=slab.id+"/"+observed.data.id+"/"+results[result]+"/"+UnityData.GetActiveLoadLabel();
        if(!peaks.TryGetValue(key,out var peak)||Mathf.Abs(lastValue)>Mathf.Abs(peak.value))
            peaks[key]=new Peak {value=lastValue,at=new Vector2(response.x,response.y),element=observed.data.id};
        history.Add(string.Join(",",sentAt.ToString("R",CultureInfo.InvariantCulture),slab.id,
            (response.x-Mathf.Min(slab.x0,slab.x1)).ToString("R",CultureInfo.InvariantCulture),
            (response.y-Mathf.Min(slab.y0,slab.y1)).ToString("R",CultureInfo.InvariantCulture),
            response.p.ToString("R",CultureInfo.InvariantCulture),observed.data.id,results[result],
            lastValue.ToString("R",CultureInfo.InvariantCulture),Units,"\""+UnityData.GetActiveLoadLabel().Replace("\"","\"\"")+"\""));
    }
    private void RestoreColors()
    {
        foreach(var pair in originalColors) if(pair.Key!=null) pair.Key.material.color=pair.Value;
        originalColors.Clear();
    }
    private void ApplyColors()
    {
        foreach(var element in FindObjectsOfType<ElementSelectable>())
        {
            if(element.data==null || element==observed) continue;
            var renderer=element.GetComponent<Renderer>(); if(renderer==null) continue;
            float value=Component(element.data,.5f); if(float.IsNaN(value)) continue;
            if(!originalColors.ContainsKey(renderer)) originalColors[renderer]=renderer.material.color;
            renderer.material.color=Color.Lerp(Color.blue,Color.red,Mathf.InverseLerp(lastMin,lastMax,value));
        }
    }
    private void OnGUI()
    {
        if(!visible) return;
        EnsureStyles();
        Rect panel=PanelLayout.Apply("MobileLoad",PanelRect());
        GUI.DrawTexture(panel,panelTexture);
        GUI.Label(new Rect(panel.x+14,panel.y+7,panel.width-28,25),"CARGA MÓVIL · LOSA",titleStyle);
        GUILayout.BeginArea(new Rect(panel.x+12,panel.y+34,panel.width-24,panel.height-44));
        scroll=GUILayout.BeginScrollView(scroll);
        GUILayout.BeginHorizontal();
        bool nextActive=GUILayout.Toggle(active," Persona + carga activa",GUILayout.Width(190));
        if(nextActive!=active) { active=nextActive; selectionVersion++; ClearResponse(); sentLoad=float.NaN; playing=directionalWalking=false;UpdateDirectionArrowColors(); }
        GUILayout.FlexibleSpace();
        GUILayout.Label(playing?"● CAMINANDO":pending!=null?"● CALCULANDO":response!=null?"● ACTUALIZADO":"● EN ESPERA",
            playing||pending!=null?warningStyle():response!=null?successStyle():smallStyle);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        MetricCard("LOSA",slab==null?"—":slab.id,slab==null?"seleccione una":slab.nivel);
        MetricCard("PERSONA",$"{loadKN:0.00} kN",active?"vertical ↓":"desactivada");
        string pos=slab==null?"—":$"{position.x-Mathf.Min(slab.x0,slab.x1):0.00}, {position.y-Mathf.Min(slab.y0,slab.y1):0.00} m";
        MetricCard("POSICIÓN",pos,playing?$"{speed:0.00} m/s":"detenida");
        GUILayout.EndHorizontal();

        panelTab=GUILayout.Toolbar(panelTab,new[]{"MOVIMIENTO","REPARTO","RESPUESTA"},GUILayout.Height(28));
        GUILayout.Space(6);
        if(slab!=null)
        {
            if(panelTab==0) DrawMovementTab();
            else if(panelTab==1) DrawTransferTab();
            else DrawResponseTab();
        }
        else
        {
            GUILayout.Space(35);
            GUILayout.Label("Haz click sobre una losa translúcida",subtitleStyle);
            GUILayout.Label("La persona aparecerá automáticamente en el punto seleccionado.",wrap);
        }

        GUILayout.Space(7);
        GUIStyle currentStatus=status.StartsWith("ERROR")?errorStyle():status.Contains("PASS")?successStyle():warningStyle();
        GUILayout.Label(status,currentStatus);
        GUILayout.EndScrollView(); GUILayout.EndArea();
        if(person!=null && person.activeSelf && Camera.main!=null)
        {
            Vector3 s=Camera.main.WorldToScreenPoint(person.transform.position+Vector3.up*2);
            if(s.z>0) GUI.Label(new Rect(s.x-65,Screen.height-s.y,160,25),$"Person ↓ P={loadKN:0.###} kN");
        }
    }

    private void DrawMovementTab()
    {
        float xmin=Mathf.Min(slab.x0,slab.x1),ymin=Mathf.Min(slab.y0,slab.y1);
        GUILayout.Label("Mover la persona",subtitleStyle);
        GUILayout.Label("Arrástrala directamente o marca un destino para verla caminar.",smallStyle);
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Carga P",GUILayout.Width(70)); pText=GUILayout.TextField(pText,GUILayout.Width(85)); GUILayout.Label("kN",GUILayout.Width(30));
        GUILayout.FlexibleSpace(); followPerson=GUILayout.Toggle(followPerson," Seguir con cámara",GUILayout.Width(145));
        GUILayout.EndHorizontal();
        if(float.TryParse(pText.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out float p)&&!float.IsInfinity(p)&&p>=0) loadKN=p;
        else GUILayout.Label("La carga debe ser un número mayor o igual a cero.",errorStyle());

        GUILayout.Space(6);
        GUILayout.Label($"X local  {position.x-xmin:0.00} m",smallStyle);
        float x=GUILayout.HorizontalSlider(position.x,xmin,Mathf.Max(slab.x0,slab.x1));
        GUILayout.Label($"Y local  {position.y-ymin:0.00} m",smallStyle);
        float y=GUILayout.HorizontalSlider(position.y,ymin,Mathf.Max(slab.y0,slab.y1));
        if(x!=position.x||y!=position.y) {playing=directionalWalking=false;UpdateDirectionArrowColors();Move(new Vector2(x,y));}
        GUILayout.BeginHorizontal();
        GUILayout.Label("X",GUILayout.Width(18));xText=GUILayout.TextField(xText,GUILayout.Width(70));
        GUILayout.Label("Y",GUILayout.Width(18));yText=GUILayout.TextField(yText,GUILayout.Width(70));
        if(GUILayout.Button("APLICAR COORDENADAS")&&TryLocalPoint(xmin,ymin,out var localPoint)) {playing=directionalWalking=false;UpdateDirectionArrowColors();Move(localPoint);}
        GUILayout.EndHorizontal();

        GUILayout.Space(7); GUILayout.Label($"Velocidad  {speed:0.00} m/s",smallStyle);
        speed=GUILayout.HorizontalSlider(speed,.05f,4);
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("COLOCAR")) {place=true;chooseDestination=false;playing=directionalWalking=false;UpdateDirectionArrowColors();}
        if(GUILayout.Button("ELEGIR DESTINO")) {chooseDestination=true;place=false;playing=directionalWalking=false;UpdateDirectionArrowColors();}
        if(GUILayout.Button(playing?"PAUSA":"▶ PLAY"))
        {
            if(playing) {playing=directionalWalking=false;UpdateDirectionArrowColors();} else {directionalWalking=false;playing=active&&PathValid(position,destination);}
        }
        if(GUILayout.Button("RESET")) {playing=directionalWalking=false;UpdateDirectionArrowColors();position=destination=origin;SyncCoordinates();sentLoad=float.NaN;}
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Direction("CAMINAR ←",Vector2.left);Direction("CAMINAR →",Vector2.right);Direction("CAMINAR ↑",Vector2.up);Direction("CAMINAR ↓",Vector2.down);
        GUILayout.EndHorizontal();

        if(directionalWalking) GUILayout.Label("Dirección continua: "+DirectionName(walkDirection)+" · se detendrá en el final conectado",successStyle());

        GUILayout.Space(8);
        GUILayout.BeginVertical(cardStyle);
        GUILayout.Label($"{slab.id} · {slab.nivel}",subtitleStyle);
        GUILayout.Label($"{Mathf.Abs(slab.x1-slab.x0):0.00} × {Mathf.Abs(slab.y1-slab.y0):0.00} m  ·  Área {Mathf.Abs((slab.x1-slab.x0)*(slab.y1-slab.y0)):0.00} m²",wrap);
        if(slabMetadata!=null)
            GUILayout.Label($"h {slabMetadata.thickness:0.00} m  ·  qG {slabMetadata.qG:0.00} kN/m²  ·  Perfil {slabMetadata.profile}",smallStyle);
        GUILayout.EndVertical();
        GUILayout.Label("TIP: haz click en una de las cuatro flechas alrededor de la persona para cruzar todas las losas conectadas.",smallStyle);
    }

    private bool TryLocalPoint(float xmin,float ymin,out Vector2 point)
    {
        point=position;
        if(!float.TryParse(xText.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out float xx)||
           !float.TryParse(yText.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out float yy)||
           float.IsNaN(xx)||float.IsInfinity(xx)||float.IsNaN(yy)||float.IsInfinity(yy)) return false;
        point=new Vector2(xmin+xx,ymin+yy); return Contains(point);
    }

    private void DrawTransferTab()
    {
        GUILayout.Label("Reparto de la carga",subtitleStyle);
        if(response==null)
        {
            GUILayout.Label("Esperando una posición válida y el análisis de OpenSees.",wrap);
            DrawPendingBar();
            return;
        }
        bool stale=Vector2.Distance(position,new Vector2(response.x,response.y))>.005f||Mathf.Abs(loadKN-response.p)>1e-6f;
        GUILayout.BeginHorizontal();
        MetricCard("APLICADA",$"{response.p:F4} kN","persona");
        MetricCard("TRANSFERIDA",$"{response.transferred:F4} kN",stale?"posición anterior":"actualizada");
        MetricCard("ERROR",$"{response.error:F6} kN",response.error<1e-6f?"✓ conserva":"revisar");
        GUILayout.EndHorizontal();
        GUILayout.Label(stale?"⏳ Calculando la posición nueva…":"✓ MOBILE LOAD CONSERVATION · PASS",
            stale?warningStyle():successStyle());

        GUILayout.Space(5); GUILayout.Label("Nodos receptores",subtitleStyle);
        float max=0; foreach(var n in response.nodes) max=Mathf.Max(max,n.p);
        foreach(var n in response.nodes) DrawLoadBar($"Nodo {n.node}",n.p,max,$"{-n.p:F4} kN ↓");
        GUILayout.Space(5); GUILayout.Label("Vigas receptoras",subtitleStyle);
        foreach(var b in response.receivers)
            DrawLoadBar($"Viga {b.beam} · {SideName(b.side)}",b.load,response.p,$"{b.load:F4} kN");

        showAdvanced=GUILayout.Toggle(showAdvanced," Ver detalle tributario y técnico");
        if(showAdvanced)
        {
            GUILayout.BeginVertical(cardStyle);
            GUILayout.Label($"Respuesta confirmada: X={response.x:0.###}, Y={response.y:0.###} m globales",smallStyle);
            if(slabMetadata!=null) foreach(var edge in slabMetadata.edges)
                GUILayout.Label($"{SideName(edge.side)} · Atrib {edge.area:0.###} m² · "+
                    (edge.beams.Length==0?"SIN RECEPTOR EN CENTRO":"vigas "+string.Join(", ",edge.beams)),smallStyle);
            GUILayout.Label("La losa es superficie de carga; no existen resultados shell.",smallStyle);
            GUILayout.EndVertical();
        }
    }

    private void DrawResponseTab()
    {
        GUILayout.Label("Respuesta global de la estructura",subtitleStyle);
        GUILayout.Label("Selecciona una viga o columna en el modelo y elige qué componente observar.",smallStyle);
        int r=GUILayout.Toolbar(result,results,GUILayout.Height(28));
        if(r!=result) {result=r;hasValue=false;if(response!=null)Record();}
        GUILayout.BeginHorizontal();
        bool nextMap=GUILayout.Toggle(colorMap," Mapa global",GUILayout.Width(125));
        if(nextMap!=colorMap) {colorMap=nextMap;if(colorMap&&hasValue)ApplyColors();else RestoreColors();}
        GUILayout.FlexibleSpace();
        GUILayout.Label("Elemento: "+(observed?.data==null?"ninguno":observed.data.elementTag),smallStyle);
        GUILayout.EndHorizontal();

        if(hasValue)
        {
            GUILayout.BeginHorizontal();
            MetricCard("VALOR ACTUAL",$"{displayedValue:0.###} {Units}",results[result]+" al centro");
            MetricCard("RANGO GLOBAL",$"{displayedMin:0.##} / {displayedMax:0.##}",Units+" · azul / rojo");
            MetricCard("CRÍTICO","E"+critical,"máximo absoluto");
            GUILayout.EndHorizontal();
            DrawSignedMeter(displayedValue,Mathf.Max(Mathf.Abs(displayedMin),Mathf.Abs(displayedMax)));
            GUILayout.Space(5);GUILayout.Label(results[result]+" durante el recorrido",subtitleStyle);
            DrawHistoryChart();
            if(slab!=null&&observed?.data!=null)
            {
                string key=slab.id+"/"+observed.data.id+"/"+results[result]+"/"+UnityData.GetActiveLoadLabel();
                if(peaks.TryGetValue(key,out var peak))
                    GUILayout.Label($"★ Máximo recorrido  {peak.value:0.###} {Units}  en  X={peak.at.x-Mathf.Min(slab.x0,slab.x1):0.00}, Y={peak.at.y-Mathf.Min(slab.y0,slab.y1):0.00} m",successStyle());
            }
        }
        else GUILayout.Label("Selecciona una viga o columna para comenzar el gráfico.",warningStyle());

        GUILayout.BeginHorizontal();
        if(GUILayout.Button("EXPORTAR CSV")) Export();
        if(GUILayout.Button("BORRAR RECORRIDO")) {history.Clear();chartHistory.Clear();peaks.Clear();lastRecorded="";}
        GUILayout.EndHorizontal();
        GUILayout.Label($"{chartHistory.Count} muestras guardadas. {exportMessage}",smallStyle);
        showAdvanced=GUILayout.Toggle(showAdvanced," Ajustes avanzados");
        if(showAdvanced)
        {
            GUILayout.Label("Python + OpenSeesPy",smallStyle);pythonExecutable=GUILayout.TextField(pythonExecutable);
            if(GUILayout.Button("REINTENTAR ANÁLISIS")) sentLoad=float.NaN;
            GUILayout.Label("Muros: respuesta móvil FE no disponible. La deformada se activa desde el viewer.",smallStyle);
        }
    }

    private void EnsureStyles()
    {
        if(wrap!=null) return;
        panelTexture=Texture(new Color(.035f,.045f,.075f,.97f));
        cardTexture=Texture(new Color(.075f,.095f,.14f,.96f));
        successTexture=Texture(new Color(.04f,.28f,.19f,.96f));
        warningTexture=Texture(new Color(.32f,.21f,.045f,.96f));
        errorTexture=Texture(new Color(.35f,.075f,.085f,.96f));
        wrap=new GUIStyle(GUI.skin.label) {wordWrap=true,fontSize=12};
        wrap.normal.textColor=new Color(.9f,.94f,1f);
        titleStyle=new GUIStyle(GUI.skin.label) {fontSize=16,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
        titleStyle.normal.textColor=new Color(.35f,.88f,1f);
        subtitleStyle=new GUIStyle(GUI.skin.label) {fontSize=13,fontStyle=FontStyle.Bold};
        subtitleStyle.normal.textColor=Color.white;
        smallStyle=new GUIStyle(wrap) {fontSize=11};smallStyle.normal.textColor=new Color(.67f,.75f,.86f);
        cardStyle=new GUIStyle(GUI.skin.box) {padding=new RectOffset(9,9,7,7),margin=new RectOffset(3,3,3,3)};
        cardStyle.normal.background=cardTexture;
        metricStyle=new GUIStyle(GUI.skin.label) {fontSize=15,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
        metricStyle.normal.textColor=Color.white;
        successStatus=StatusStyle(successTexture,new Color(.52f,1f,.72f));
        warningStatus=StatusStyle(warningTexture,new Color(1f,.84f,.38f));
        errorStatus=StatusStyle(errorTexture,new Color(1f,.58f,.62f));
    }

    private GUIStyle StatusStyle(Texture2D background,Color foreground)
    {
        var style=new GUIStyle(wrap) {padding=new RectOffset(9,9,6,6),fontStyle=FontStyle.Bold};
        style.normal.background=background;style.normal.textColor=foreground;return style;
    }
    private GUIStyle successStyle() => successStatus;
    private GUIStyle warningStyle() => warningStatus;
    private GUIStyle errorStyle() => errorStatus;
    private Texture2D Texture(Color color)
    {
        var texture=new Texture2D(1,1);texture.SetPixel(0,0,color);texture.Apply();return texture;
    }

    private void MetricCard(string label,string value,string detail)
    {
        GUILayout.BeginVertical(cardStyle,GUILayout.MinWidth(110),GUILayout.Height(67));
        GUILayout.Label(label,smallStyle);GUILayout.Label(value,metricStyle,GUILayout.Height(21));
        GUILayout.Label(detail,smallStyle);GUILayout.EndVertical();
    }

    private void DrawPendingBar()
    {
        Rect rect=GUILayoutUtility.GetRect(100,15);Color old=GUI.color;
        GUI.color=new Color(.14f,.17f,.24f);GUI.DrawTexture(rect,Texture2D.whiteTexture);
        GUI.color=new Color(.2f,.65f,1f);
        GUI.DrawTexture(new Rect(rect.x,rect.y,rect.width*(.25f+.25f*Mathf.Sin(Time.unscaledTime*3)),rect.height),Texture2D.whiteTexture);
        GUI.color=old;
    }

    private void DrawLoadBar(string label,float value,float maximum,string valueLabel)
    {
        GUILayout.BeginHorizontal();GUILayout.Label(label,smallStyle,GUILayout.Width(155));
        Rect rect=GUILayoutUtility.GetRect(80,13,GUILayout.ExpandWidth(true));
        Color old=GUI.color;GUI.color=new Color(.16f,.19f,.27f);GUI.DrawTexture(rect,Texture2D.whiteTexture);
        GUI.color=new Color(.1f,.75f,1f);GUI.DrawTexture(new Rect(rect.x,rect.y,rect.width*Mathf.Clamp01(value/Mathf.Max(maximum,.000001f)),rect.height),Texture2D.whiteTexture);
        GUI.color=old;GUILayout.Label(valueLabel,smallStyle,GUILayout.Width(82));GUILayout.EndHorizontal();
    }

    private void DrawSignedMeter(float value,float bound)
    {
        Rect rect=GUILayoutUtility.GetRect(100,18);Color old=GUI.color;
        GUI.color=new Color(.14f,.17f,.24f);GUI.DrawTexture(rect,Texture2D.whiteTexture);
        float center=rect.center.x;float width=Mathf.Abs(value)/Mathf.Max(bound,.000001f)*rect.width*.5f;
        GUI.color=value>=0?new Color(1f,.42f,.28f):new Color(.2f,.65f,1f);
        GUI.DrawTexture(value>=0?new Rect(center,rect.y,width,rect.height):new Rect(center-width,rect.y,width,rect.height),Texture2D.whiteTexture);
        GUI.color=Color.white;GUI.DrawTexture(new Rect(center-1,rect.y,2,rect.height),Texture2D.whiteTexture);GUI.color=old;
    }

    private void DrawHistoryChart()
    {
        Rect rect=GUILayoutUtility.GetRect(100,145,GUILayout.ExpandWidth(true));
        Color old=GUI.color;GUI.color=new Color(.055f,.07f,.105f);GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;
        int elementId=observed?.data==null?0:observed.data.id;
        var points=new List<HistoryPoint>();
        for(int i=Mathf.Max(0,chartHistory.Count-160);i<chartHistory.Count;i++)
        {
            var p=chartHistory[i];if(p.hasValue&&p.element==elementId&&p.result==result) points.Add(p);
        }
        if(points.Count<2) {GUI.Label(new Rect(rect.x+10,rect.y+55,rect.width-20,25),"El gráfico aparecerá cuando la persona cambie de posición.",smallStyle);return;}
        float min=points[0].value,max=min,t0=points[0].time,t1=points[points.Count-1].time;
        foreach(var p in points) {min=Mathf.Min(min,p.value);max=Mathf.Max(max,p.value);}
        if(Mathf.Abs(max-min)<1e-6f) {min-=1;max+=1;}
        if(Mathf.Abs(t1-t0)<1e-6f) t1=t0+1;
        float zero=Mathf.InverseLerp(max,min,0);
        if(zero>=0&&zero<=1) DrawGuiLine(new Vector2(rect.x,rect.y+rect.height*zero),new Vector2(rect.xMax,rect.y+rect.height*zero),new Color(.5f,.55f,.65f,.5f),1);
        Vector2 previous=Vector2.zero;
        for(int i=0;i<points.Count;i++)
        {
            float px=Mathf.Lerp(rect.x+4,rect.xMax-4,Mathf.InverseLerp(t0,t1,points[i].time));
            float py=Mathf.Lerp(rect.y+5,rect.yMax-20,Mathf.InverseLerp(max,min,points[i].value));
            Vector2 current=new Vector2(px,py);if(i>0) DrawGuiLine(previous,current,new Color(.2f,.85f,1f),2);previous=current;
        }
        GUI.color=new Color(1f,.85f,.2f);GUI.DrawTexture(new Rect(previous.x-4,previous.y-4,8,8),Texture2D.whiteTexture);GUI.color=old;
        GUI.Label(new Rect(rect.x+6,rect.y+3,rect.width-12,18),$"máx {max:0.###} · mín {min:0.###} {Units}",smallStyle);
        GUI.Label(new Rect(rect.x+6,rect.yMax-18,rect.width-12,18),$"tiempo {t0:0.0} → {t1:0.0} s",smallStyle);
    }

    private static void DrawGuiLine(Vector2 a,Vector2 b,Color color,float thickness)
    {
        if(Event.current.type!=EventType.Repaint) return;
        Vector2 delta=b-a;if(delta.sqrMagnitude<.000001f)return;
        Matrix4x4 matrix=GUI.matrix;Color old=GUI.color;GUI.color=color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,a);
        GUI.DrawTexture(new Rect(a.x,a.y-thickness*.5f,delta.magnitude,thickness),Texture2D.whiteTexture);
        GUI.matrix=matrix;GUI.color=old;
    }

    private string SideName(string side)
    {
        if(side=="bottom")return "inferior";if(side=="top")return "superior";
        if(side=="left")return "izquierda";if(side=="right")return "derecha";return side;
    }
    private void Direction(string label,Vector2 d)
    {
        if(GUILayout.Button(label) && slab!=null) StartDirectionalWalk(d);
    }
    private void Export()
    {
        try {
            string path=Path.Combine(Application.persistentDataPath,"mobile-slab-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv");
            File.WriteAllText(path,"time_s,slab,x_local_m,y_local_m,P_kN,element,result,value,units,combination\n"+string.Join("\n",history),Encoding.UTF8);
            exportMessage=path;
        } catch(Exception ex) {exportMessage=ex.Message;}
    }
}

public class MobileDirectionArrow : MonoBehaviour
{
    public float x,y;
    public Color baseColor;
    public LineRenderer line;
    public Renderer head;
}

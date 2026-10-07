using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// PC-only navigation physics. None of these objects enter OpenSees or its exports.
public sealed class DesktopWalkthrough : MonoBehaviour
{
    public static bool IsActive {get;private set;}
    public float walkingSpeed=3.5f, runningSpeed=6.5f, jumpHeight=1f, gravity=-22f;
    public CharacterController Body {get;private set;}
    public GameObject CollisionWorld {get;private set;}
    public bool Paused {get;private set;}
    public bool GazeSelectionEnabled {get;private set;}
    public ElementSelectable GazeTarget {get;private set;}
    private ElementPicker gamePicker;
    private ElementSelectable savedSelection;
    private readonly Dictionary<Collider,ElementSelectable> collisionMembers=new Dictionary<Collider,ElementSelectable>();
    public DesktopSkateController Skate {get;private set;}
    private StructureViewer viewer;
    private StructureData data;
    private Camera cameraView;
    private GameObject player;
    private Transform savedParent;
    private Vector3 savedPosition;
    private Quaternion savedRotation;
    private float savedNear,savedFov,yaw,pitch,verticalSpeed;
    private CursorLockMode savedLock;
    private bool savedCursor;
    private Vector3 startPoint;
    private readonly Dictionary<Behaviour,bool> savedBehaviours=new Dictionary<Behaviour,bool>();
    private readonly Dictionary<Collider,bool> savedColliders=new Dictionary<Collider,bool>();
    public bool Active {get;private set;}
    public float[] FloorHeights
    {
        get
        {
            if(data?.slabs==null)return new float[0];
            float lowest=data.slabs.Min(s=>s.z);
            var ids=new HashSet<int>((data.supports ?? new SupportData[0]).Select(s=>s.node));
            return data.slabs.Select(s=>s.z).Concat(data.nodes.Where(n=>ids.Contains(n.id) && n.z<lowest).Select(n=>n.z)).Distinct().OrderBy(h=>h).ToArray();
        }
    }

    public void Configure(StructureViewer owner,StructureData model){viewer=owner;data=model;}

    public void Enter(Camera suppliedCamera=null)
    {
        if(Active || data==null || Application.isMobilePlatform || SeismicPlaybackController.IsActive)return;
        cameraView=suppliedCamera!=null?suppliedCamera:Camera.main;
        if(cameraView==null)return;
        savedParent=cameraView.transform.parent;savedPosition=cameraView.transform.position;savedRotation=cameraView.transform.rotation;
        savedNear=cameraView.nearClipPlane;savedFov=cameraView.fieldOfView;
        savedLock=Cursor.lockState;savedCursor=Cursor.visible;
        viewer.SetWalkthroughView(true);
        viewer.GetComponent<StructuralXRayController>()?.RefreshContextForWalk();
        savedBehaviours.Clear();
        gamePicker=viewer.GetComponent<ElementPicker>() ?? cameraView.GetComponent<ElementPicker>();
        savedSelection=gamePicker!=null?gamePicker.Selected:null;
        GazeSelectionEnabled=false;GazeTarget=null;
        foreach(var behaviour in viewer.GetComponents<Behaviour>().Concat(cameraView.GetComponents<Behaviour>()))
        {
            if(behaviour is OrbitCamera || behaviour is ElementPicker || behaviour is ElementResultsPanel ||
                behaviour is SelectedBeamDiagramPanel || behaviour is PMPanel || behaviour is StructuralModelEditor ||
                behaviour is MobileLoadLivePanel || behaviour is MobileLoadResultsDashboard || behaviour is MobileLoadController || behaviour is SeismicPlaybackController)
            {savedBehaviours[behaviour]=behaviour.enabled;behaviour.enabled=false;}
        }
        BuildCollisionWorld();
        player=new GameObject("Personaje_recorrido_PC");
        Body=player.AddComponent<CharacterController>();Body.height=1.75f;Body.radius=.25f;
        Body.center=Vector3.up*.875f;Body.stepOffset=.42f;Body.slopeLimit=48f;Body.skinWidth=.025f;Body.minMoveDistance=0;
        cameraView.transform.SetParent(player.transform,false);cameraView.transform.localPosition=Vector3.up*1.62f;
        cameraView.transform.localRotation=Quaternion.identity;
        cameraView.nearClipPlane=.05f;cameraView.fieldOfView=70;
        Active=IsActive=true;yaw=90;pitch=0;Paused=false;player.transform.rotation=Quaternion.Euler(0,yaw,0);
        startPoint=FindExteriorSpawn();Teleport(startPoint);SetPaused(false);
        Skate=player.AddComponent<DesktopSkateController>();Skate.Configure(Body,cameraView,gravity);
    }

    private void BuildCollisionWorld()
    {
        CollisionWorld=new GameObject("Colisiones_recorrido_sin_efecto_estructural");CollisionWorld.layer=2;
        CollisionWorld.transform.SetParent(viewer.transform,false);
        // Disable original selection colliders only while walking; retain exact enabled states.
        savedColliders.Clear();
        collisionMembers.Clear();
        foreach(var collider in viewer.GetComponentsInChildren<Collider>(true))
        {savedColliders[collider]=collider.enabled;collider.enabled=false;}
        foreach(var source in viewer.GetComponentsInChildren<MeshFilter>(true))
        {
            if(source.sharedMesh==null || source.GetComponent<Renderer>()==null || source.GetComponentInParent<TextMesh>()!=null)continue;
            bool structural=source.GetComponent<ElementSelectable>()!=null;
            bool slab=source.GetComponent<SlabSelectable>()!=null;
            bool decoration=source.GetComponentInParent<VisualSiteTerrain>()!=null || source.GetComponentInParent<VisualStairs>()!=null ||
                source.GetComponentInParent<VisualFlatRoof>()!=null || source.GetComponentInParent<VisualFrameFacade>()!=null || source.GetComponentInParent<VisualStudyRoom>()!=null || source.GetComponentInParent<VisualInteriorPartitions>()!=null;
            if(!structural && !slab && !decoration)continue;
            // Leaves, lines, spectators, signs and decorative tiny details are not obstacles.
            string n=source.name;
            if(n=="Copa" || n.Contains("Linea") || n.Contains("Punto_penal") || n.Contains("Circulo") ||
                n.Contains("Barra_ambientacion") || source.GetComponentInParent<VisualFootballCrowd>()!=null)continue;
            var proxy=new GameObject("Recorrido_"+n);proxy.layer=2;proxy.transform.SetParent(CollisionWorld.transform,false);
            proxy.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);proxy.transform.localScale=source.transform.lossyScale;
            if(source.sharedMesh.name=="Cube")
            {
                var box=proxy.AddComponent<BoxCollider>();box.center=source.sharedMesh.bounds.center;box.size=source.sharedMesh.bounds.size;
            }
            else proxy.AddComponent<MeshCollider>().sharedMesh=source.sharedMesh;
            var member=source.GetComponent<ElementSelectable>();
            if(member!=null && !member.isWall && member.data!=null && (member.data.type=="viga" || member.data.type=="columna"))
                collisionMembers[proxy.GetComponent<Collider>()]=member;
        }
        // Walk at the finish level already used by the slab caps and stair landings.
        float finish=data.elements.Where(e=>e.type=="viga").Select(e=>e.height_m/2).DefaultIfEmpty(.4f).Max()+.14f;
        VisualCafe.TryLayout(data,out var cafe);
        foreach(var slab in data.slabs)
        {
            var regions=new List<Rect>{Rect.MinMaxRect(Mathf.Min(slab.x0,slab.x1),Mathf.Min(slab.y0,slab.y1),Mathf.Max(slab.x0,slab.x1),Mathf.Max(slab.y0,slab.y1))};
            // The cafeteria and its rear terrace have their own finish at Y0.
            // Do not raise those floors: the real visual door openings must remain passable.
            if(cafe!=null && Mathf.Abs(slab.z-cafe.Floor)<.01f)
            {
                Cut(regions,Rect.MinMaxRect(cafe.Left-.4f,cafe.PatioFront-.3f,cafe.Right+1.3f,cafe.Back+.3f));
                Cut(regions,Rect.MinMaxRect(cafe.RearLeft-.4f,cafe.Back,cafe.RearRight+.4f,cafe.PatioRear+2));
            }
            foreach(var rect in regions)
            {
                var floor=new GameObject("Suelo_recorrido_"+slab.id);floor.layer=2;floor.transform.SetParent(CollisionWorld.transform,false);
                floor.transform.position=new Vector3(rect.center.x,slab.z+finish-.075f,rect.center.y);
                floor.AddComponent<BoxCollider>().size=new Vector3(rect.width,.15f,rect.height);
            }
        }
        Physics.SyncTransforms();
    }

    private static void Cut(List<Rect> regions,Rect opening)
    {
        var previous=regions.ToArray();regions.Clear();
        foreach(var r in previous)
        {
            float left=Mathf.Max(r.xMin,opening.xMin),right=Mathf.Min(r.xMax,opening.xMax);
            float low=Mathf.Max(r.yMin,opening.yMin),high=Mathf.Min(r.yMax,opening.yMax);
            if(left>=right || low>=high){regions.Add(r);continue;}
            if(left>r.xMin)regions.Add(Rect.MinMaxRect(r.xMin,r.yMin,left,r.yMax));
            if(right<r.xMax)regions.Add(Rect.MinMaxRect(right,r.yMin,r.xMax,r.yMax));
            if(low>r.yMin)regions.Add(Rect.MinMaxRect(left,r.yMin,right,low));
            if(high<r.yMax)regions.Add(Rect.MinMaxRect(left,high,right,r.yMax));
        }
    }

    private Vector3 FindExteriorSpawn()
    {
        VisualCafe.TryLayout(data,out var cafe);
        Vector3 candidate=cafe!=null?new Vector3(cafe.Right+5,0,cafe.Front-5):new Vector3(15,0,-12);
        if(Physics.Raycast(candidate+Vector3.up*80,Vector3.down,out var hit,200,1<<2))candidate.y=hit.point.y+.04f;
        return candidate;
    }

    public bool GoToFloor(float height)
    {
        if(!Active)return false;
        float finish=data.elements.Where(e=>e.type=="viga").Select(e=>e.height_m/2).DefaultIfEmpty(.4f).Max()+.14f;
        foreach(var slab in data.slabs.Where(s=>Mathf.Abs(s.z-height)<.01f).OrderByDescending(s=>Mathf.Abs((s.x1-s.x0)*(s.y1-s.y0))))
            foreach(float tx in new[]{.5f,.25f,.75f})foreach(float tz in new[]{.5f,.25f,.75f})
            {
                Vector3 foot=new Vector3(Mathf.Lerp(slab.x0,slab.x1,tx),height+finish+.05f,Mathf.Lerp(slab.y0,slab.y1,tz));
                if(Physics.Raycast(foot,Vector3.down,out var surface,1.5f,1<<2))foot.y=surface.point.y+.05f;
                if(Physics.CheckCapsule(foot+Vector3.up*.28f,foot+Vector3.up*1.47f,.26f,1<<2,QueryTriggerInteraction.Ignore))continue;
                Teleport(foot);return true;
            }
        var supports=new HashSet<int>((data.supports ?? new SupportData[0]).Select(s=>s.node));
        foreach(var node in data.nodes.Where(n=>supports.Contains(n.id) && Mathf.Abs(n.z-height)<.01f))
        {
            Vector3 origin=new Vector3(node.x+2,height+1,node.y+2);
            if(!Physics.Raycast(origin,Vector3.down,out var hit,3,1<<2))continue;
            Vector3 foot=hit.point+Vector3.up*.05f;
            if(Physics.CheckCapsule(foot+Vector3.up*.28f,foot+Vector3.up*1.47f,.26f,1<<2,QueryTriggerInteraction.Ignore))continue;
            Teleport(foot);return true;
        }
        return false;
    }

    public void Teleport(Vector3 feet)
    {
        if(Body==null)return;
        Body.enabled=false;player.transform.position=feet;Body.enabled=true;verticalSpeed=0;
        if(Skate!=null)Skate.ResetMotion();Physics.SyncTransforms();
    }

    // Same physics path is used by keyboard movement and runtime verification.
    public void Step(Vector2 movement,bool jump,bool sprint,float dt)
    {
        if(!Active || Paused || Body==null)return;
        if(Skate!=null && Skate.Mounted)
        {Skate.Step(movement,jump,dt);if(player.transform.position.y<-45)Teleport(startPoint);return;}
        dt=Mathf.Min(dt,.05f);
        if(Body.isGrounded && verticalSpeed<0)verticalSpeed=-2;
        if(jump && Body.isGrounded)verticalSpeed=Mathf.Sqrt(jumpHeight*-2*gravity);
        verticalSpeed=Mathf.Max(-50,verticalSpeed+gravity*dt);
        Vector3 horizontal=player.transform.right*movement.x+player.transform.forward*movement.y;
        horizontal=Vector3.ClampMagnitude(horizontal,1)*(sprint?runningSpeed:walkingSpeed);
        CollisionFlags flags=Body.Move((horizontal+Vector3.up*verticalSpeed)*dt);
        if((flags&CollisionFlags.Above)!=0 && verticalSpeed>0)verticalSpeed=0;
        if(player.transform.position.y<-45)Teleport(startPoint);
    }

    private void Update()
    {
        if(Application.isMobilePlatform || !Application.isPlaying)return;
#if ENABLE_INPUT_SYSTEM
        var k=Keyboard.current;var mouse=Mouse.current;if(k==null)return;
        bool toggle=k.fKey.wasPressedThisFrame,escape=k.escapeKey.wasPressedThisFrame;
        Vector2 move=new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
        bool jump=k.spaceKey.wasPressedThisFrame,sprint=k.leftShiftKey.isPressed || k.rightShiftKey.isPressed,reset=k.rKey.wasPressedThisFrame;
        bool skateToggle=k.eKey.wasPressedThisFrame;
        bool kickflip=k.qKey.wasPressedThisFrame,shoveIt=k.tKey.wasPressedThisFrame;
        bool inspect=k.digit1Key.wasPressedThisFrame || k.numpad1Key.wasPressedThisFrame;
        Vector2 look=mouse!=null?mouse.delta.ReadValue()*.12f:Vector2.zero;
#else
        bool toggle=Input.GetKeyDown(KeyCode.F),escape=Input.GetKeyDown(KeyCode.Escape);
        Vector2 move=new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));
        bool jump=Input.GetKeyDown(KeyCode.Space),sprint=Input.GetKey(KeyCode.LeftShift),reset=Input.GetKeyDown(KeyCode.R);
        bool skateToggle=Input.GetKeyDown(KeyCode.E);
        bool kickflip=Input.GetKeyDown(KeyCode.Q),shoveIt=Input.GetKeyDown(KeyCode.T);
        bool inspect=Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1);
        Vector2 look=new Vector2(Input.GetAxis("Mouse X"),Input.GetAxis("Mouse Y"))*2;
#endif
        if(toggle && (Active || GUIUtility.keyboardControl==0)){if(Active)Exit();else Enter();return;}
        if(!Active)return;
        if(escape)SetPaused(!Paused);
        if(Paused)return;
        if(inspect)SetGazeSelection(!GazeSelectionEnabled);
        if(skateToggle)ToggleSkate();
        if(Skate!=null && Skate.Mounted)
        {
            if(reset)Teleport(startPoint);
            if(kickflip)Skate.RequestTrick(DesktopSkateController.Trick.Kickflip);
            else if(shoveIt)Skate.RequestTrick(DesktopSkateController.Trick.ShoveIt);
            Step(move,jump,sprint,Time.deltaTime);Skate.UpdateCamera(look,Time.deltaTime);return;
        }
        yaw+=look.x;pitch=Mathf.Clamp(pitch-look.y,-85,85);
        player.transform.rotation=Quaternion.Euler(0,yaw,0);cameraView.transform.localRotation=Quaternion.Euler(pitch,0,0);
        if(reset)Teleport(startPoint);
        Step(move,jump,sprint,Time.deltaTime);
    }

    public bool ToggleSkate()
    {
        if(!Active || Skate==null || (!Skate.Mounted && verticalSpeed>0))return false;
        if(!Skate.Toggle())return false;
        verticalSpeed=0;
        if(!Skate.Mounted)
        {
            yaw=player.transform.eulerAngles.y;pitch=0;
            cameraView.transform.localPosition=Vector3.up*1.62f;cameraView.transform.localRotation=Quaternion.identity;
        }
        return true;
    }

    public void SetGazeSelection(bool value)
    {
        if(!Active)return;
        GazeSelectionEnabled=value;GazeTarget=null;
        foreach(var item in savedBehaviours)
            if(item.Key!=null && (item.Key is ElementPicker || item.Key is ElementResultsPanel || item.Key is SelectedBeamDiagramPanel))
                item.Key.enabled=value;
        if(!value && gamePicker!=null)gamePicker.SelectElement(savedSelection,false);
    }

    private void LateUpdate(){RefreshGazeSelection();}
    public void RefreshGazeSelection()
    {
        if(!Active || !GazeSelectionEnabled || Paused || cameraView==null || gamePicker==null)return;
        GazeTarget=null;
        // Use the same nearest collision as walking: walls, glass and floors occlude inspection.
        if(!Physics.Raycast(cameraView.transform.position,cameraView.transform.forward,out var hit,50,1<<2,QueryTriggerInteraction.Ignore))return;
        if(!collisionMembers.TryGetValue(hit.collider,out var member) || !member.gameObject.activeInHierarchy)return;
        var renderer=member.GetComponent<Renderer>();if(renderer==null || !renderer.enabled)return;
        GazeTarget=member;
        if(gamePicker.Selected!=member)gamePicker.SelectElement(member,false);
    }

    public void SetPaused(bool value)
    {Paused=value;Cursor.lockState=value?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=value;}
    public void SetRestoredBehaviourState(Behaviour behaviour,bool value)
    {
        if(Active&&savedBehaviours.ContainsKey(behaviour))savedBehaviours[behaviour]=value;
        else if(behaviour!=null)behaviour.enabled=value;
    }

    private void OnApplicationFocus(bool focus){if(!focus && Active)SetPaused(true);}

    private void OnGUI()
    {
        if(Application.isMobilePlatform || !Application.isPlaying || !VisualCafe.UseDesktopLayout)return;
        if(!Active)
        {
            GUI.enabled=!SeismicPlaybackController.IsActive;
            if(GUI.Button(new Rect(Screen.width-215,Screen.height-45,200,30),"Modo juego · primera persona (F)"))Enter();
            GUI.enabled=true;return;
        }
        bool skating=Skate!=null && Skate.Mounted;
        string controls=skating?"SKATE · W impulsar · S frenar · A/D girar · Espacio ollie · Q kickflip · T shove-it · E bajarse":"WASD mover · Ratón mirar · Espacio saltar · Shift correr · E subir al skate";
        string state=skating?" · "+(Skate.Speed*3.6f).ToString("F0")+" km/h · "+Skate.Status:"";
        GUI.Box(new Rect(12,12,Mathf.Min(1000,Screen.width-24),58),controls+"\n"+(skating?"Ratón cámara · ":"")+"1 selección por mirada: "+(GazeSelectionEnabled?"ON":"OFF")+" · Esc menú / cursor · R volver al inicio · F salir"+state);
        if(GazeSelectionEnabled && !Paused)
        {
            var previous=GUI.color;GUI.color=GazeTarget!=null?Color.yellow:Color.white;
            GUI.Label(new Rect(Screen.width/2f-5,Screen.height/2f-10,18,24),"+");GUI.color=previous;
        }
        if(!Paused)return;
        float x=(Screen.width-340)/2f,y=90;
        GUI.Box(new Rect(x,y,340,250+FloorHeights.Length*30),"Modo juego · caminar / skate");
        if(GUI.Button(new Rect(x+20,y+35,300,30),"Continuar"))SetPaused(false);
        if(GUI.Button(new Rect(x+20,y+70,300,30),"Volver al inicio")){Teleport(startPoint);SetPaused(false);}
        if(GUI.Button(new Rect(x+20,y+105,300,30),skating?"Bajarse del skate (E)":"Subir al skate (E)"))
            if(ToggleSkate())SetPaused(false);
        int i=0;foreach(float height in FloorHeights)
            if(GUI.Button(new Rect(x+20,y+145+i++*30,300,26),"Ir al piso "+Mathf.RoundToInt(height/4)+" (Y="+height+")"))
                if(GoToFloor(height))SetPaused(false);
        if(GUI.Button(new Rect(x+20,y+160+i*30,300,30),"Volver al visualizador"))Exit();
    }

    public void Exit()
    {
        if(!Active)return;
        if(GazeSelectionEnabled)SetGazeSelection(false);
        collisionMembers.Clear();
        Active=IsActive=false;
        if(Skate!=null){Skate.Dispose();Skate=null;}
        if(cameraView!=null)
        {
            cameraView.transform.SetParent(savedParent,true);cameraView.transform.SetPositionAndRotation(savedPosition,savedRotation);
            cameraView.nearClipPlane=savedNear;cameraView.fieldOfView=savedFov;
        }
        foreach(var item in savedColliders)if(item.Key!=null)item.Key.enabled=item.Value;
        foreach(var item in savedBehaviours)if(item.Key!=null)item.Key.enabled=item.Value;
        if(CollisionWorld!=null){CollisionWorld.SetActive(false);Release(CollisionWorld);CollisionWorld=null;}
        if(player!=null){player.SetActive(false);Release(player);player=null;Body=null;}
        Cursor.lockState=savedLock;Cursor.visible=savedCursor;Paused=false;
        if(viewer!=null)viewer.SetWalkthroughView(false);
        if(viewer!=null)viewer.GetComponent<StructuralXRayController>()?.RefreshContextForWalk();
    }
    private static void Release(Object target){if(Application.isPlaying)Destroy(target);else DestroyImmediate(target);}
    private void OnDisable(){Exit();}
}

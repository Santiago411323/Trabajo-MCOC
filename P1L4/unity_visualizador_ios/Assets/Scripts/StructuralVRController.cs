using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

// A separate visual world consumes the same JSON. It never adds analytical loads or members.
public sealed partial class StructuralVRController : MonoBehaviour
{
    public bool IsVR { get; private set; }
    public bool IsChanging { get; private set; }
    public int FloorIndex { get; private set; }
    public float[] Floors { get; private set; }
    private GameObject world, rig;
    private Camera vrCamera;
    private StructureData data;
    private StructuralARController ar;
    private ARSession session;
    private Camera arCamera;
    private Google.XR.Cardboard.XRLoader cardboard;
    private XRLoader arLoader;
    private ScreenOrientation oldOrientation;
    private bool oldPortrait, oldPortraitUpsideDown, oldLandscapeLeft, oldLandscapeRight;
    private readonly List<Action> actions = new List<Action>();
    private readonly List<Material> materials = new List<Material>();
    private readonly List<LineRenderer> diagrams = new List<LineRenderer>();
    private TextMesh title, info;
    private Transform menu;
    private ElementSelectable selected;
    private Collider gazeTarget, activatedTarget;
    private float gazeSince;
    private string combo = "C1", message = "";
    private bool walking, environment = true;
    private int result = 4; // My
    private string savedCombo;
    private Transform directionPad;
    private LineRenderer gazeProgress;
    private TextMesh nodeILabel,nodeJLabel;
    private Vector3 movementDirection;
    private bool showDiagram=true;


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<StructuralARController>() != null && FindAnyObjectByType<StructuralVRController>() == null)
            new GameObject("MCOC AR VR switch").AddComponent<StructuralVRController>();
    }

    private void OnGUI()
    {
        if (IsVR) return; // IMGUI is not stereo: all VR controls are actual world-space objects.
        float scale = Mathf.Max(1f, Mathf.Min(Screen.width,Screen.height)/390f);
        Matrix4x4 previous=GUI.matrix; GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
        Rect safe=Screen.safeArea;
        GUI.enabled=!IsChanging;
        if(GUI.Button(new Rect((safe.x+12)/scale,(Screen.height-safe.yMax+12)/scale,150,40),"Modo VR · edificio"))
            StartCoroutine(EnterVR());
        GUI.enabled=true;
        if(!string.IsNullOrEmpty(message))GUI.Label(new Rect(12,60,Screen.width/scale-24,48),message);
        GUI.matrix=previous;
    }

    public IEnumerator EnterVR()
    {
        if(IsVR||IsChanging)yield break;
        ar=FindAnyObjectByType<StructuralARController>(); session=FindAnyObjectByType<ARSession>(); arCamera=Camera.main;
        if(ar==null||arCamera==null) {message="No se encontro la escena AR.";yield break;}
        IsChanging=true; walking=false; savedCombo=UnityData.ActiveCombo; oldOrientation=Screen.orientation;
        // Cancelling a partial placement avoids invisible guides and late anchor callbacks.
        ar.CancelForVR(); ar.enabled=false;
        if(session!=null)session.enabled=false;
        arCamera.gameObject.SetActive(false);
        arLoader=XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager.activeLoader : null;
        if(arLoader!=null)arLoader.Stop();
        oldPortrait=Screen.autorotateToPortrait;
        oldPortraitUpsideDown=Screen.autorotateToPortraitUpsideDown;
        oldLandscapeLeft=Screen.autorotateToLandscapeLeft;
        oldLandscapeRight=Screen.autorotateToLandscapeRight;
        Screen.autorotateToPortrait=false;
        Screen.autorotateToPortraitUpsideDown=false;
        Screen.autorotateToLandscapeLeft=true;
        Screen.autorotateToLandscapeRight=true;
        Screen.orientation=ScreenOrientation.AutoRotation;
        yield return null;
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        // Let the phone settle its screen rotation before Cardboard initializes its sensor/display basis.
        float orientationDeadline=Time.realtimeSinceStartup+2f;
        while(Screen.orientation!=ScreenOrientation.LandscapeLeft && Screen.orientation!=ScreenOrientation.LandscapeRight && Time.realtimeSinceStartup<orientationDeadline)
            yield return null;
#endif
        bool started=true;
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        cardboard=ScriptableObject.CreateInstance<Google.XR.Cardboard.XRLoader>();
        started=cardboard.Initialize() && cardboard.Start();
        if(started)started=cardboard.GetLoadedSubsystem<XRDisplaySubsystem>()?.running==true;
        if(started)started=cardboard.GetLoadedSubsystem<XRInputSubsystem>()?.running==true;
#endif
        if(!started){message="No pudo iniciarse Cardboard. Se recupero AR.";RestoreAR();IsChanging=false;yield break;}
        try
        {
            BuildWorld(); IsVR=true; message="";
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            if(!Google.XR.Cardboard.Api.HasDeviceParams())Google.XR.Cardboard.Api.ScanDeviceParams();
#endif
        }
        catch(Exception error){Debug.LogException(error);message="Error al abrir VR: "+error.Message;RestoreAR();}
        IsChanging=false;
    }

    public void ExitVR()
    {
        if(!IsVR||IsChanging)return;
        IsChanging=true; RestoreAR(); IsChanging=false;
    }

    private void RestoreAR()
    {
        ResetMobileWorld();walking=false;movementDirection=Vector3.zero;IsVR=false;selected=null;gazeTarget=activatedTarget=null;
        if(cardboard!=null){cardboard.Stop();cardboard.Deinitialize();Destroy(cardboard);cardboard=null;}
        if(world!=null)
        {
            // Primitive meshes are Unity assets; only release materials created for this world.
            string[] ownedByDecor={"Pasto_visual","Roca_visual","Salmon_fachada_visual","Cubierta_plana_gris","Escaleras_grises_visuales"};
            foreach(Material material in world.GetComponentsInChildren<Renderer>(true)
                .Where(r=>r.GetComponent<TextMesh>()==null&&!OwnedByDecoration(r)).SelectMany(r=>r.sharedMaterials).Distinct())
                if(material!=null&&!materials.Contains(material)&&!ownedByDecor.Contains(material.name))Destroy(material);
            world.SetActive(false);Destroy(world);
        }
        if(rig!=null){rig.SetActive(false);Destroy(rig);}world=rig=null;diagrams.Clear();actions.Clear();
        foreach(Material material in materials)Destroy(material);materials.Clear();
        Screen.autorotateToPortrait=oldPortrait;
        Screen.autorotateToPortraitUpsideDown=oldPortraitUpsideDown;
        Screen.autorotateToLandscapeLeft=oldLandscapeLeft;
        Screen.autorotateToLandscapeRight=oldLandscapeRight;
        Screen.orientation=oldOrientation;
        if(arLoader!=null)arLoader.Start();
        if(arCamera!=null)arCamera.gameObject.SetActive(true);
        if(session!=null)session.enabled=true;
        if(ar!=null){ar.RestoreEngineeringCases();ar.enabled=true;ar.ResumeAfterVR();}
        if(!string.IsNullOrEmpty(savedCombo))UnityData.ActiveCombo=savedCombo;
    }

    private void BuildWorld()
    {
        data=JsonUtility.FromJson<StructureData>(Resources.Load<TextAsset>("estructura_p1l4_unity").text);
        Floors=data.slabs.Select(s=>s.z).Distinct().OrderBy(y=>y).ToArray();
        if(Floors.Length==0)throw new InvalidOperationException("El modelo no contiene pisos.");
        rig=new GameObject("Recorrido VR visual");
        GameObject head=new GameObject("Camara VR");head.transform.SetParent(rig.transform,false);
        vrCamera=head.AddComponent<Camera>();head.tag="MainCamera";vrCamera.nearClipPlane=.05f;vrCamera.farClipPlane=1000;
        vrCamera.cullingMask=(1<<30)|(1<<31);
        // Cardboard exposes CenterEyeRotation. Track only orientation: floor/walking own position.
        var pose=head.AddComponent<TrackedPoseDriver>();
        pose.trackingType=TrackedPoseDriver.TrackingType.RotationOnly;
        pose.updateType=TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        pose.ignoreTrackingState=true;
        pose.rotationInput=new InputActionProperty(new InputAction("Cardboard eye rotation",InputActionType.Value,"<XRHMD>/centerEyeRotation",expectedControlType:"Quaternion"));
#if UNITY_EDITOR
        pose.enabled=false;
        // XR Simulation supplies AR eye matrices; the editor preview uses its own camera pose.
        vrCamera.stereoTargetEye=StereoTargetEyeMask.None;
#endif
        head.AddComponent<AudioListener>();
        world=new GameObject("Edificio VR sin reanalisis");
        world.AddComponent<StructureViewer>();
        // Desktop menus and movement scripts have their own lifecycle; keep them out of VR.
        foreach(MonoBehaviour component in world.GetComponents<MonoBehaviour>())component.enabled=false;
        foreach(var selectable in world.GetComponentsInChildren<ElementSelectable>())selectable.enabled=false;
        Material floorMaterial=MaterialFor(new Color(.53f,.55f,.57f));
        foreach(var slab in data.slabs)
        {
            GameObject floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Piso_transitable_visual_"+slab.id;
            floor.transform.SetParent(world.transform,false);floor.layer=2;
            floor.transform.position=new Vector3((slab.x0+slab.x1)*.5f,slab.z+.39f,(slab.y0+slab.y1)*.5f);
            floor.transform.localScale=new Vector3(Mathf.Abs(slab.x1-slab.x0),.12f,Mathf.Abs(slab.y1-slab.y0));
            floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;floor.GetComponent<Collider>().enabled=false;Destroy(floor.GetComponent<Collider>());
        }
        foreach(Transform child in world.GetComponentsInChildren<Transform>(true))child.gameObject.layer=30;
        PrepareEnvironment();
        CreateMenu();
        FloorIndex=Array.FindIndex(Floors,f=>Mathf.Abs(f)<.01f);if(FloorIndex<0)FloorIndex=0;
        ChangeFloor(0);
        for(int i=0;i<2;i++)
        {
            GameObject line=new GameObject(i==0?"Eje seleccionado VR":"Diagrama OpenSees VR");line.layer=30;line.transform.SetParent(world.transform,false);
            var renderer=line.AddComponent<LineRenderer>();renderer.sharedMaterial=MaterialFor(i==0?Color.cyan:Color.yellow,true);
            renderer.sharedMaterial.renderQueue=3990; // Keep the menu legible over the result overlay.
            renderer.startWidth=renderer.endWidth=i==0?.035f:.055f;renderer.useWorldSpace=true;diagrams.Add(renderer);
            renderer.positionCount=0;
        }
        nodeILabel=Text("Nodo I seleccionado",world.transform,Vector3.zero,.020f,"");
        nodeJLabel=Text("Nodo J seleccionado",world.transform,Vector3.zero,.020f,"");
        InitializeMobileWorld();
        InitializeEngineeringVR();
    }

    private Material MaterialFor(Color color,bool overlay=false)
    {
        Material material=new Material(overlay?Resources.Load<Shader>("VRMenuOverlay"):(Shader.Find("Unlit/Color")??Shader.Find("Sprites/Default"))){color=color};materials.Add(material);return material;
    }

    private TextMesh Text(string name,Transform parent,Vector3 position,float size,string value)
    {
        GameObject text=new GameObject(name);text.layer=31;text.transform.SetParent(parent,false);text.transform.localPosition=position;
        TextMesh mesh=text.AddComponent<TextMesh>();mesh.text=value;mesh.fontSize=64;mesh.characterSize=size;
        mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;mesh.color=Color.white;
        var renderer=text.GetComponent<Renderer>();Texture atlas=renderer.sharedMaterial.mainTexture;
        renderer.sharedMaterial=MaterialFor(Color.white,true);renderer.sharedMaterial.mainTexture=atlas;renderer.sharedMaterial.renderQueue=4001;
        return mesh;
    }

    private void CreateMenu()
    {
        menu=new GameObject("Menu espacial VR").transform;menu.SetParent(rig.transform,false);
        menu.localScale=Vector3.one*.8f;
        title=Text("Piso actual",menu,new Vector3(0,.43f,0),.021f,"");
        info=Text("Resultado seleccionado",menu,new Vector3(0,-1.12f,0),.015f,"Mira una viga o columna\nManten la mira 1,2 s o pulsa el visor");
        info.anchor=TextAnchor.UpperCenter;
        string[] labels={"Piso -","Piso +","Detener","C1/C2/C3","N/V/M","Ambiente","Centrar","Salir AR","Diagrama","Sismo","Acero"};
        Action[] callbacks={()=>ChangeFloor(-1),()=>ChangeFloor(1),()=>StopMovement(),()=>CycleCombo(),()=>CycleResult(),()=>ToggleEnvironment(),()=>Recenter(),()=>ExitVR(),()=>{showDiagram=!showDiagram;RefreshResult();},()=>ToggleMobileSeismic(),()=>{showSteel=!showSteel;RefreshResult();}};
        for(int i=0;i<labels.Length;i++)
        {
            GameObject button=GameObject.CreatePrimitive(PrimitiveType.Cube);button.name=labels[i];button.layer=31;button.transform.SetParent(menu,false);
            button.transform.localPosition=new Vector3((i%4-1.5f)*.47f,.23f-(i/4)*.23f,0);button.transform.localScale=new Vector3(.44f,.18f,.02f);
            button.GetComponent<Renderer>().sharedMaterial=MaterialFor(new Color(.06f,.19f,.30f),true);
            var action=button.AddComponent<StructuralVRButton>();action.Index=i;actions.Add(callbacks[i]);
            Text(labels[i],menu,button.transform.localPosition+new Vector3(0,0,-.02f),.014f,labels[i]);
        }
        Text("Mira",vrCamera.transform,new Vector3(0,0,1),.010f,"+");
        directionPad=new GameObject("Cruceta con mirada").transform;directionPad.SetParent(menu,false);
        Vector2[] directions={Vector2.up,Vector2.down,Vector2.left,Vector2.right};
        string[] directionLabels={"Avanzar ↑","Retroceder ↓","← Izquierda","Derecha →"};
        for(int i=0;i<4;i++)
        {
            var button=GameObject.CreatePrimitive(PrimitiveType.Cube);button.name=directionLabels[i];button.layer=31;button.transform.SetParent(directionPad,false);
            button.transform.localPosition=new Vector3(directions[i].x*.49f,-.71f+directions[i].y*.21f,0);
            button.transform.localScale=new Vector3(.46f,.18f,.02f);
            button.GetComponent<Renderer>().sharedMaterial=MaterialFor(new Color(.10f,.29f,.22f),true);
            var control=button.AddComponent<StructuralVRButton>();control.Direction=directions[i];
            Text(directionLabels[i],directionPad,button.transform.localPosition+new Vector3(0,0,-.02f),.013f,directionLabels[i]);
        }
        Text("Instruccion de cruceta",directionPad,new Vector3(0,-.71f,-.02f),.010f,"Mira 0,8 s\nAparta: parar");
        var ring=new GameObject("Progreso de mirada");ring.layer=31;ring.transform.SetParent(vrCamera.transform,false);
        gazeProgress=ring.AddComponent<LineRenderer>();gazeProgress.useWorldSpace=false;gazeProgress.sharedMaterial=MaterialFor(Color.yellow,true);
        gazeProgress.startWidth=gazeProgress.endWidth=.002f;gazeProgress.positionCount=0;
        PositionMenu();
    }

    private void PositionMenu()
    {
        Vector3 forward=vrCamera.transform.forward;forward.y=0;if(forward.sqrMagnitude<.001f)forward=rig.transform.forward;
        menu.position=vrCamera.transform.position+forward.normalized*2.6f+Vector3.down*.65f;
        menu.rotation=Quaternion.LookRotation(forward.normalized);
    }

    private void FollowMenu()
    {
        Vector3 forward=Vector3.ProjectOnPlane(vrCamera.transform.forward,Vector3.up);
        // A fixed-to-head panel would never let the central reticle reach its side buttons.
        // Keep it stable while aiming; recapture it after a wide turn, following position always.
        bool aiming=gazeTarget!=null&&gazeTarget.GetComponent<StructuralVRButton>()!=null;
        if(!aiming&&forward.sqrMagnitude>.01f&&Vector3.Angle(menu.forward,forward)>35f)
            menu.rotation=Quaternion.LookRotation(forward.normalized);
        menu.position=vrCamera.transform.position+menu.forward*2.6f+Vector3.down*.65f;
    }

    public void ChangeFloor(int delta)
    {
        StopMovement();FloorIndex=Mathf.Clamp(FloorIndex+delta,0,Floors.Length-1);
        float y=Floors[FloorIndex];var slabs=data.slabs.Where(s=>Mathf.Abs(s.z-y)<.01f).ToArray();
        Vector3 old=rig.transform.position;
        Physics.SyncTransforms();
        var positions=new List<Vector2>();
        if(old.y>1f)positions.Add(new Vector2(old.x,old.z));
        foreach(var slab in slabs.OrderByDescending(s=>Mathf.Abs((s.x1-s.x0)*(s.y1-s.y0))))
        {
            positions.Add(new Vector2((slab.x0+slab.x1)*.5f,(slab.y0+slab.y1)*.5f));
            for(int x=1;x<=3;x++)for(int z=1;z<=3;z++)positions.Add(new Vector2(Mathf.Lerp(slab.x0,slab.x1,x/4f),Mathf.Lerp(slab.y0,slab.y1,z/4f)));
        }
        bool found=false;
        foreach(Vector2 point in positions)
        {
            if(!slabs.Any(s=>Contains(s,point.x,point.y))||Physics.CheckSphere(new Vector3(point.x,y+1.25f,point.y),.25f,1<<30))continue;
            rig.transform.position=new Vector3(point.x,y+2.05f,point.y);found=true;break;
        }
        if(!found)throw new InvalidOperationException("No se encontro una posicion libre en el piso.");
        PositionMenu();UpdateTitle();
    }

    private static bool Contains(SlabData slab,float x,float z) => slab.Contains(x,z);
    private void UpdateTitle(){if(title!=null)title.text="Piso "+(FloorIndex+1)+"/"+Floors.Length+" · Y="+Floors[FloorIndex].ToString("0.##")+" m · "+(walking?"Caminando":"Detenido");}
    private void StopMovement(){walking=false;movementDirection=Vector3.zero;UpdateTitle();}
    private void Recenter(){StopMovement();
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        Google.XR.Cardboard.Api.Recenter();
#endif
        PositionMenu();UpdateTitle();}
    private void ToggleEnvironment()
    {
        environment=!environment;
        ApplyEnvironmentVisibility();
    }

    private void PrepareEnvironment()
    {
        Shader shader=Resources.Load<Shader>("VRVisualEnvironment");
        if(shader==null)throw new InvalidOperationException("Falta el material del ambiente VR.");
        // Explicit, stereo-compatible rendering; no dependency on AR lighting or stripped Standard variants.
        var roots=new List<Transform>();
        roots.AddRange(world.GetComponentsInChildren<VisualSiteTerrain>(true).Select(c=>c.transform));
        roots.AddRange(world.GetComponentsInChildren<VisualFrameFacade>(true).Select(c=>c.transform));
        roots.AddRange(world.GetComponentsInChildren<VisualFlatRoof>(true).Select(c=>c.transform));
        roots.AddRange(world.GetComponentsInChildren<VisualStairs>(true).Select(c=>c.transform));
        roots.AddRange(world.GetComponentsInChildren<VisualCafe>(true).Select(c=>c.transform));
        roots.AddRange(world.GetComponentsInChildren<VisualCampusSite>(true).Select(c=>c.transform));
        roots.AddRange(world.GetComponentsInChildren<VisualStudyRoom>(true).Select(c=>c.transform));
        roots.AddRange(world.GetComponentsInChildren<VisualInteriorPartitions>(true).Select(c=>c.transform));
        foreach(var root in roots)
        {
            root.gameObject.SetActive(true);
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.layer=30;renderer.enabled=true;
                foreach(var material in renderer.sharedMaterials)if(material!=null){material.shader=shader;material.enableInstancing=true;}
            }
        }
        ApplyEnvironmentVisibility();
    }

    private void ApplyEnvironmentVisibility()
    {
        var terrain=world.GetComponentInChildren<VisualSiteTerrain>(true);terrain.SetVisibility(environment,environment);
        foreach(var cafe in world.GetComponentsInChildren<VisualCafe>(true))cafe.gameObject.SetActive(environment);
        foreach(var room in world.GetComponentsInChildren<VisualStudyRoom>(true))room.gameObject.SetActive(environment);
        foreach(var partitions in world.GetComponentsInChildren<VisualInteriorPartitions>(true))partitions.gameObject.SetActive(environment);
        foreach(var campus in world.GetComponentsInChildren<VisualCampusSite>(true))campus.gameObject.SetActive(environment);
        foreach(var facade in world.GetComponentsInChildren<VisualFrameFacade>(true))foreach(var p in facade.Panels)p.SetActive(environment);
        foreach(var roof in world.GetComponentsInChildren<VisualFlatRoof>(true))foreach(var p in roof.Pieces)p.SetActive(environment);
        foreach(var stairs in world.GetComponentsInChildren<VisualStairs>(true))foreach(var p in stairs.Pieces)p.SetActive(environment);
    }
    private void CycleCombo(){combo=combo=="C1"?"C2":combo=="C2"?"C3":"C1";RefreshResult();}
    private void CycleResult(){int[] components={0,1,2,4,5};int index=Array.IndexOf(components,result);result=components[(index+1)%components.Length];RefreshResult();}

    private void Update()
    {
        if(!IsVR||IsChanging)return;
        bool trigger=false;
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        Google.XR.Cardboard.Api.UpdateScreenParams();
        if(Google.XR.Cardboard.Api.IsCloseButtonPressed){ExitVR();return;}
        if(Google.XR.Cardboard.Api.IsGearButtonPressed)Google.XR.Cardboard.Api.ScanDeviceParams();
        if(Google.XR.Cardboard.Api.IsTriggerHeldPressed)Recenter();
        trigger=Google.XR.Cardboard.Api.IsTriggerPressed;
#else
        trigger=Input.GetMouseButtonDown(0);
        if(Input.GetMouseButton(1))vrCamera.transform.Rotate(-Input.GetAxis("Mouse Y")*2f,Input.GetAxis("Mouse X")*2f,0);
#endif
        FollowMenu();
        Ray ray=new Ray(vrCamera.transform.position,vrCamera.transform.forward);
        var hits=Physics.RaycastAll(ray,80f).OrderBy(h=>h.distance);
        RaycastHit? chosen=null;
        // Menu remains operable even where structural geometry is close to the user.
        if(Physics.Raycast(ray,out RaycastHit menuHit,10f,1<<31))chosen=menuHit;
        foreach(var hit in hits)
        {
            if(chosen.HasValue)break;
            if(hit.collider.GetComponent<StructuralVRButton>()!=null){chosen=hit;break;}
            var element=hit.collider.GetComponent<ElementSelectable>();
            if(element!=null && element.transform.IsChildOf(world.transform) && element.data!=null && (element.data.type=="viga"||element.data.type=="columna"||element.data.type=="muro_eq")){chosen=hit;break;}
        }
        Collider target=chosen.HasValue?chosen.Value.collider:null;
        HandleGaze(target,trigger,Time.unscaledTime);
        if(!IsVR)return;
        UpdateMobileWorld();
        UpdateEngineeringVR();
        if(walking&&!TryMove(Time.deltaTime))StopMovement();
        FollowMenu();
        if(selected!=null)
        {
            nodeILabel.transform.rotation=Quaternion.LookRotation(nodeILabel.transform.position-vrCamera.transform.position);
            nodeJLabel.transform.rotation=Quaternion.LookRotation(nodeJLabel.transform.position-vrCamera.transform.position);
        }
    }

    private void HandleGaze(Collider target,bool trigger,float now)
    {
        if(target!=gazeTarget)
        {
            StopMovement();gazeTarget=target;gazeSince=now;activatedTarget=null;
            var entered=target!=null?target.GetComponent<StructuralVRButton>():null;
            if(entered!=null&&entered.IsDirection)
            {
                Vector3 forward=Vector3.ProjectOnPlane(menu.forward,Vector3.up).normalized;
                movementDirection=forward*entered.Direction.y+Vector3.Cross(Vector3.up,forward)*entered.Direction.x;
            }
        }
        var button=target!=null?target.GetComponent<StructuralVRButton>():null;
        float delay=button!=null&&button.IsDirection?.8f:1.2f;
        float progress=target!=null?Mathf.Clamp01((now-gazeSince)/delay):0;
        gazeProgress.positionCount=progress>0?Mathf.Max(2,Mathf.CeilToInt(progress*32)+1):0;
        for(int i=0;i<gazeProgress.positionCount;i++)
        {
            float angle=i/(float)(gazeProgress.positionCount-1)*progress*Mathf.PI*2;
            gazeProgress.SetPosition(i,new Vector3(Mathf.Sin(angle)*.024f,Mathf.Cos(angle)*.024f,1f));
        }
        if(target==null||target==activatedTarget&&!(trigger&&button!=null&&!button.IsDirection))return;
        if(progress<1&&!(trigger&&(button==null||!button.IsDirection)))return;
        activatedTarget=target;
        if(button!=null)
        {
            if(button.IsDirection){walking=true;UpdateTitle();}
            else actions[button.Index]();
        }
        else SelectElement(target.GetComponent<ElementSelectable>());
    }

    private bool TryMove(float deltaTime)
    {
        if(MobileSeismicPlayback.IsActive)return false;
        float distance=Mathf.Clamp(deltaTime,0,.05f)*1.8f;
        if(movementDirection.sqrMagnitude<.01f)return false;
        Vector3 candidate=rig.transform.position+movementDirection.normalized*distance;
        bool supported=data.slabs.Any(s=>Mathf.Abs(s.z-Floors[FloorIndex])<.01f&&Contains(s,candidate.x,candidate.z));
        Vector3 torso=rig.transform.position+Vector3.down*.8f;
        bool blocked=Physics.CheckCapsule(torso,rig.transform.position,.2f,1<<30)||Physics.CapsuleCast(torso,rig.transform.position,.2f,movementDirection.normalized,out _,distance+.05f,1<<30);
        if(!supported||blocked)return false;
        rig.transform.position=candidate;return true;
    }

    private void SelectElement(ElementSelectable element)
    {
        if(element==null||element.data==null)return;
        if(selected!=null)selected.OnDeselected();selected=element;selected.OnSelected();
        StopMovement();showDiagram=true;RefreshResult();
    }

    private void RefreshResult()
    {
        if(selected==null)return;
        UnityData.ActiveCombo=combo;
        Vector3 a=selected.startPoint,b=selected.endPoint;
        Vector3 axis=(b-a).normalized,down=Vector3.ProjectOnPlane(Vector3.down,axis).normalized;if(down.sqrMagnitude<.01f)down=Vector3.forward;
        float[] values=new float[41];float maximum=0;
        for(int i=0;i<41;i++){if(!TryVRSection(i/40f,out var section))return;values[i]=section.Component(result);maximum=Mathf.Max(maximum,Mathf.Abs(values[i]));}
        diagrams[0].positionCount=2;diagrams[0].SetPositions(new[]{a,b});diagrams[1].positionCount=41;
        for(int i=0;i<41;i++)diagrams[1].SetPosition(i,Vector3.Lerp(a,b,i/40f)+down*(maximum>.00001f?values[i]/maximum*.75f:0));
        foreach(var diagram in diagrams)diagram.enabled=showDiagram;
        nodeILabel.text="I · N"+selected.data.nodeI;nodeJLabel.text="J · N"+selected.data.nodeJ;
        nodeILabel.transform.position=a+Vector3.up*.22f;nodeJLabel.transform.position=b+Vector3.up*.22f;
        nodeILabel.gameObject.SetActive(showDiagram);nodeJLabel.gameObject.SetActive(showDiagram);
        string component=result==0?"N":result==1?"Vy":result==2?"Vz":result==4?"My":"Mz";
        info.text=selected.data.elementTag+" · ID "+selected.data.id+" · "+(MobileSeismicPlayback.IsActive?MobileSeismicPlayback.Instance.Summary:combo)+"\nI: N"+selected.data.nodeI+" → J: N"+selected.data.nodeJ+"\n"+component+" I / centro / J\n"+values[0].ToString("0.00")+" / "+values[20].ToString("0.00")+" / "+values[40].ToString("0.00")+" "+(result>=4?"kN·m":"kN")+"\nDiagrama normalizado · "+(showDiagram?"visible":"oculto");
        if(showSteel)info.text+="\n"+MobileSeismicPlayback.Reinforcement(selected.data);
        if(MobileSeismicPlayback.IsActive)info.text+="\n"+CapacitySummary();
    }

    private void OnDestroy(){if(IsVR)RestoreAR();}
}

public sealed class StructuralVRButton : MonoBehaviour { public int Index; public Vector2 Direction; public bool IsDirection=>Direction.sqrMagnitude>.01f; }

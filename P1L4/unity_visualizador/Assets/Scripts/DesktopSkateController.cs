using System.Collections.Generic;
using UnityEngine;

// Local PC gameplay only: shares the walkthrough capsule, never the structural model.
public sealed class DesktopSkateController : MonoBehaviour
{
    public enum Trick { None, Kickflip, ShoveIt }
    public Trick ActiveTrick {get;private set;}
    public Trick LastLandedTrick {get;private set;}
    public float TrickProgress {get;private set;}
    public bool Mounted {get;private set;}
    public float Speed => velocity.magnitude;
    public Vector3 Velocity => velocity;
    public string Status {get;private set;}="Listo";
    public GameObject Visual {get;private set;}
    public float maximumSpeed=11f, pushAcceleration=4.8f, brakeAcceleration=9f, rollingResistance=.45f;
    public float ollieHeight=.65f;
    private CharacterController body;
    private Camera cameraView;
    private Vector3 velocity,groundNormal=Vector3.up;
    private float gravity,verticalSpeed,heading,orbitYaw,orbitPitch=20,cameraYaw,jumpTime,groundGrace,jumpBuffer;
    private float originalStepOffset;
    private Transform rider,board,frontLeg,rearLeg;
    private readonly List<Material> materials=new List<Material>();
    private readonly List<Transform> wheels=new List<Transform>();
    private Vector3 blockingNormal;
    private Trick pendingTrick;
    private float trickTime,trickDuration,landingMessageTime;
    private Quaternion boardBaseRotation;
    private bool wasAirborne;

    public void Configure(CharacterController capsule,Camera camera,float worldGravity)
    {body=capsule;cameraView=camera;gravity=worldGravity;originalStepOffset=body.stepOffset;}

    public bool Toggle()
    {
        if(body==null || cameraView==null)return false;
        if(!Grounded(out groundNormal) || verticalSpeed>0)
        {Status="Aterriza antes de subir o bajar";return false;}
        Mounted=!Mounted;ResetMotion();
        body.stepOffset=Mounted?.08f:originalStepOffset;
        if(Mounted)
        {
            heading=transform.eulerAngles.y;orbitYaw=0;orbitPitch=20;cameraYaw=heading;
            if(Visual==null)BuildVisual();Visual.SetActive(true);
            Status="W para impulsarte";UpdateCamera(Vector2.zero,0);
        }
        else {if(Visual!=null)Visual.SetActive(false);Status="A pie";}
        return true;
    }

    public void ResetMotion()
    {
        velocity=Vector3.zero;verticalSpeed=0;jumpTime=groundGrace=jumpBuffer=0;blockingNormal=Vector3.zero;
        ActiveTrick=pendingTrick=LastLandedTrick=Trick.None;TrickProgress=trickTime=landingMessageTime=0;wasAirborne=false;boardBaseRotation=Quaternion.identity;
        heading=transform.eulerAngles.y;
        if(board!=null){board.localRotation=Quaternion.identity;board.localPosition=Vector3.zero;}
        if(rider!=null)rider.localRotation=Quaternion.identity;
        Status=Mounted?"W para impulsarte":"Listo";
    }

    public bool RequestTrick(Trick trick)
    {
        if(!Mounted || trick==Trick.None || ActiveTrick!=Trick.None || pendingTrick!=Trick.None)return false;
        if(!Grounded(out var normal))
        {Status="Haz el truco desde el suelo";return false;}
        pendingTrick=trick;jumpBuffer=.12f;return true;
    }

    private bool Grounded(out Vector3 normal)
    {
        normal=Vector3.up;
        // Ground probes only see the temporary navigation world (layer Ignore Raycast).
        if(Physics.SphereCast(transform.position+Vector3.up*.25f,.13f,Vector3.down,out var hit,.18f,1<<2,QueryTriggerInteraction.Ignore))
        {
            normal=hit.normal;
            return verticalSpeed<=0 && normal.y>=Mathf.Cos(body.slopeLimit*Mathf.Deg2Rad);
        }
        return body.isGrounded && verticalSpeed<=0;
    }

    public void Step(Vector2 input,bool ollie,float dt)
    {
        if(!Mounted || body==null || !body.enabled || dt<=0)return;
        dt=Mathf.Min(dt,.05f);input=Vector2.ClampMagnitude(input,1);
        bool grounded=Grounded(out groundNormal);
        groundGrace=grounded?.10f:Mathf.Max(0,groundGrace-dt);
        jumpBuffer=ollie?.12f:Mathf.Max(0,jumpBuffer-dt);
        if(jumpBuffer<=0)pendingTrick=Trick.None;
        landingMessageTime=Mathf.Max(0,landingMessageTime-dt);
        float previousSpeed=Speed;
        // A/D steer the board; looking with the mouse never changes travel direction.
        if(previousSpeed>.12f || input.y>0)
            heading+=input.x*(grounded?95f:40f)*Mathf.Clamp01(previousSpeed/1.5f+.15f)*dt;
        transform.rotation=Quaternion.Euler(0,heading,0);
        if(grounded)
        {
            if(input.y>0)velocity+=transform.forward*(pushAcceleration*input.y*dt);
            Vector3 slopePull=Vector3.ProjectOnPlane(Vector3.up*gravity,groundNormal);
            slopePull.y=0;velocity+=slopePull*dt;
            float deceleration=rollingResistance+(input.y<0?brakeAcceleration*-input.y:0);
            velocity=Vector3.MoveTowards(velocity,Vector3.zero,deceleration*dt);
            // Wheels constrain lateral motion gradually, preserving momentum around turns.
            float direction=Vector3.Dot(velocity,transform.forward)>=0?1:-1;
            velocity=Vector3.Lerp(velocity,transform.forward*(velocity.magnitude*direction),1-Mathf.Exp(-7*dt));
            verticalSpeed=-3;
        }
        velocity=Vector3.ClampMagnitude(velocity,maximumSpeed);
        if(jumpBuffer>0 && groundGrace>0)
        {
            ActiveTrick=pendingTrick;pendingTrick=Trick.None;LastLandedTrick=Trick.None;TrickProgress=trickTime=0;
            float height=ActiveTrick==Trick.None?ollieHeight:Mathf.Max(ollieHeight,.85f);
            verticalSpeed=Mathf.Sqrt(height*-2*gravity);jumpBuffer=groundGrace=0;
            trickDuration=2*verticalSpeed/-gravity*.78f;
            grounded=false;jumpTime=0;wasAirborne=true;Status=TrickName(ActiveTrick);
        }
        verticalSpeed=Mathf.Max(-50,verticalSpeed+gravity*dt);
        Vector3 travel=grounded?Vector3.ProjectOnPlane(velocity,groundNormal):velocity;
        blockingNormal=Vector3.zero;
        Vector3 before=transform.position;
        var flags=body.Move((travel+Vector3.up*verticalSpeed)*dt);
        if((flags&CollisionFlags.Above)!=0 && verticalSpeed>0)verticalSpeed=0;
        bool landed=(flags&CollisionFlags.Below)!=0 && verticalSpeed<=0;
        bool trickLanding=ActiveTrick!=Trick.None && landed && wasAirborne;
        if(ActiveTrick!=Trick.None)
        {
            trickTime+=dt;TrickProgress=Mathf.Clamp01(trickTime/trickDuration);
            if(landed && wasAirborne)
            {
                bool complete=TrickProgress>=.95f;
                LastLandedTrick=complete?ActiveTrick:Trick.None;
                Status=complete?TrickName(ActiveTrick)+" · aterrizado":"Truco interrumpido";
                landingMessageTime=2;
                ActiveTrick=Trick.None;wasAirborne=false;
            }
        }
        if(blockingNormal.sqrMagnitude>.01f && Vector3.Dot(velocity,blockingNormal)<0)
        {
            // Remove only momentum into a wall; keep the component along it.
            velocity=Vector3.ProjectOnPlane(velocity,blockingNormal);velocity.y=0;
            Status="Obstáculo · S frenar / E bajarse";
        }
        else if((flags&CollisionFlags.Sides)!=0)
        {
            // Also works in editor-driven simulation where collision callbacks may not run.
            Vector3 actual=(transform.position-before)/dt;actual.y=0;
            if(actual.sqrMagnitude<velocity.sqrMagnitude)velocity=actual;
            Status="Obstáculo · S frenar / E bajarse";
        }
        else if(landed && landingMessageTime<=0 && !trickLanding)
            Status=input.y<0?"Frenando":Speed>.2f?"Rodando":"W para impulsarte";
        else if(!landed && ActiveTrick!=Trick.None)Status=TrickName(ActiveTrick);
        else if(!landed && verticalSpeed<0)Status="Aterrizando";
        if(!landed)wasAirborne=true;
        jumpTime+=dt;UpdateVisual(input.x,grounded,dt);
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {if(Mounted && Mathf.Abs(hit.normal.y)<.5f)blockingNormal=hit.normal;}

    public void UpdateCamera(Vector2 look,float dt)
    {
        if(!Mounted || cameraView==null)return;
        orbitYaw+=look.x;orbitPitch=Mathf.Clamp(orbitPitch-look.y,8,65);
        cameraYaw=dt>0?Mathf.LerpAngle(cameraYaw,heading+orbitYaw,1-Mathf.Exp(-8*dt)):heading+orbitYaw;
        Vector3 focus=transform.position+Vector3.up*1.05f;
        Vector3 offset=Quaternion.Euler(orbitPitch,cameraYaw,0)*(Vector3.back*3.8f);
        float distance=offset.magnitude;
        if(Physics.SphereCast(focus,.18f,offset.normalized,out var hit,distance,1<<2,QueryTriggerInteraction.Ignore))
            distance=Mathf.Max(.12f,hit.distance-.08f);
        cameraView.transform.position=focus+offset.normalized*distance;
        cameraView.transform.rotation=Quaternion.LookRotation(focus-cameraView.transform.position,Vector3.up);
    }

    private void UpdateVisual(float steering,bool grounded,float dt)
    {
        if(Visual==null)return;
        Quaternion slope=grounded?Quaternion.FromToRotation(Vector3.up,transform.InverseTransformDirection(groundNormal)):Quaternion.identity;
        float pop=!grounded && verticalSpeed>0?Mathf.Sin(Mathf.Min(jumpTime/.25f,1)*Mathf.PI)*-15:0;
        boardBaseRotation=Quaternion.Slerp(boardBaseRotation,slope*Quaternion.Euler(pop,0,-steering*5),1-Mathf.Exp(-12*dt));
        float angle=360*Mathf.SmoothStep(0,1,TrickProgress);
        Quaternion trickRotation=ActiveTrick==Trick.Kickflip?Quaternion.AngleAxis(angle,Vector3.forward):
            ActiveTrick==Trick.ShoveIt?Quaternion.AngleAxis(angle,Vector3.up):Quaternion.identity;
        board.localRotation=boardBaseRotation*trickRotation;
        board.localPosition=ActiveTrick!=Trick.None?Vector3.up*.15f:Vector3.zero;
        rider.localRotation=Quaternion.Slerp(rider.localRotation,Quaternion.Euler(0,0,-steering*Mathf.Min(Speed,5)*2),1-Mathf.Exp(-8*dt));
        float crouch=!grounded?.08f:0;
        rider.localPosition=ActiveTrick!=Trick.None?Vector3.up*.12f:Vector3.down*crouch;
        foreach(var wheel in wheels)wheel.Rotate(Vector3.up,Speed*dt/.035f*Mathf.Rad2Deg,Space.Self);
        if(frontLeg!=null)frontLeg.localRotation=Quaternion.Euler(0,0,grounded?0:8);
        if(rearLeg!=null)rearLeg.localRotation=Quaternion.Euler(0,0,grounded?0:-8);
    }
    public static string TrickName(Trick trick)=>trick==Trick.Kickflip?"Kickflip":trick==Trick.ShoveIt?"Shove-it 360":"Ollie";

    private Material Material(Color color)
    {
        var shader=Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
        var material=new Material(shader){color=color};materials.Add(material);return material;
    }
    private Transform Part(string name,PrimitiveType type,Transform parent,Vector3 position,Vector3 scale,Material material)
    {
        var part=GameObject.CreatePrimitive(type);part.name=name;part.layer=2;part.transform.SetParent(parent,false);
        part.transform.localPosition=position;part.transform.localScale=scale;part.GetComponent<Renderer>().sharedMaterial=material;
        var collider=part.GetComponent<Collider>();collider.enabled=false;Release(collider);return part.transform;
    }
    private void BuildVisual()
    {
        Visual=new GameObject("Skate_y_patinador_solo_visual");Visual.transform.SetParent(transform,false);
        board=new GameObject("Tabla").transform;board.SetParent(Visual.transform,false);
        rider=new GameObject("Patinador").transform;rider.SetParent(Visual.transform,false);
        var grip=Material(new Color(.10f,.12f,.14f));var salmon=Material(new Color(.94f,.40f,.27f));
        var metal=Material(new Color(.60f,.65f,.68f));var wheelsMat=Material(new Color(.94f,.90f,.78f));
        var pants=Material(new Color(.10f,.17f,.27f));var skin=Material(new Color(.75f,.53f,.37f));
        Part("Deck",PrimitiveType.Cube,board,new Vector3(0,.105f,0),new Vector3(.24f,.035f,.82f),grip);
        foreach(float z in new[]{-.40f,.40f})
        {
            var end=Part("Extremo_tabla",PrimitiveType.Cube,board,new Vector3(0,.125f,z),new Vector3(.24f,.03f,.16f),salmon);
            end.localRotation=Quaternion.Euler(z>0?-12:12,0,0);
        }
        foreach(float z in new[]{-.27f,.27f})
        {
            Part("Eje",PrimitiveType.Cube,board,new Vector3(0,.067f,z),new Vector3(.33f,.025f,.04f),metal);
            foreach(float x in new[]{-.16f,.16f})
            {
                var wheel=Part("Rueda",PrimitiveType.Cylinder,board,new Vector3(x,.035f,z),new Vector3(.07f,.024f,.07f),wheelsMat);
                wheel.localRotation=Quaternion.Euler(0,0,90);wheels.Add(wheel);
            }
        }
        Part("Torso",PrimitiveType.Capsule,rider,new Vector3(0,1.13f,0),new Vector3(.43f,.35f,.27f),salmon);
        Part("Cabeza",PrimitiveType.Sphere,rider,new Vector3(0,1.60f,0),Vector3.one*.25f,skin);
        Part("Casco",PrimitiveType.Sphere,rider,new Vector3(0,1.68f,0),new Vector3(.28f,.19f,.28f),grip);
        frontLeg=Part("Pierna_delantera",PrimitiveType.Capsule,rider,new Vector3(0,.55f,.21f),new Vector3(.16f,.31f,.16f),pants);
        rearLeg=Part("Pierna_trasera",PrimitiveType.Capsule,rider,new Vector3(0,.55f,-.21f),new Vector3(.16f,.31f,.16f),pants);
        foreach(float z in new[]{-.21f,.21f})Part("Zapatilla",PrimitiveType.Cube,rider,new Vector3(0,.19f,z),new Vector3(.28f,.09f,.12f),grip);
        foreach(float x in new[]{-.30f,.30f})
        {
            var arm=Part("Brazo",PrimitiveType.Capsule,rider,new Vector3(x,1.07f,0),new Vector3(.12f,.28f,.12f),skin);
            arm.localRotation=Quaternion.Euler(0,0,x>0?22:-22);
        }
    }

    public void Dispose()
    {
        Mounted=false;if(body!=null)body.stepOffset=originalStepOffset;
        if(Visual!=null){Visual.SetActive(false);Release(Visual);Visual=null;}
        foreach(var material in materials)if(material!=null)Release(material);
        materials.Clear();wheels.Clear();
    }
    private static void Release(Object value){if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    private void OnDestroy(){Dispose();}
}

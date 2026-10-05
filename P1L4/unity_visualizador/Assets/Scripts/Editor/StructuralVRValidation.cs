using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

[InitializeOnLoad]
public static class StructuralVRValidation
{
    private static double next;
    static StructuralVRValidation(){EditorApplication.update+=Tick;}
    public static void ValidateBatch()
    {
        AndroidVRBuild.ConfigureAndroid();StructuralARValidation.ValidateOrThrow();StructuralARSectorValidation.Validate();
        EditorSceneManager.OpenScene(StructuralARSceneSetup.ScenePath);
        EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView")).Show();
        SessionState.SetInt("MCOCVRValidation",1);EditorApplication.isPlaying=true;
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Tick()
    {
        int phase=SessionState.GetInt("MCOCVRValidation",0);
        if(phase==0||!EditorApplication.isPlaying||EditorApplication.timeSinceStartup<next)return;
        try
        {
            var vr=UnityEngine.Object.FindAnyObjectByType<StructuralVRController>();
            if(phase==1){Require(vr!=null,"Missing mode switch");vr.StartCoroutine(vr.EnterVR());}
            if(phase==2)
            {
                Require(vr.IsVR,"VR did not start in simulation");
                Require(Screen.autorotateToLandscapeLeft&&Screen.autorotateToLandscapeRight&&!Screen.autorotateToPortrait&&!Screen.autorotateToPortraitUpsideDown,"VR must support both horizontal orientations only");
                Require(vr.Floors.Length>=5,"Missing structural floors");
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                GameObject world=(GameObject)typeof(StructuralVRController).GetField("world",flags).GetValue(vr);
                var camera=(Camera)typeof(StructuralVRController).GetField("vrCamera",flags).GetValue(vr);
                var pose=camera.GetComponent<TrackedPoseDriver>();
                Require(pose!=null&&pose.trackingType==TrackedPoseDriver.TrackingType.RotationOnly,"Missing rotation-only eye tracking");
                Vector3 eyePosition=camera.transform.position;
                var simulatedHead=InputSystem.AddDevice<XRHMD>();
                pose.enabled=true;
                Quaternion turn=Quaternion.Euler(15,65,8);
                InputSystem.QueueDeltaStateEvent(simulatedHead.centerEyeRotation,turn);
                InputSystem.Update();
                Require(Quaternion.Angle(camera.transform.localRotation,turn)<.1f,"Center-eye rotation did not turn VR camera");
                Require(Vector3.Distance(camera.transform.position,eyePosition)<.001f,"Head rotation displaced floor position");
                pose.enabled=false;InputSystem.RemoveDevice(simulatedHead);
                Debug.Log("[VR tracking validation] PASS: centerEyeRotation yaw/pitch/roll changes view, position conserved.");
                Require(world.GetComponentInChildren<VisualSiteTerrain>()!=null && world.GetComponentInChildren<VisualFrameFacade>()!=null && world.GetComponentInChildren<VisualFlatRoof>()!=null && world.GetComponentInChildren<VisualStairs>()!=null,"Missing environment");
                var terrain=world.GetComponentInChildren<VisualSiteTerrain>(true);
                var facade=world.GetComponentInChildren<VisualFrameFacade>(true);
                var roof=world.GetComponentInChildren<VisualFlatRoof>(true);
                var stairs=world.GetComponentInChildren<VisualStairs>(true);
                Require(terrain.PlatformCount==3&&terrain.UpperTerrace.activeInHierarchy&&stairs.FlightCount==2&&facade.Panels.Count>0&&roof.Pieces.Count>0,"Incomplete environment geometry");
                var decor=terrain.GetComponentsInChildren<Renderer>(true).Concat(facade.GetComponentsInChildren<Renderer>(true)).Concat(roof.GetComponentsInChildren<Renderer>(true)).Concat(stairs.GetComponentsInChildren<Renderer>(true)).ToArray();
                Require(decor.All(r=>r.enabled&&r.gameObject.activeInHierarchy&&(camera.cullingMask&(1<<r.gameObject.layer))!=0&&r.sharedMaterials.All(m=>m.shader.name=="MCOC/VR Visual Environment")),"Decor is hidden, outside camera layers or using wrong shader");
                Debug.Log("[VR environment] PASS: 3 platforms, 2 stair flights, "+facade.Panels.Count+" salmon walls, "+roof.Pieces.Count+" roofs; all active, stereo shader and camera layers.");
                var member=world.GetComponentsInChildren<ElementSelectable>().Single(e=>e.data!=null&&e.data.elementTag=="E1_94");
                Require(member.data.id==94 && member.data.nodeI==61 && member.data.nodeJ==60,"IDs changed");
                typeof(StructuralVRController).GetMethod("SelectElement",flags).Invoke(vr,new object[]{member});
                Require(UnityData.TryGetSectionForces(94,"C1",.5f,out var forces)&&Mathf.Abs(forces.My+23.377838f)<.001f,"VR result mismatch");
                var diagrams=(System.Collections.Generic.List<LineRenderer>)typeof(StructuralVRController).GetField("diagrams",flags).GetValue(vr);
                Require(diagrams[0].positionCount==2&&diagrams[1].positionCount==41&&diagrams.All(d=>d.enabled&&(camera.cullingMask&(1<<d.gameObject.layer))!=0),"VR diagrams not rendered by camera");
                Vector3 middle=Vector3.Lerp(member.startPoint,member.endPoint,.5f);
                Require(Vector3.Dot(diagrams[1].GetPosition(20)-middle,Vector3.up)>0,"Negative beam moment must appear above axis");
                var column=world.GetComponentsInChildren<ElementSelectable>().First(e=>e.data!=null&&e.data.type=="columna"&&UnityData.TryGetSectionForces(e.data.id,"C1",.5f,out _));
                typeof(StructuralVRController).GetMethod("SelectElement",flags).Invoke(vr,new object[]{column});
                Require(diagrams[1].enabled&&diagrams[1].positionCount==41,"Column diagram missing");
                typeof(StructuralVRController).GetMethod("SelectElement",flags).Invoke(vr,new object[]{member});
                var rig=(GameObject)typeof(StructuralVRController).GetField("rig",flags).GetValue(vr);
                var menu=(Transform)typeof(StructuralVRController).GetField("menu",flags).GetValue(vr);
                var gaze=typeof(StructuralVRController).GetMethod("HandleGaze",flags);
                var move=typeof(StructuralVRController).GetMethod("TryMove",flags);
                var follow=typeof(StructuralVRController).GetMethod("FollowMenu",flags);
                var walking=typeof(StructuralVRController).GetField("walking",flags);
                var direction=typeof(StructuralVRController).GetField("movementDirection",flags);
                Vector3 position=rig.transform.position;
                foreach(var arrow in menu.GetComponentsInChildren<StructuralVRButton>().Where(b=>b.IsDirection))
                {
                    var collider=arrow.GetComponent<Collider>();
                    gaze.Invoke(vr,new object[]{collider,false,10f});gaze.Invoke(vr,new object[]{collider,false,10.79f});
                    Require(!(bool)walking.GetValue(vr),"Gaze movement started before 0.8 seconds");
                    gaze.Invoke(vr,new object[]{collider,false,10.81f});Require((bool)walking.GetValue(vr),"Direction gaze did not start movement");
                    Vector3 expected=menu.forward*arrow.Direction.y+Vector3.Cross(Vector3.up,menu.forward)*arrow.Direction.x;
                    Require(Vector3.Angle((Vector3)direction.GetValue(vr),expected)<.1f,"Wrong arrow direction");
                    Quaternion frozen=menu.rotation;camera.transform.rotation=Quaternion.Euler(0,menu.eulerAngles.y+60,0);follow.Invoke(vr,null);
                    Require(Quaternion.Angle(menu.rotation,frozen)<.1f&&Vector3.Angle((Vector3)direction.GetValue(vr),expected)<.1f,"Aim changed locked movement basis");
                    gaze.Invoke(vr,new object[]{null,false,11f});Require(!(bool)walking.GetValue(vr),"Looking away did not stop immediately");
                }
                camera.transform.rotation=Quaternion.identity;follow.Invoke(vr,null);
                direction.SetValue(vr,Vector3.right);Physics.SyncTransforms();
                bool stepped=(bool)move.Invoke(vr,new object[]{.05f});
                Require(stepped&&rig.transform.position.x>position.x,"Virtual step failed inside slab");
                Require(Mathf.Abs(rig.transform.position.x-position.x-.09f)<.001f,"VR walk speed should be 1.8 m/s");
                var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.layer=30;
                obstacle.transform.position=rig.transform.position+Vector3.right*.1f;obstacle.transform.localScale=new Vector3(.3f,3f,.3f);Physics.SyncTransforms();
                Vector3 beforeBlock=rig.transform.position;Require(!(bool)move.Invoke(vr,new object[]{.05f})&&rig.transform.position==beforeBlock,"Movement crossed obstacle");
                UnityEngine.Object.DestroyImmediate(obstacle);
                rig.transform.position=new Vector3(100000,position.y,100000);Physics.SyncTransforms();
                Require(!(bool)move.Invoke(vr,new object[]{.05f}),"Movement left structural slab");rig.transform.position=position;
                camera.transform.rotation=Quaternion.Euler(0,120,0);follow.Invoke(vr,null);
                Require(Vector3.Angle(menu.forward,Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up))<.1f,"Menu did not follow wide head turn");
                Require(Vector3.Distance(menu.position,camera.transform.position+menu.forward*2.6f+Vector3.down*.65f)<.001f,"Menu did not follow position");
                Debug.Log("[VR gaze navigation] PASS: four arrows, dwell, locked direction, immediate stop, slab/obstacle limits, following menu, beam/column diagrams visible and moment signs.");
                int start=vr.FloorIndex;vr.ChangeFloor(1);Require(vr.FloorIndex==start+1,"Floor up failed");
                vr.ChangeFloor(-1);Require(vr.FloorIndex==start,"Floor down failed");
                for(int i=0;i<20;i++)vr.ChangeFloor(-1);Require(vr.FloorIndex==0,"Floor lower bound failed");
                for(int i=0;i<20;i++)vr.ChangeFloor(1);Require(vr.FloorIndex==vr.Floors.Length-1,"Floor upper bound failed");
                vr.ChangeFloor(-2);
                typeof(StructuralVRController).GetMethod("ToggleEnvironment",flags).Invoke(vr,null);
                Require(!world.GetComponentInChildren<VisualSiteTerrain>(true).UpperTerrace.activeSelf,"Environment hiding failed");
                Require(decor.All(r=>!r.gameObject.activeInHierarchy),"Some decor remained visible when hidden");
                typeof(StructuralVRController).GetMethod("ToggleEnvironment",flags).Invoke(vr,null);
                Require(decor.All(r=>r.gameObject.activeInHierarchy),"Some decor did not return");
                vr.ExitVR();Require(!vr.IsVR&&UnityEngine.Object.FindAnyObjectByType<StructuralARController>().enabled,"AR restore failed");
            }
            if(phase==3){vr.StartCoroutine(vr.EnterVR());}
            if(phase==4){Require(vr.IsVR,"Second VR entry failed");var flags=BindingFlags.Instance|BindingFlags.NonPublic;var cam=(Camera)typeof(StructuralVRController).GetField("vrCamera",flags).GetValue(vr);var menu=(Transform)typeof(StructuralVRController).GetField("menu",flags).GetValue(vr);menu.gameObject.SetActive(false);cam.transform.position=new Vector3(20,38,-80);cam.transform.LookAt(new Vector3(20,4,4));}
            if(phase==5)ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath("Logs/iphone-vr-ambiente.png"));
            if(phase==6){vr.ExitVR();Debug.Log("[Structural VR Validation] PASS: AR/VR/AR twice; floors and bounds; environment; E1_94 ID/nodes/C1 conserved. Native stereo and tracking require an Android phone.");SessionState.SetInt("MCOCVRValidation",0);EditorApplication.Exit(0);return;}
            SessionState.SetInt("MCOCVRValidation",phase+1);next=EditorApplication.timeSinceStartup+3;
        }
        catch(Exception error){SessionState.SetInt("MCOCVRValidation",0);Debug.LogException(error);EditorApplication.Exit(1);}
    }
}

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class DesktopSkateValidation
{
    public static void ValidateBatch()
    {
        GameObject model=null,cameraObject=null,fixture=null;
        try
        {
            model=new GameObject("Skate validation");var viewer=model.AddComponent<StructureViewer>();
            cameraObject=new GameObject("Skate test camera");var camera=cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(80,40,-60),Quaternion.Euler(20,40,0));
            var savedPosition=camera.transform.position;var savedRotation=camera.transform.rotation;
            var walk=model.AddComponent<DesktopWalkthrough>();walk.Configure(viewer,UnityData.Structure);walk.Enter(camera);
            Require(walk.Active && walk.Skate!=null,"Skate not attached to PC game mode");
            Vector3 exteriorSpawn=walk.Body.transform.position;
            fixture=new GameObject("Synthetic skate test course");
            Box(fixture,"Level surface",new Vector3(500,99.9f,500),new Vector3(100,.2f,100));
            Physics.SyncTransforms();walk.Teleport(new Vector3(500,100.05f,500));
            Settle(walk);Require(walk.ToggleSkate() && walk.Skate.Mounted,"Cannot mount on ground");
            var skate=walk.Skate;Require(skate.Visual.activeSelf,"Board and rider are invisible");
            Require(!skate.Visual.GetComponentsInChildren<Collider>().Any(c=>c.enabled),"Visual skate adds collision obstacles");
            Require(Mathf.Abs(walk.Body.stepOffset-.08f)<.001f,"Skate cannot auto-step up stair flights");
            skate.UpdateCamera(Vector2.zero,0);
            Require(Vector3.Distance(camera.transform.position,walk.Body.transform.position)>2,"Skate camera is not third person");
            float yaw=walk.Body.transform.eulerAngles.y;
            skate.UpdateCamera(new Vector2(35,0),.02f);
            Require(Mathf.Abs(Mathf.DeltaAngle(yaw,walk.Body.transform.eulerAngles.y))<.01f,"Mouse camera changes travel direction");
            Vector3 start=walk.Body.transform.position;
            Frames(walk,Vector2.up,120);float pushedSpeed=skate.Speed;
            Require(pushedSpeed>6 && Vector3.Distance(start,walk.Body.transform.position)>5,"Pushing does not accelerate");
            var coastStart=walk.Body.transform.position;Frames(walk,Vector2.zero,30);
            Require(skate.Speed>pushedSpeed*.8f && Vector3.Distance(coastStart,walk.Body.transform.position)>2,"Releasing W loses inertia");
            Frames(walk,Vector2.down,100);Require(skate.Speed<.05f,"S must brake to a stop");
            Frames(walk,Vector2.up,60);float straightHeading=walk.Body.transform.eulerAngles.y;
            Frames(walk,Vector2.right,30);
            Require(Mathf.Abs(Mathf.DeltaAngle(straightHeading,walk.Body.transform.eulerAngles.y))>30,"A/D do not steer");
            walk.Teleport(new Vector3(500,100.05f,500));Frames(walk,Vector2.zero,15);Frames(walk,Vector2.up,40);
            float baseY=walk.Body.transform.position.y,topY=baseY;Vector3 beforeOllie=walk.Body.transform.position;
            walk.Step(Vector2.zero,true,false,.02f);
            Require(!walk.ToggleSkate(),"Must not mount/dismount in midair");
            for(int i=0;i<75;i++){walk.Step(Vector2.zero,false,false,.02f);topY=Mathf.Max(topY,walk.Body.transform.position.y);}
            Require(topY-baseY>.5f && walk.Body.isGrounded && Mathf.Abs(walk.Body.transform.position.y-baseY)<.1f,"Ollie does not land correctly");
            Require(Vector3.Distance(beforeOllie,walk.Body.transform.position)>2,"Ollie loses forward momentum");
            foreach(var trick in new[]{DesktopSkateController.Trick.Kickflip,DesktopSkateController.Trick.ShoveIt})
            {
                walk.Teleport(new Vector3(500,100.05f,500));Frames(walk,Vector2.zero,15);Frames(walk,Vector2.up,40);
                var beforeTrick=walk.Body.transform.position;
                Require(skate.RequestTrick(trick),"Cannot request "+trick);walk.Step(Vector2.zero,false,false,.02f);
                Require(skate.ActiveTrick==trick && !skate.RequestTrick(trick),"Trick not started or can be stacked infinitely");
                bool rotated=false;
                for(int i=0;i<75;i++)
                {
                    walk.Step(Vector2.zero,false,false,.02f);
                    if(skate.ActiveTrick==trick && skate.TrickProgress>.25f && skate.TrickProgress<.75f)
                        rotated|=Quaternion.Angle(skate.Visual.transform.Find("Tabla").localRotation,Quaternion.identity)>60;
                }
                Require(rotated && skate.LastLandedTrick==trick && skate.ActiveTrick==DesktopSkateController.Trick.None && walk.Body.isGrounded,"Trick rotation/landing failed: "+trick);
                Require(Vector3.Distance(beforeTrick,walk.Body.transform.position)>2,"Trick loses travel momentum: "+trick);
                Require(Quaternion.Angle(skate.Visual.transform.Find("Tabla").localRotation,Quaternion.identity)<2,"Board remains flipped after landing");
            }
            walk.SetPaused(true);Vector3 paused=walk.Body.transform.position;float pausedSpeed=skate.Speed;
            Frames(walk,Vector2.up,10);Require(walk.Body.transform.position==paused && Mathf.Abs(skate.Speed-pausedSpeed)<.001f,"Pause moves skate");walk.SetPaused(false);
            walk.Teleport(new Vector3(500,100.05f,500));Frames(walk,Vector2.zero,15);
            // A flat wall must stop velocity into the obstacle, rather than accumulating speed.
            var wall=Box(fixture,"Wall",new Vector3(504,101,500),new Vector3(.25f,2,15));
            walk.Body.transform.rotation=Quaternion.Euler(0,90,0);skate.ResetMotion();Physics.SyncTransforms();
            Frames(walk,Vector2.up,200);
            Require(walk.Body.transform.position.x<503.7f && skate.Speed<.15f,"Skate crosses a wall or retains blocked velocity: "+walk.Body.transform.position+" speed="+skate.Speed);
            wall.SetActive(false);
            // A sloped surface must pull the board downhill without pressing W.
            var slope=Box(fixture,"Slope fixture",new Vector3(530,104,530),new Vector3(20,.3f,12));slope.transform.rotation=Quaternion.Euler(0,0,-12);
            Physics.SyncTransforms();
            Require(Physics.Raycast(new Vector3(530,110,530),Vector3.down,out var floor,20,1<<2),"Missing slope");
            walk.Teleport(floor.point+Vector3.up*.04f);Frames(walk,Vector2.zero,15);Frames(walk,Vector2.zero,80);
            Require(skate.Speed>1.5f && walk.Body.transform.position.x>531,"Slope does not accelerate board downhill");
            // Real lawn-to-earth approaches: all three terrace heights, in both directions.
            var supportIds=UnityData.Structure.supports.Select(s=>s.node).ToHashSet();
            float front=UnityData.Structure.nodes.Where(n=>supportIds.Contains(n.id)).Min(n=>n.y)-12f;
            foreach(float x in new[]{-25f,12f,50f})
            {
                Require(Physics.Raycast(new Vector3(x,80,front+2),Vector3.down,out var lawn,150,1<<2),"Missing real lawn "+x);
                Require(lawn.collider.name.StartsWith("Recorrido_Pasto_"),"Test route must start on lawn, not stairs or furniture: "+lawn.collider.name);
                walk.Teleport(lawn.point+Vector3.up*.04f);walk.Body.transform.rotation=Quaternion.Euler(0,180,0);skate.ResetMotion();Frames(walk,Vector2.zero,20);
                float largestStep=0,previousY=walk.Body.transform.position.y;
                for(int i=0;i<145;i++)
                {walk.Step(Vector2.up,false,false,.02f);largestStep=Mathf.Max(largestStep,Mathf.Abs(walk.Body.transform.position.y-previousY));previousY=walk.Body.transform.position.y;}
                Require(walk.Body.transform.position.z<front-5 && walk.Body.isGrounded,"Cannot skate from lawn onto earth at X="+x+": "+walk.Body.transform.position+" speed="+skate.Speed);
                Require(largestStep<.28f,"Earth approach has an abrupt step/drop at X="+x+": "+largestStep);
                walk.Body.transform.rotation=Quaternion.identity;skate.ResetMotion();Frames(walk,Vector2.up,155);
                Require(walk.Body.transform.position.z>front+.5f && walk.Body.isGrounded,"Earth approach blocks return onto lawn at X="+x+": "+walk.Body.transform.position);
            }
            walk.Teleport(new Vector3(500,100.05f,500));Frames(walk,Vector2.zero,15);
            // Put a wall behind the player to verify the orbit camera retracts before it.
            var cameraWall=Box(fixture,"Camera obstruction",new Vector3(498.5f,101,500),new Vector3(.2f,3,8));
            walk.Body.transform.rotation=Quaternion.Euler(0,90,0);skate.ResetMotion();Physics.SyncTransforms();skate.UpdateCamera(Vector2.zero,0);
            Require(camera.transform.position.x>498.7f,"Third-person camera crosses wall");cameraWall.SetActive(false);
            // Capture real environment with the procedural board and rider.
            walk.Teleport(exteriorSpawn);Frames(walk,Vector2.zero,20);skate.UpdateCamera(new Vector2(85,0),0);
            Capture(camera,"Logs/desktop-skate-preview.png");
            Require(skate.RequestTrick(DesktopSkateController.Trick.Kickflip),"Kickflip unavailable in real environment");
            Frames(walk,Vector2.zero,12);skate.UpdateCamera(Vector2.zero,0);Capture(camera,"Logs/desktop-kickflip-preview.png");
            Frames(walk,Vector2.zero,65);
            Require(walk.ToggleSkate() && !skate.Mounted && !skate.Visual.activeSelf,"Cannot return to walking");
            Require(Mathf.Abs(camera.transform.localPosition.y-1.62f)<.001f && Mathf.Abs(walk.Body.stepOffset-.42f)<.001f,"First-person camera/steps not restored");
            Frames(walk,Vector2.zero,20);float walkingBase=walk.Body.transform.position.y;
            walk.Step(Vector2.zero,true,false,.02f);float walkingTop=walk.Body.transform.position.y;
            for(int i=0;i<80;i++){walk.Step(Vector2.zero,false,false,.02f);walkingTop=Mathf.Max(walkingTop,walk.Body.transform.position.y);}
            Require(walkingTop-walkingBase>.75f && walk.Body.isGrounded,"Walking jump regressed after dismount");
            walk.Exit();Require(!DesktopWalkthrough.IsActive && walk.Skate==null,"Skate persists outside game mode");
            Require(Vector3.Distance(camera.transform.position,savedPosition)<.001f && Quaternion.Angle(camera.transform.rotation,savedRotation)<.001f,"Visualizer camera not restored");
            Debug.Log("[PC Skate] PASS: mount/dismount; board/rider; orbit; acceleration/inertia/braking/steering; ollie; kickflip and shove-it rotations with momentum/landing; pause; walls; downhill gravity; real earth approaches both ways at three platform heights; camera obstruction; walking restoration; clean exit. No structural edits.");
            Cleanup(model,cameraObject,fixture);EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);Cleanup(model,cameraObject,fixture);EditorApplication.Exit(1);}
    }
    private static void Settle(DesktopWalkthrough walk){Frames(walk,Vector2.zero,20);}
    private static void Frames(DesktopWalkthrough walk,Vector2 input,int count){for(int i=0;i<count;i++)walk.Step(input,false,false,.02f);}
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static GameObject Box(GameObject parent,string name,Vector3 center,Vector3 size)
    {var box=new GameObject(name);box.layer=2;box.transform.SetParent(parent.transform);box.transform.position=center;box.AddComponent<BoxCollider>().size=size;return box;}
    private static void Cleanup(params GameObject[] objects){foreach(var obj in objects)if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);}
    private static void Capture(Camera camera,string path)
    {
        var lamp=new GameObject("Skate preview light");var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;lamp.transform.rotation=Quaternion.Euler(40,-35,0);
        var target=new RenderTexture(1280,800,24);camera.targetTexture=target;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1280,800,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();System.IO.File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(lamp);
    }
}

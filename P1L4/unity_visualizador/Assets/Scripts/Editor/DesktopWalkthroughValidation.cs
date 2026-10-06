using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class DesktopWalkthroughValidation
{
    public static void ValidateBatch()
    {
        GameObject model=null,camObject=null;
        try
        {
            model=new GameObject("Walkthrough physics validation");
            var viewer=model.AddComponent<StructureViewer>();
            camObject=new GameObject("Walkthrough test camera");var camera=camObject.AddComponent<Camera>();
            camera.transform.position=new Vector3(80,40,-60);camera.transform.rotation=Quaternion.Euler(20,40,0);
            var savedPosition=camera.transform.position;var savedRotation=camera.transform.rotation;
            var walk=model.AddComponent<DesktopWalkthrough>();walk.Configure(viewer,UnityData.Structure);
            var stairs=model.GetComponentInChildren<VisualStairs>();
            var exit=stairs.Paths.Single(p=>p.Name=="Escalera_E1_220_Terraza_Y4");
            Require(Mathf.Abs(exit.End.x-30)<.001f,"E1_220 stairs must arrive at column E1_281 (X30)");
            var rail=model.GetComponentInChildren<VisualSalmonRailings>();
            Require(rail.Routes.Any(r=>Mathf.Abs(r[0].x-exit.Start.x)<.001f && Mathf.Abs(r[1].x-30)<.001f && r[1].z<exit.End.z),"Stair railing did not move with arrival");
            var original=model.GetComponentsInChildren<Collider>(true).ToDictionary(c=>c,c=>c.enabled);
            walk.Enter(camera);Require(walk.Active && DesktopWalkthrough.IsActive,"Cannot enter walkthrough");
            Require(walk.CollisionWorld.GetComponentsInChildren<Collider>().Length>100,"Navigation world incomplete");
            Require(walk.FloorHeights.Contains(-4f) && walk.FloorHeights.Length==6,"Basement must be accessible in floor selector");
            foreach(float floor in walk.FloorHeights)
            {
                Require(walk.GoToFloor(floor),"No unobstructed spawn at floor "+floor);
                for(int i=0;i<90;i++)walk.Step(Vector2.zero,false,false,1f/60);
                Require(walk.Body.isGrounded && walk.Body.transform.position.y>floor-.65f && walk.Body.transform.position.y<floor+.65f,"Gravity/floor collision failed on "+floor);
            }
            walk.GoToFloor(0);for(int i=0;i<30;i++)walk.Step(Vector2.zero,false,false,1f/60);
            float baseY=walk.Body.transform.position.y,maxY=baseY;
            for(int i=0;i<110;i++)
            {walk.Step(Vector2.zero,i==0,false,1f/60);maxY=Mathf.Max(maxY,walk.Body.transform.position.y);}
            Require(maxY-baseY>.75f && walk.Body.isGrounded && Mathf.Abs(walk.Body.transform.position.y-baseY)<.1f,"Jump must rise and return to ground");
            VisualCafe.TryLayout(UnityData.Structure,out var cafe);
            // Both facade doorways must fit the standing capsule at the cafe's real floor.
            foreach(float z in new[]{-7.25f,8.9f})
            {
                var feet=new Vector3(cafe.Left+5f,.1f,z);
                Require(!Physics.CheckCapsule(feet+Vector3.up*.28f,feet+Vector3.up*1.47f,.25f,1<<2),"Cafe doorway is blocked in walkthrough at Z"+z);
            }
            var access=stairs.Paths.Single(p=>p.Name=="Escalera_Y4_cafeteria_normal");
            walk.Teleport(access.End+Vector3.up*.08f);for(int i=0;i<20;i++)walk.Step(Vector2.zero,false,false,.02f);
            for(int i=0;i<190;i++)walk.Step(Vector2.up,false,false,.02f);
            Require(walk.Body.transform.position.x>29.5f && walk.Body.transform.position.y>3.9f,"Cannot climb normal terrace stairs: "+walk.Body.transform.position);
            foreach(var flight in stairs.Paths.Where(p=>p.Name.StartsWith("Escalera_E1_")))
            {
                walk.Teleport(flight.End+Vector3.up*.1f);for(int i=0;i<30;i++)walk.Step(Vector2.zero,false,false,.02f);
                int frames=Mathf.CeilToInt((flight.End.x-flight.Start.x+.2f)/walk.walkingSpeed/.02f);
                for(int i=0;i<frames;i++)walk.Step(Vector2.down,false,false,.02f);
                Require(walk.Body.transform.position.x<flight.Start.x+.3f && walk.Body.transform.position.y>flight.Start.y-.15f,"Cannot climb cantilever stair "+flight.Name+": "+walk.Body.transform.position);
            }
            walk.Teleport(Vector3.Lerp(access.Start,access.End,.5f)+Vector3.up*.1f);
            for(int i=0;i<45;i++)walk.Step(Vector2.left,false,false,.02f); // Heading +X: left is +Z, towards solid rail.
            Require(walk.Body.transform.position.z<access.Start.z+access.Width/2-.15f,"Player crossed closed stair rail");
            walk.GoToFloor(8);for(int i=0;i<30;i++)walk.Step(Vector2.zero,false,false,.02f);
            Vector3 best=Vector3.forward;float distance=-1;
            for(int heading=0;heading<360;heading+=45)
            {
                Vector3 direction=Quaternion.Euler(0,heading,0)*Vector3.forward;
                float clear=Physics.Raycast(camera.transform.position,direction,out var obstruction,25,1<<2)?obstruction.distance:25;
                if(clear>distance){distance=clear;best=direction;}
            }
            camera.transform.rotation=Quaternion.LookRotation(best);Capture(camera,"Logs/desktop-walkthrough-interior.png");
            walk.SetPaused(true);var pausedPosition=walk.Body.transform.position;
            walk.Step(Vector2.up,true,true,.02f);Require(walk.Body.transform.position==pausedPosition,"Paused menu must stop movement");
            walk.Exit();Require(!DesktopWalkthrough.IsActive && walk.CollisionWorld==null,"Walking physics persists after exit");
            Require(Vector3.Distance(camera.transform.position,savedPosition)<.001f && Quaternion.Angle(camera.transform.rotation,savedRotation)<.001f,"Orbit camera pose was not restored");
            Require(original.All(p=>p.Key.enabled==p.Value),"Original collider states not restored");
            camera.transform.position=new Vector3(-8,28,-45);camera.transform.LookAt(new Vector3(20,5,-10));
            Capture(camera,"Logs/desktop-terrace-stairs-preview.png");
            Debug.Log("[PC walkthrough] PASS: stairs/rail moved to E1_281; complete collision world; six floor spawns including basement; gravity; jump/landing; normal and cantilever stair ascent; both cafe doors passable; solid rail blocks; pause; camera/colliders restored on exit.");
            UnityEngine.Object.DestroyImmediate(model);UnityEngine.Object.DestroyImmediate(camObject);
            EditorApplication.Exit(0);
        }
        catch(Exception error)
        {
            Debug.LogException(error);if(model!=null)UnityEngine.Object.DestroyImmediate(model);if(camObject!=null)UnityEngine.Object.DestroyImmediate(camObject);
            EditorApplication.Exit(1);
        }
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Capture(Camera camera,string path)
    {
        var lightObject=new GameObject("Walkthrough preview lighting");var light=lightObject.AddComponent<Light>();
        light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(40,-35,0);
        var target=new RenderTexture(1280,800,24);camera.targetTexture=target;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1280,800,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();System.IO.File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(lightObject);
    }
}

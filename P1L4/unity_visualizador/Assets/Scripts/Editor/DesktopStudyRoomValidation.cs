using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class DesktopStudyRoomValidation
{
    public static void ValidateBatch()
    {
        GameObject model=null,camObject=null;
        try
        {
            model=new GameObject("Study room validation");var viewer=model.AddComponent<StructureViewer>();
            var facade=model.GetComponentInChildren<VisualFrameFacade>();
            foreach(var pair in new[]{new[]{"E1_230","E1_243"},new[]{"E1_255","E1_257"}})
                Require(facade.Panels.Any(p=>p.name.Contains(pair[0]+"_") && p.name.Contains(pair[1]+"_")),"Missing requested window "+pair[0]+" / "+pair[1]);
            var room=model.GetComponentInChildren<VisualStudyRoom>();
            Require(room!=null && room.Tables.Count==4,"Room requires four tables");
            var tables=room.Tables.OrderBy(t=>t.position.z).ThenBy(t=>t.position.x).ToArray();
            Require(Mathf.Abs(tables[0].position.x-tables[2].position.x)<.001f && Mathf.Abs(tables[1].position.x-tables[3].position.x)<.001f &&
                Mathf.Abs(tables[0].position.z-tables[1].position.z)<.001f && Mathf.Abs(tables[2].position.z-tables[3].position.z)<.001f,"Tables must form a regular 2x2 grid");
            Require(room.GetComponentsInChildren<Transform>().Count(t=>t.name=="Laptop_sala")==4 &&
                room.GetComponentsInChildren<Transform>().Count(t=>t.name=="Imagen_laptop")==4,"Laptop screens missing");
            Require(room.GetComponentsInChildren<Collider>().Length==0 && room.GetComponentsInChildren<ElementSelectable>().Length==0,"Room must remain decorative");
            foreach(var renderer in room.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("Muro_visual") || r.name.StartsWith("Dintel")))
                Require(renderer.sharedMaterial.name=="Cemento_sala_visual","Room walls must be cement colored");
            var cafe=model.GetComponentInChildren<VisualCafe>();
            var cafeTables=cafe.GetComponentsInChildren<Transform>().Where(t=>t.name=="Mesa").ToArray();
            Require(cafeTables.Length==29,"Cafe table count changed");
            VisualCafe.TryLayout(UnityData.Structure,out var layout);
            foreach(var table in cafeTables)
                if(table.position.z<layout.Rear)Require(Mathf.Abs(table.position.x-(layout.Left+5))>=2.04f,"Table blocks cafe circulation");
            for(int i=0;i<cafeTables.Length;i++)for(int j=i+1;j<cafeTables.Length;j++)
                Require(Mathf.Abs(cafeTables[i].position.x-cafeTables[j].position.x)>=2.59f || Mathf.Abs(cafeTables[i].position.z-cafeTables[j].position.z)>=2.59f,"Cafe chairs overlap");
            camObject=new GameObject("Room navigation camera");var camera=camObject.AddComponent<Camera>();
            var walk=model.AddComponent<DesktopWalkthrough>();walk.Configure(viewer,UnityData.Structure);walk.Enter(camera);
            var feet=room.DoorCenter-Vector3.up*1.1f+Vector3.up*.01f;
            Require(!Physics.CheckCapsule(feet+Vector3.up*.28f,feet+Vector3.up*1.47f,.25f,1<<2),"Door over E1_8 is blocked");
            Require(Physics.Raycast(new Vector3(1,6,-3.6f),Vector3.left,2,1<<2),"Room wall missing navigation collision proxy");
            walk.Exit();
            Capture(camera,room.transform);
            Debug.Log("[Study room] PASS: two requested windows; three cement enclosure sides; clear door above E1_8; four equidistant tables and laptops; nonstructural decoration; cafe tables/chairs clear circulation and each other; game door passable.");
            UnityEngine.Object.DestroyImmediate(model);UnityEngine.Object.DestroyImmediate(camObject);DesktopCafeValidation.ValidateBatch();
        }
        catch(Exception e)
        {Debug.LogException(e);if(model!=null)UnityEngine.Object.DestroyImmediate(model);if(camObject!=null)UnityEngine.Object.DestroyImmediate(camObject);EditorApplication.Exit(1);}
    }
    private static void Capture(Camera camera,Transform room)
    {
        camera.transform.position=new Vector3(3.75f,7,-10);camera.transform.LookAt(new Vector3(3.75f,5.5f,-3));camera.farClipPlane=120;
        var go=new GameObject("Study preview light");var light=go.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;go.transform.rotation=Quaternion.Euler(45,-30,0);
        var target=new RenderTexture(1280,800,24);camera.targetTexture=target;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1280,800,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();System.IO.File.WriteAllBytes("Logs/study-room-preview.png",image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(go);
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}

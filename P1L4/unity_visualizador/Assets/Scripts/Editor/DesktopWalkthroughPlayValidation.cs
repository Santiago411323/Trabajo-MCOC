using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class DesktopWalkthroughPlayValidation
{
    private const string Key="MCOC.WalkthroughPlayValidation";
    static DesktopWalkthroughPlayValidation(){EditorApplication.update+=Check;}
    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/StructureViewerScene.unity",OpenSceneMode.Single);
        SessionState.SetString(Key+"Start",DateTime.UtcNow.Ticks.ToString());
        SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
    }
    private static void Check()
    {
        if(SessionState.GetBool(Key+"Finished",false) && !EditorApplication.isPlaying)
        {
            SessionState.SetBool(Key+"Finished",false);EditorApplication.Exit(SessionState.GetInt(Key+"Code",1));return;
        }
        if(!SessionState.GetBool(Key,false))return;
        double elapsed=(DateTime.UtcNow.Ticks-long.Parse(SessionState.GetString(Key+"Start","0")))/(double)TimeSpan.TicksPerSecond;
        if(elapsed>90){Finish(1);return;}
        if(!EditorApplication.isPlaying || Time.timeSinceLevelLoad<3)return;
        try
        {
            var walk=UnityEngine.Object.FindObjectOfType<DesktopWalkthrough>();
            if(walk==null)throw new Exception("PC scene did not attach the game controller on Play");
            walk.Enter();
            if(!walk.Active || walk.Body==null || !DesktopWalkthrough.IsActive)throw new Exception("Game mode failed in actual Play Mode");
            for(int i=0;i<30;i++)walk.Step(Vector2.zero,false,false,.02f);
            Vector3 startFeet=walk.Body.transform.position;
            if(!walk.ToggleSkate() || !walk.Skate.Mounted)throw new Exception("Cannot mount skate in Play Mode");
            for(int i=0;i<30;i++)walk.Step(Vector2.up,false,false,.02f);
            if(walk.Skate.Speed<1.5f)throw new Exception("Skate does not accelerate in Play Mode");
            walk.Skate.UpdateCamera(Vector2.zero,0);
            if(Vector3.Distance(Camera.main.transform.position,walk.Body.transform.position)<2)throw new Exception("Skate camera did not switch to third person");
            walk.Teleport(startFeet);for(int i=0;i<20;i++)walk.Step(Vector2.zero,false,false,.02f);
            foreach(var trick in new[]{DesktopSkateController.Trick.Kickflip,DesktopSkateController.Trick.ShoveIt})
            {
                if(!walk.Skate.RequestTrick(trick))throw new Exception("Cannot request trick in Play Mode: "+trick);
                for(int i=0;i<75;i++)walk.Step(Vector2.zero,false,false,.02f);
                if(walk.Skate.LastLandedTrick!=trick || !walk.Body.isGrounded)throw new Exception("Trick does not land in Play Mode: "+trick);
            }
            walk.SetPaused(true);
            if(!walk.Paused || Camera.main.GetComponent<OrbitCamera>().enabled)throw new Exception("Orbit camera and game camera are active together");
            var pausedPosition=walk.Body.transform.position;walk.Step(Vector2.up,true,false,.02f);
            if(walk.Body.transform.position!=pausedPosition)throw new Exception("Paused skate keeps moving");
            walk.SetPaused(false);
            for(int i=0;i<60;i++)walk.Step(Vector2.down,false,false,.02f);
            if(!walk.ToggleSkate() || walk.Skate.Mounted)throw new Exception("Cannot dismount skate in Play Mode");
            if(Mathf.Abs(Camera.main.transform.localPosition.y-1.62f)>.001f)throw new Exception("Walking camera not restored");
            walk.Exit();
            if(DesktopWalkthrough.IsActive || !Camera.main.GetComponent<OrbitCamera>().enabled)throw new Exception("Cannot return to visualizer in Play Mode");
            Debug.Log("[PC Play Mode] PASS: scene initializes game mode; walking; skate mounts/accelerates; third-person camera; kickflip/shove-it land; pause; brake; dismount restores first person; restores orbit camera on exit.");
            Finish(0);
        }
        catch(Exception error){Debug.LogException(error);Finish(1);}
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key,false);SessionState.SetInt(Key+"Code",code);SessionState.SetBool(Key+"Finished",true);
        if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;
        else EditorApplication.Exit(code);
    }
}

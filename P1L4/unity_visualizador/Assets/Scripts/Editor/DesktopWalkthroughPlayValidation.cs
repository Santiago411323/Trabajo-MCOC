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
            walk.SetPaused(true);
            if(!walk.Paused || Camera.main.GetComponent<OrbitCamera>().enabled)throw new Exception("Orbit camera and game camera are active together");
            walk.Exit();
            if(DesktopWalkthrough.IsActive || !Camera.main.GetComponent<OrbitCamera>().enabled)throw new Exception("Cannot return to visualizer in Play Mode");
            Debug.Log("[PC Play Mode] PASS: scene initializes game mode automatically; enters first person with collisions; pauses; restores orbit camera and exits.");
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

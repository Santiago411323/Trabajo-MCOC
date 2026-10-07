using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class DesktopGazeFacadeValidation
{
    private const string Key="MCOC.GazeFacadeValidation";
    static DesktopGazeFacadeValidation(){EditorApplication.update+=Tick;}
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/StructureViewerScene.unity",OpenSceneMode.Single);
        SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
    }
    private static bool Pair(GameObject panel,string a,string b)=>panel.name.Contains(a+"_")&&panel.name.Contains(b+"_");
    private static void Tick()
    {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||Time.timeSinceLevelLoad<3)return;
        SessionState.SetBool(Key,false);
        try
        {
            var viewer=UnityEngine.Object.FindAnyObjectByType<StructureViewer>();
            var facade=viewer.GetComponentInChildren<VisualFrameFacade>();
            Require(!facade.Panels.Any(p=>Pair(p,"E1_271","E1_259")),"Window to remove remains");
            foreach(var tags in new[]{new[]{"E1_259","E1_310"},new[]{"E1_271","E1_311"},new[]{"E1_311","E1_310"}})
            {
                var panel=facade.Panels.Single(p=>Pair(p,tags[0],tags[1]));
                Require(panel.transform.Find("Vidrio")!=null&&panel.transform.Find("Montante")!=null,"Window style missing");
                Require(panel.GetComponentsInChildren<Collider>().Length==0&&panel.GetComponentsInChildren<ElementSelectable>().Length==0,"Decorative windows have structural effects");
                Require(panel.GetComponentInChildren<Renderer>().bounds.min.y>12&&panel.GetComponentInChildren<Renderer>().bounds.max.y<16,"Wrong story");
            }
            foreach(var pair in new[]{new[]{"E1_258","E1_270"},new[]{"E1_245","E1_259"},new[]{"E1_271","E1_283"},new[]{"E1_241","E1_254"},new[]{"E1_255","E1_257"}})
                Require(facade.Panels.Any(p=>Pair(p,pair[0],pair[1])),"Unrelated window removed: "+pair[0]);
            var walk=viewer.GetComponent<DesktopWalkthrough>();var picker=UnityEngine.Object.FindAnyObjectByType<ElementPicker>();
            var panelResults=viewer.GetComponent<ElementResultsPanel>();var diagrams=viewer.GetComponent<DiagramController>();
            var originalSelection=picker.Selected;bool originalPickerEnabled=picker.enabled,originalPanelEnabled=panelResults.enabled;
            var column=viewer.GetComponentsInChildren<ElementSelectable>().Single(e=>e.data!=null&&e.data.elementTag=="E1_259");
            var beam=viewer.GetComponentsInChildren<ElementSelectable>().Single(e=>e.data!=null&&e.data.elementTag=="E1_94");
            var originalColliders=viewer.GetComponentsInChildren<Collider>(true).ToDictionary(c=>c,c=>c.enabled);
            walk.Enter();Require(walk.Active&&!walk.GazeSelectionEnabled,"Inspection must default off");
            Aim(column);walk.RefreshGazeSelection();Require(picker.Selected==originalSelection,"Selection changed with inspection off");
            walk.SetGazeSelection(true);
            bool axialShortcut=(bool)typeof(DiagramController).GetMethod("PressedKey",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(diagrams,new object[]{KeyCode.Alpha1});
            Require(walk.GazeSelectionEnabled&&!axialShortcut,"Inspection not enabled or key1 not reserved for game mode");
            walk.RefreshGazeSelection();Require(walk.GazeTarget==column&&picker.Selected==column&&picker.enabled&&panelResults.enabled,"Look at column failed");
            Aim(beam);walk.RefreshGazeSelection();Require(walk.GazeTarget==beam&&picker.Selected==beam,"Look at beam failed");
            walk.SetPaused(true);Aim(column);walk.RefreshGazeSelection();Require(picker.Selected==beam,"Paused inspection changed selection");walk.SetPaused(false);
            var blocker=new GameObject("Test opaque wall");blocker.layer=2;blocker.transform.position=Vector3.Lerp(Camera.main.transform.position,(column.startPoint+column.endPoint)/2,.5f);
            blocker.AddComponent<BoxCollider>().size=Vector3.one*.2f;Physics.SyncTransforms();walk.RefreshGazeSelection();
            Require(walk.GazeTarget==null&&picker.Selected==beam,"Inspection sees through walls");blocker.SetActive(false);UnityEngine.Object.Destroy(blocker);
            walk.SetGazeSelection(false);Require(!walk.GazeSelectionEnabled&&picker.Selected==originalSelection&&!picker.enabled&&!panelResults.enabled,"Disable did not restore selection");
            walk.Exit();Require(picker.enabled==originalPickerEnabled&&panelResults.enabled==originalPanelEnabled&&originalColliders.All(c=>c.Key.enabled==c.Value),"Exit did not restore visualizer/colliders");
            walk.Enter();Require(!walk.GazeSelectionEnabled,"Reenter retained inspection");walk.Exit();
            Debug.Log("[Gaze/facade] PASS: only requested upper window removed, three matching windows added, other windows preserved; gaze activation/deactivation, beam/column selection, collision occlusion, paused menu, results panels, key1 shortcut isolation, exit/reentry restoration.");
            EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    private static void Aim(ElementSelectable member)
    {
        var camera=Camera.main;var center=(member.startPoint+member.endPoint)/2;
        var lateral=Vector3.Cross(member.endPoint-member.startPoint,Vector3.up).normalized;
        if(lateral.sqrMagnitude<.1f)lateral=Vector3.forward;
        camera.transform.position=center+lateral*.9f;camera.transform.LookAt(center);Physics.SyncTransforms();
    }
}

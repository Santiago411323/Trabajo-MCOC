using UnityEngine;
using UnityEngine.XR.ARSubsystems;

public sealed partial class StructuralARController
{
    private bool mobileSeismicWasActive;
    private bool TryDisplayedSection(float t,out FrameSectionForces value) => MobileSeismicPlayback.IsActive
        ? MobileSeismicPlayback.Instance.TrySection(selectedElement.id,t,out value)
        : UnityData.TryGetSectionForces(selectedElement.id,activeCombo,t,out value);
    private void LateUpdate()
    {
        bool active=MobileSeismicPlayback.IsActive;
        if(active && calibrationStage==CalibrationStage.Locked && structuralAnchor!=null && structuralAnchor.trackingState==TrackingState.Tracking)
        {
            UpdateDiagram();
            foreach(var item in placements)
                if(item!=currentPlacement && item.Diagram!=null)item.Diagram.enabled=false;
        }
        else if(!active && mobileSeismicWasActive)
        {
            foreach(var item in placements)if(item.Diagram!=null)item.Diagram.enabled=item.HasResults;
            if(structuralAnchor!=null && structuralAnchor.trackingState==TrackingState.Tracking)UpdateDiagram();
        }
        mobileSeismicWasActive=active;
    }
    private void DrawMobileSeismicResult()
    {
        GUILayout.Label(MobileSeismicPlayback.Instance.Summary,HeaderStyle());
        GUILayout.Label("OpenSees precalculado · gravedad incluida · separado de C1/C2/C3",WrapStyle());
        GUILayout.BeginHorizontal();
        foreach(string c in new[]{"N","Vy","Vz","My","Mz"})if(GUILayout.Button(c))SetActiveResult(c);
        GUILayout.EndHorizontal();
        if(selectedElement!=null && MobileSeismicPlayback.Instance.TrySection(selectedElement.id,.5f,out var s))
        {
            GUILayout.Label(activeResult+" centro = "+ComponentValue(s,activeResult).ToString("0.00")+
                (activeResult.StartsWith("M")?" kN·m":" kN"),WrapStyle());
            GUILayout.Label(MobileSeismicPlayback.Reinforcement(selectedElement),WrapStyle());
        }
        if(GUILayout.Button("Volver a resultados estaticos"))MobileSeismicPlayback.Instance.Close();
    }
}

public sealed partial class StructuralARController
{
    public void CancelForVR()
    {
        // The AR loader is paused, not destroyed: existing anchors remain owned by AR.
        StopAllCoroutines();
        ClearCalibrationVisuals(); CancelGuidedPlacement();
        if(freeAimMarker!=null)freeAimMarker.SetActive(false);
        foreach(PlacementRecord item in placements)if(item.Root!=null)item.Root.SetActive(false);
    }

    public void ResumeAfterVR()
    {
        UpdateAnchorState(); UpdateOtherPlacements();
    }
}

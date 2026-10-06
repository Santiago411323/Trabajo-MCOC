using UnityEngine;

public class DiaphragmSelectable : InfoSelectable
{
    public StructuralDiaphragmViewer viewer;
    public int groupIndex, nodeTag;
    public override string GetInfo() => viewer != null ? viewer.Describe(groupIndex, nodeTag) : "Diafragma no disponible.";
    public override void OnSelected() { if(viewer!=null)viewer.Focus(groupIndex); }
}

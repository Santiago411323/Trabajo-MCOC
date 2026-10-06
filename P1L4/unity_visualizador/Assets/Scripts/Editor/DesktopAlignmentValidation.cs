using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class DesktopAlignmentValidation
{
    public static void ValidateBatch()
    {
        GameObject model=null;
        try
        {
            model=new GameObject("Desktop alignment validation");
            model.AddComponent<StructureViewer>();
            var data=UnityData.Structure;
            var nodes=data.nodes.ToDictionary(n=>n.id);
            for(int level=1;level<=5;level++)
                foreach(int index in new[]{4,5,6})
                {
                    string tag="C"+(level*1000+index);
                    var column=data.elements.Single(e=>e.elementTag==tag);
                    if(Mathf.Abs(nodes[column.nodeI].y)>.001f || Mathf.Abs(nodes[column.nodeJ].y)>.001f)
                        throw new Exception("Unaligned column "+tag);
                    var visual=model.GetComponentsInChildren<ElementSelectable>().Single(e=>e.data!=null && e.data.elementTag==tag);
                    if(Mathf.Abs(visual.transform.position.z)>.001f)throw new Exception("Unity mapping failed for "+tag);
                }
            foreach(var slab in model.GetComponentsInChildren<SlabSelectable>())
                if(slab.metadata==null || slab.metadata.id!=slab.slab.id ||
                   Mathf.Abs(slab.metadata.area-(slab.slab.x1-slab.slab.x0)*(slab.slab.y1-slab.slab.y0))>.01f)
                    throw new Exception("Stale desktop slab catalog "+slab.slab.id);
            if(data.elements.Length!=803 || data.p1l4.elementForces.Length!=5621)
                throw new Exception("Desktop geometry and analysis do not correspond");
            Debug.Log("[E2 alignment] PASS: 15 column segments on Unity Z0; actual rendered positions; desktop slab catalog matches geometry; current OpenSees results loaded.");
            UnityEngine.Object.DestroyImmediate(model);model=null;
            DesktopWalkthroughValidation.ValidateBatch();
        }
        catch(Exception error)
        {
            Debug.LogException(error);
            if(model!=null)UnityEngine.Object.DestroyImmediate(model);
            EditorApplication.Exit(1);
        }
    }
}

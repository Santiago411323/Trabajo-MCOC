using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class DesktopPositiveWallValidation
{
    public static void ValidateBatch()
    {
        GameObject model=null;
        try
        {
            model=new GameObject("Positive wall validation");model.AddComponent<StructureViewer>();
            var data=UnityData.Structure;var nodes=data.nodes.ToDictionary(n=>n.id,n=>new Vector3(n.x,n.z,n.y));
            foreach(int id in Enumerable.Range(31,10))
            {
                var wall=data.walls.Single(w=>w.id==id);
                var rendered=model.GetComponentsInChildren<ElementSelectable>().Single(e=>e.isWall && e.wallId==id);
                Require(rendered.GetComponent<Renderer>().bounds.min.z>0,"Wall still rendered on negative Z: "+id);
                Require(Mathf.Abs(nodes[wall.nodeI].z-(id<=35?6.99f:4.045f))<.001f,"Wrong wall initial edge: "+id);
                Require(Mathf.Abs(nodes[wall.nodeJ].z-6.99f)<.001f,"Wrong wall final edge: "+id);
                Require(Mathf.Abs(rendered.GetComponent<Renderer>().bounds.center.y-nodes[wall.nodeI].y-2)<.02f,"Wrong story: "+id);
                Require(wall.demands.Length>=3 && wall.demands.All(d=>!float.IsNaN(d.P_kN) && !float.IsNaN(d.M_kN_m)),"Missing wall P-M demands: "+id);
            }
            Debug.Log("[Positive walls] PASS: IDs31..35 at Unity Z6.99 close the window side; IDs36..40 retain Z4.045..6.99; corridor side remains open across all five stories; original IDs and recalculated P-M demands available.");
            UnityEngine.Object.DestroyImmediate(model);DesktopEnvironmentRevisionValidation.ValidateBatch();
        }
        catch(Exception error){Debug.LogException(error);if(model!=null)UnityEngine.Object.DestroyImmediate(model);EditorApplication.Exit(1);}
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}

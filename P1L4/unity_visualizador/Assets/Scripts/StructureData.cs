using System;
using UnityEngine;

[Serializable]
public class StructureData
{
    public string units;
    public float q_G;
    public string p1l4_version;
    public NodeData[] nodes;
    public ElementData[] elements;
    public WallData[] walls;
    public SupportData[] supports;
    public DiaphragmData[] diaphragmList;
    public SlabData[] slabs;
    public TributaryFloorData[] tributaryList;
    public PointLoadData[] pointLoads;

    public P1L4Extras p1l4;
}

[Serializable]
public class PointLoadData
{
    public int node;
    public float fx;
    public float fy;
    public float fz;
    public float mx;
    public float my;
    public float mz;
}

[Serializable]
public class DiaphragmData
{
    public string level;
    public float x;
    public float y;
    public float z;
    public int maestro;
    public int[] slaves;
}

[Serializable]
public class P1L4Extras
{
    // Missing in legacy exports: eleForce in GLOBAL structural coordinates.
    // A future exporter may explicitly set this to "local" for localForce.
    public string elementForceCoordinates;
    public ComboInfo[] combinations;
    public DisplacementRecord[] displacements;
    public ElementForceRecord[] elementForces;
    public PMCurveData[] pmCurves;
    public SectionMaterialData[] sectionMaterials;
    public WallRegistryEntry[] wallRegistry;
}

[Serializable]
public class ComboInfo
{
    public string name;
    public string label;
    public float G;
    public float Q;
    public float EX;
    public float EY;
}

[Serializable]
public class DisplacementRecord
{
    public string combo;
    public int node;
    public float ux;
    public float uy;
    public float uz;
    public float rx;
    public float ry;
    public float rz;
}

[Serializable]
public class ElementForceRecord
{
    public string combo;
    public int id;
    public float[] f;
}

[Serializable]
public class PMCurveData
{
    public string sectionId;
    public string elementType;
    public float b_m;
    public float h_m;
    public float fc_MPa;
    public float fy_MPa;
    public int steelBars;
    public float barDiameter_mm;
    public float Ast_mm2;
    public float rho_percent;
    public float Po_kN;
    public string interpretation;
    public PMPoint[] points;
    public DemandRecord[] demands;
}

[Serializable]
public class DemandRecord
{
    public string combo;
    public float P_kN;
    public float M_kN_m;
    public string note;
}

[Serializable]
public class SectionMaterialData
{
    public string sectionId;
    public string elementType;
    public string materialName;
    public float fc_MPa;
    public float fy_MPa;
    public float E_MPa;
    public float b_m;
    public float h_m;
    public int steelBars;
    public float barDiameter_mm;
    public float Ast_mm2;
    public float rho_percent;
    public string note;
}

[Serializable]
public class WallRegistryEntry
{
    public int index;
    public int nodeI;
    public int nodeJ;
    public float grosor;
    public float longitud;
    public string bottom;
    public string top;
    public string pmSectionId;
    public bool hasCurve;
    public DemandRecord[] demands;
}

[Serializable]
public class SupportData
{
    public int node;
    public string type;
    public int ux;
    public int uy;
    public int uz;
    public int rx;
    public int ry;
    public int rz;
}

[Serializable]
public class NodeData
{
    public int id;
    public float x;
    public float y;
    public float z;
}

[Serializable]
public class ElementData
{
    public int id;
    public string type;
    public int nodeI;
    public int nodeJ;
    public string seccion;
    public string sectionId;
    public string elementTag;
    public string sourceBuilding;
    public string sourceId;
    public float width_m;
    public float height_m;
    public float uniformLoad;
    public float deadLoad;
    public float liveLoad;
    public float factoredLoad14D;
    public float factoredLoad12D16L;
    public float axialI;
    public float axialJ;
    public float shearI;
    public float shearJ;
    public float momentI;
    public float momentJ;
    public string piso;
    public float areaTributaria;
    public float cargaTributaria;
}

[Serializable]
public class WallData
{
    public int id;
    public int nodeI;
    public int nodeJ;
    public string type;
    public float grosor;
    public float longitud;
    public string bottom;
    public string top;
    public string sourceBuilding;
    public string sourceId;
    public DemandRecord[] demands;
}

[Serializable]
public class SlabData
{
    public string id;
    public string nivel;
    public float x0;
    public float y0;
    public float x1;
    public float y1;
    public float z;
    public SlabOpening[] openings;

    public bool Contains(float x,float y)
    {
        if(float.IsNaN(x)||float.IsNaN(y)||float.IsInfinity(x)||float.IsInfinity(y)) return false;
        if(x<Math.Min(x0,x1)||x>Math.Max(x0,x1)||y<Math.Min(y0,y1)||y>Math.Max(y0,y1)) return false;
        foreach(var h in openings ?? new SlabOpening[0])
            if(x>=Math.Min(h.x0,h.x1)&&x<=Math.Max(h.x0,h.x1)&&y>=Math.Min(h.y0,h.y1)&&y<=Math.Max(h.y0,h.y1)) return false;
        return true;
    }

    public bool CanMove(float ax,float ay,float bx,float by)
    {
        if(!Contains(ax,ay)||!Contains(bx,by)) return false;
        foreach(var h in openings ?? new SlabOpening[0])
        {
            float lo=0,hi=1; bool intersects=true;
            for(int axis=0;axis<2;axis++)
            {
                float v=axis==0?ax:ay,d=axis==0?bx-ax:by-ay;
                float mn=axis==0?Math.Min(h.x0,h.x1):Math.Min(h.y0,h.y1);
                float mx=axis==0?Math.Max(h.x0,h.x1):Math.Max(h.y0,h.y1);
                if(Math.Abs(d)<1e-8f) {if(v<mn||v>mx) intersects=false;}
                else {float t0=(mn-v)/d,t1=(mx-v)/d;lo=Math.Max(lo,Math.Min(t0,t1));hi=Math.Min(hi,Math.Max(t0,t1));}
            }
            if(intersects&&lo<=hi) return false;
        }
        return true;
    }
}

[Serializable]
public class SlabOpening { public float x0, y0, x1, y1; }

public static class SlabNavigation
{
    public static SlabData FindAdjacent(SlabData current, SlabData[] slabs,
        float targetX, float targetY, float directionX, float directionY, float tolerance = 0.03f)
    {
        if(current==null||slabs==null) return null;
        SlabData best=null;float bestOffset=float.MaxValue;
        foreach(var candidate in slabs)
        {
            if(candidate==null||candidate==current||Math.Abs(candidate.z-current.z)>tolerance||
                !candidate.Contains(targetX,targetY)||!TouchesInDirection(current,candidate,directionX,directionY,tolerance)) continue;
            float offset=Math.Abs(directionX)>=Math.Abs(directionY)
                ? Math.Abs(targetY-(candidate.y0+candidate.y1)*.5f)
                : Math.Abs(targetX-(candidate.x0+candidate.x1)*.5f);
            if(offset<bestOffset) {best=candidate;bestOffset=offset;}
        }
        return best;
    }

    private static bool TouchesInDirection(SlabData a,SlabData b,float dx,float dy,float tolerance)
    {
        float ax0=Math.Min(a.x0,a.x1),ax1=Math.Max(a.x0,a.x1),ay0=Math.Min(a.y0,a.y1),ay1=Math.Max(a.y0,a.y1);
        float bx0=Math.Min(b.x0,b.x1),bx1=Math.Max(b.x0,b.x1),by0=Math.Min(b.y0,b.y1),by1=Math.Max(b.y0,b.y1);
        if(Math.Abs(dx)>=Math.Abs(dy))
        {
            float overlap=Math.Min(ay1,by1)-Math.Max(ay0,by0);
            return overlap>tolerance && (dx>0?Math.Abs(ax1-bx0)<=tolerance:Math.Abs(ax0-bx1)<=tolerance);
        }
        float xOverlap=Math.Min(ax1,bx1)-Math.Max(ax0,bx0);
        return xOverlap>tolerance && (dy>0?Math.Abs(ay1-by0)<=tolerance:Math.Abs(ay0-by1)<=tolerance);
    }
}

[Serializable]
public class SlabLoadCatalog { public SlabLoadMetadata[] slabs; }
[Serializable]
public class SlabLoadMetadata
{
    public string id, profile;
    public float thickness, unitWeight, finishes, qG;
    public SlabLoadEdge[] edges;
}
[Serializable]
public class SlabLoadEdge { public string side,message; public float area; public int[] beams; }

[Serializable]
public class TributaryFloorData
{
    public string piso;
    public float area_total;
    public float carga_total;
    public int vigas;
}

[Serializable]
public class PMPoint
{
    public string label;
    public float P_kN;
    public float M_kN_m;
    public float phi_1_m;
}

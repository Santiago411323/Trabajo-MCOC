using System;
using System.Collections.Generic;
using UnityEngine;

// Same nearest active-edge regions as mobile_slab_worker.transfer; not a shell solution.
public static class XrayTributaryRegion
{
    public static List<List<Vector2>> Build(SlabData slab,string side)
    {
        float x0=Math.Min(slab.x0,slab.x1),x1=Math.Max(slab.x0,slab.x1),y0=Math.Min(slab.y0,slab.y1),y1=Math.Max(slab.y0,slab.y1);
        var distances=new Dictionary<string,Vector3>{{"bottom",new Vector3(0,1,-y0)},{"top",new Vector3(0,-1,y1)},
            {"left",new Vector3(1,0,-x0)},{"right",new Vector3(-1,0,x1)}};
        if(Math.Min(x1-x0,y1-y0)<=0||!distances.ContainsKey(side))return new List<List<Vector2>>();
        string[] active=Math.Max(x1-x0,y1-y0)/Math.Min(x1-x0,y1-y0)>2
            ? (x1-x0<y1-y0?new[]{"left","right"}:new[]{"bottom","top"}) :new[]{"bottom","right","top","left"};
        if(Array.IndexOf(active,side)<0)return new List<List<Vector2>>();
        var polygon=new List<Vector2>{new Vector2(x0,y0),new Vector2(x1,y0),new Vector2(x1,y1),new Vector2(x0,y1)};
        foreach(string other in active)if(other!=side)
        {
            var inequality=distances[side]-distances[other];polygon=Clip(polygon,inequality.x,inequality.y,inequality.z);
        }
        var pieces=new List<List<Vector2>>();if(Area(polygon)>1e-7)pieces.Add(polygon);
        foreach(var hole in slab.openings??Array.Empty<SlabOpening>())
        {
            var split=new List<List<Vector2>>();float hx0=Math.Min(hole.x0,hole.x1),hx1=Math.Max(hole.x0,hole.x1),hy0=Math.Min(hole.y0,hole.y1),hy1=Math.Max(hole.y0,hole.y1);
            foreach(var piece in pieces)
            {
                var left=Clip(piece,1,0,-hx0);var right=Clip(piece,-1,0,hx1);
                var middle=Clip(Clip(piece,-1,0,hx0),1,0,-hx1);
                var bottom=Clip(middle,0,1,-hy0);var top=Clip(middle,0,-1,hy1);
                foreach(var p in new[]{left,right,bottom,top})if(Area(p)>1e-7)split.Add(p);
            }
            pieces=split;
        }
        return pieces;
    }
    public static float Area(List<Vector2> polygon)
    {
        double area=0;for(int i=0;i<polygon.Count;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Count];area+=(double)a.x*b.y-(double)b.x*a.y;}return (float)Math.Abs(area*.5);
    }
    private static List<Vector2> Clip(List<Vector2> polygon,float a,float b,float c)
    {
        var output=new List<Vector2>();if(polygon.Count==0)return output;
        for(int i=0;i<polygon.Count;i++)
        {
            var start=polygon[i];var end=polygon[(i+1)%polygon.Count];float ds=a*start.x+b*start.y+c,de=a*end.x+b*end.y+c;
            bool insideStart=ds<=.000001f,insideEnd=de<=.000001f;
            if(insideStart)output.Add(start);
            if(insideStart!=insideEnd)output.Add(Vector2.Lerp(start,end,Mathf.Clamp01(ds/(ds-de))));
        }
        return output;
    }
}

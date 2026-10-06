using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

public static class XrayNetworkChecks
{
    static int count;
    static void Check(bool condition,string message){count++;if(!condition)throw new Exception("X-Ray: "+message);}
    static void Close(double a,double b,double tolerance,string message){Check(Math.Abs(a-b)<=tolerance,message);}
    public static void Run(string folder,JsonSerializerOptions options)
    {
        var data=JsonSerializer.Deserialize<StructureData>(File.ReadAllText(Path.Combine(folder,"model.json")),options);
        var network=new IncrementalResponseNetwork(data);
        var frameOptions=new JsonSerializerOptions{IncludeFields=true,PropertyNameCaseInsensitive=true};
        var frame=JsonSerializer.Deserialize<IncrementalResponseNetwork.Frame>(File.ReadAllText(Path.Combine(folder,"frame.json")),frameOptions);
        var expected=JsonSerializer.Deserialize<Dictionary<string,double>>(File.ReadAllText(Path.Combine(folder,"expected_my.json")));
        Check(network.InvalidEdges==0,"All analytic I-J edges have true existing vertices");
        Check(network.Members.Count==data.elements.Length,"All native members indexed");
        Check(network.CompleteDiaphragms && network.Constraints.Count==data.p1l4.analysisModel.diafragmas.Sum(d=>d.slaves),"MPC membership matches native metadata");
        var result=network.Evaluate(frame,IncrementalResponseNetwork.Metric.My);
        Check(result.Missing==0,"Real fixture has complete physical-member actions");
        Check(result.Path!=null&&network.Supports.Contains(result.Path.Support),"Connection route terminates at declared restrained support");
        Check(result.Path.Nodes.Count==result.Path.Edges.Count+1,"Route has continuous vertices");
        for(int i=0;i<result.Path.Edges.Count;i++)
        {
            var e=result.Path.Edges[i];int a=result.Path.Nodes[i],b=result.Path.Nodes[i+1];
            Check(e.I==a&&e.J==b||e.I==b&&e.J==a,"Route respects actual endpoints");
            Check(!e.Constraint,"XY/Rz MPC never depicted as a vertical gravity-transfer bar");
        }
        foreach(var pair in result.ById)
        {
            Check(pair.Value.Available,"Every reading supported by a response");
            Close(pair.Value.Value,expected[pair.Key.ToString()],.00001,"DeltaMy max agrees with independent native endpoint calculation");
            Check(pair.Value.Position>=0&&pair.Value.Position<=1,"Position is within actual member");
        }
        for(int i=1;i<result.Ranked.Count;i++)Check(result.Ranked[i-1].Value>=result.Ranked[i].Value,"Ranking sorted by one comparable metric");
        foreach(var r in result.Ranked)Close(r.Intensity,r.Value/result.Maximum,.000001,"Normalization uses true metric maximum");
        Check(result.BalanceAvailable,"Reactions actually exported");Close(result.ReactionZ,frame.P,.00001,"Global reaction sum balances applied load");
        Check(result.Visible.Count<=18+frame.Receivers.Length+result.Path.Edges.Count+3,"AUTO legibility budget plus actual connections");
        foreach(var receiver in frame.Receivers)Check(result.Visible.Contains(receiver.Beam),"Immediate receiver remains visible even at zero chosen component");
        int selected=result.Ranked[0].Edge.Id;Check(network.Explain(result,selected).Contains(result.Ranked[0].Edge.Tag),"Explanation preserves exact elementTag");
        foreach(var e in network.Members.Values)Check(network.Adjacency[e.I].Contains(e)&&network.Adjacency[e.J].Contains(e),"Bidirectional adjacency from IDs");
        var absent=new IncrementalResponseNetwork.Frame {Slab=frame.Slab,Forces=Array.Empty<ElementForceRecord>(),Displacements=Array.Empty<DisplacementRecord>()};
        var missing=network.Evaluate(absent,IncrementalResponseNetwork.Metric.My);
        Check(missing.Ranked.Count==0&&missing.Missing>0,"Missing responses never become measured zero or fake ranking");
        var zero=new IncrementalResponseNetwork.Frame {Slab=frame.Slab,Forces=frame.Forces.Select(f=>new ElementForceRecord{id=f.id,f=new float[12]}).ToArray()};
        Check(network.Evaluate(zero,IncrementalResponseNetwork.Metric.My).Ranked.Count==0,"Zero input has no dominant response");
        var simple=new StructureData {
            nodes=new[]{new NodeData{id=1,x=0},new NodeData{id=2,x=8},new NodeData{id=3,x=0},new NodeData{id=4,x=8}},
            elements=new[]{new ElementData{id=1,nodeI=1,nodeJ=2,type="viga"},new ElementData{id=2,nodeI=3,nodeJ=4,type="viga"}},
            supports=new[]{new SupportData{node=4,uz=1}}
        };
        var detached=new IncrementalResponseNetwork(simple);
        Check(detached.FindRoute(new[]{1},4,new Dictionary<int,IncrementalResponseNetwork.Reading>())==null,"Coincident geometry does not connect distinct IDs");
        float[] uniform=new float[12];uniform[4]=20;uniform[2]=uniform[8]=8;uniform[10]=-20;
        var score=IncrementalResponseNetwork.Score(detached.Members[1],uniform,new Dictionary<int,DisplacementRecord>(),detached.Nodes,IncrementalResponseNetwork.Metric.My);
        Close(score.Value,36,.00001,"Interior maximum of Delta diagram evaluated rather than subtracting maxima");Close(score.Position,.5,.00001,"Interior peak position");
        var square=new SlabData{x0=0,y0=0,x1=4,y1=4};
        float area=0;foreach(string side in new[]{"bottom","right","top","left"})
        {float part=XrayTributaryRegion.Build(square,side).Sum(XrayTributaryRegion.Area);Close(part,4,.00001,"Nearest-edge area for square");area+=part;}
        Close(area,16,.00001,"Regions conserve slab area");
        square.openings=new[]{new SlabOpening{x0=1,y0=1,x1=3,y1=3}};
        area=new[]{"bottom","right","top","left"}.Sum(side=>XrayTributaryRegion.Build(square,side).Sum(XrayTributaryRegion.Area));
        Close(area,12,.00001,"Openings are not painted as loadable tributary surface");
        var oneWay=new SlabData{x0=0,y0=0,x1=4,y1=12};
        Close(XrayTributaryRegion.Build(oneWay,"left").Sum(XrayTributaryRegion.Area),24,.00001,"One-way nearest active edge");
        Check(XrayTributaryRegion.Build(oneWay,"bottom").Count==0,"Inactive edge cannot be invented as receiver");
        Console.WriteLine("PASS: "+count+" X-Ray checks: native ranking, true graph, restrained destinations, missing/zero data and tributary openings.");
    }
}

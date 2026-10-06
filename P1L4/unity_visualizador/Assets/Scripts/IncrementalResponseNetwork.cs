using System;
using System.Collections.Generic;
using System.Linq;

// Camera-independent graph and ranking. The moving-load worker already supplies DeltaR.
public sealed class IncrementalResponseNetwork
{
    public enum Metric { N, Vy, Vz, My, Mz, T, Displacement }
    public sealed class Edge
    {
        public int Id,I,J;public string Tag,Type;public ElementData Member;
        public bool Constraint,Link;
        public bool Physical=>!Constraint&&!Link;
    }
    public sealed class Receiver {public int Beam;public string Side;public float Load,Area,Position;}
    public sealed class Frame
    {
        public int Sequence;public string Slab,SourceHash,BaseLabel;public float X,Y,P,TransferError,Transferred;
        public ElementForceRecord[] Forces;public DisplacementRecord[] Displacements;
        public Receiver[] Receivers;public SupportReactionRecord[] Reactions;public bool ReactionsAvailable;
        public int[] LoadedNodes;
    }
    public sealed class Reading
    {
        public Edge Edge;public bool Available;public float Value,Signed,Position,Intensity;public int Rank;
        public string Category=>Intensity>=.8f?"DOMINANTE":Intensity>=.5f?"ALTA RESPUESTA":Intensity>=.25f?"SIGNIFICATIVO":"SECUNDARIO";
    }
    public sealed class Route
    {
        public int Support;public List<int> Nodes=new List<int>();public List<Edge> Edges=new List<Edge>();
    }
    public sealed class Result
    {
        public Frame Frame;public Metric Metric;public List<Reading> Ranked=new List<Reading>();
        public Dictionary<int,Reading> ById=new Dictionary<int,Reading>();
        public HashSet<int> Visible=new HashSet<int>(),ReceiverIds=new HashSet<int>();
        public Route Path;public int Missing,PrimaryBeam,ReactionSupport;public float Maximum,ReactionZ,ForceResidual;
        public bool BalanceAvailable;
    }
    public readonly Dictionary<int,NodeData> Nodes=new Dictionary<int,NodeData>();
    public readonly Dictionary<int,Edge> Members=new Dictionary<int,Edge>();
    public readonly Dictionary<int,List<Edge>> Adjacency=new Dictionary<int,List<Edge>>();
    public readonly HashSet<int> Supports=new HashSet<int>();
    public readonly List<Edge> Constraints=new List<Edge>();
    public readonly Dictionary<int,int> SupportDepth=new Dictionary<int,int>();
    public readonly Dictionary<string,SlabData> Slabs=new Dictionary<string,SlabData>();
    public int InvalidEdges {get;private set;}
    public bool CompleteDiaphragms {get;private set;}=true;

    public IncrementalResponseNetwork(StructureData data)
    {
        foreach(var n in data.nodes??Array.Empty<NodeData>()){Nodes.Add(n.id,n);Adjacency[n.id]=new List<Edge>();}
        foreach(var s in data.slabs??Array.Empty<SlabData>())Slabs[s.id]=s;
        foreach(var e in data.elements??Array.Empty<ElementData>())
        {
            if(!Nodes.ContainsKey(e.nodeI)||!Nodes.ContainsKey(e.nodeJ)||e.nodeI==e.nodeJ){InvalidEdges++;continue;}
            var edge=new Edge{Id=e.id,I=e.nodeI,J=e.nodeJ,Tag=!string.IsNullOrEmpty(e.elementTag)?e.elementTag:e.id.ToString(),
                Type=e.type,Member=e,Link=e.type=="brazo_rigido"};
            Members.Add(edge.Id,edge);Connect(edge);
        }
        foreach(var s in data.supports??Array.Empty<SupportData>())if(s.uz==1&&Nodes.ContainsKey(s.node))Supports.Add(s.node);
        int constraintId=-1;
        foreach(var d in data.p1l4?.analysisModel?.diafragmas??Array.Empty<RigidDiaphragmRecord>())
        {
            if(d.slaveTags==null||d.slaveTags.Length!=d.slaves){CompleteDiaphragms=false;continue;}
            if(!Nodes.ContainsKey(d.master)){Nodes.Add(d.master,new NodeData{id=d.master,x=d.x,y=d.y,z=d.z});Adjacency[d.master]=new List<Edge>();}
            foreach(int slave in d.slaveTags)
            {
                if(!Nodes.ContainsKey(slave)){CompleteDiaphragms=false;continue;}
                var edge=new Edge{Id=constraintId--,I=d.master,J=slave,Constraint=true,Type="MPC_Ux_Uy_Rz",Tag=$"MPC {d.master}→{slave}"};
                Constraints.Add(edge);Connect(edge);
            }
        }
        if((data.p1l4?.analysisModel?.diafragmas)==null)CompleteDiaphragms=false;
        var queue=new Queue<int>();foreach(int n in Supports){SupportDepth[n]=0;queue.Enqueue(n);}
        while(queue.Count>0)
        {
            int n=queue.Dequeue();foreach(var edge in Adjacency[n])
            {if(edge.Constraint)continue;int next=edge.I==n?edge.J:edge.I;if(SupportDepth.ContainsKey(next))continue;SupportDepth[next]=SupportDepth[n]+1;queue.Enqueue(next);}
        }
    }
    private void Connect(Edge edge){Adjacency[edge.I].Add(edge);Adjacency[edge.J].Add(edge);}
    public static string Units(Metric metric)=>metric==Metric.Displacement?"mm":metric==Metric.My||metric==Metric.Mz||metric==Metric.T?"kN·m":"kN";
    public static int Component(Metric metric)=>metric==Metric.N?0:metric==Metric.Vy?1:metric==Metric.Vz?2:metric==Metric.T?3:metric==Metric.My?4:5;
    public static Reading Score(Edge edge,float[] force,Dictionary<int,DisplacementRecord> displacements,Dictionary<int,NodeData> nodes,Metric metric)
    {
        var r=new Reading{Edge=edge};
        if(!nodes.TryGetValue(edge.I,out var i)||!nodes.TryGetValue(edge.J,out var j)||!FrameGeometry.TryCreate(i,j,out var geometry))return r;
        if(metric==Metric.Displacement)
        {
            if(!displacements.TryGetValue(edge.I,out var ui)||!displacements.TryGetValue(edge.J,out var uj)||!DiaphragmKinematics.Finite(ui)||!DiaphragmKinematics.Finite(uj))return r;
            double a=Math.Sqrt((double)ui.ux*ui.ux+(double)ui.uy*ui.uy+(double)ui.uz*ui.uz)*1000;
            double b=Math.Sqrt((double)uj.ux*uj.ux+(double)uj.uy*uj.uy+(double)uj.uz*uj.uz)*1000;
            r.Available=true;r.Value=(float)Math.Max(a,b);r.Signed=r.Value;r.Position=a>=b?0:1;return r;
        }
        if(!FrameForces.IsValid(force))return r;
        for(int k=0;k<=60;k++)
        {
            float t=k/60f,value=FrameForces.Evaluate(force,geometry.Length,t).Component(Component(metric));
            if(!r.Available||Math.Abs(value)>r.Value){r.Available=true;r.Value=Math.Abs(value);r.Signed=value;r.Position=t;}
        }
        return r;
    }
    public Result Evaluate(Frame frame,Metric metric,int threshold=0)
    {
        var result=new Result{Frame=frame,Metric=metric};
        var forces=new Dictionary<int,float[]>();foreach(var f in frame.Forces??Array.Empty<ElementForceRecord>())forces[f.id]=f.f;
        var displacements=new Dictionary<int,DisplacementRecord>();foreach(var d in frame.Displacements??Array.Empty<DisplacementRecord>())displacements[d.node]=d;
        foreach(var pair in Members)
        {
            var edge=pair.Value;if(!edge.Physical)continue;
            forces.TryGetValue(edge.Id,out var f);var reading=Score(edge,f,displacements,Nodes,metric);result.ById[edge.Id]=reading;
            if(!reading.Available){result.Missing++;continue;}
            result.Maximum=Math.Max(result.Maximum,reading.Value);
            if(reading.Value>1e-8f)result.Ranked.Add(reading);
        }
        result.Ranked.Sort((a,b)=>{int c=b.Value.CompareTo(a.Value);return c!=0?c:a.Edge.Id.CompareTo(b.Edge.Id);});
        for(int i=0;i<result.Ranked.Count;i++){var r=result.Ranked[i];r.Rank=i+1;r.Intensity=result.Maximum>1e-8f?r.Value/result.Maximum:0;}
        int budget=threshold==0?18:threshold==1?Math.Max(1,(int)Math.Ceiling(result.Ranked.Count*.1)):threshold==2?Math.Max(1,(int)Math.Ceiling(result.Ranked.Count*.2)):48;
        for(int i=0;i<Math.Min(48,result.Ranked.Count);i++)
        {
            var r=result.Ranked[i];
            if(threshold<3?i<budget:r.Intensity>=(threshold==3?.25f:.5f))result.Visible.Add(r.Edge.Id);
        }
        float primaryLoad=-1;
        foreach(var r in frame.Receivers??Array.Empty<Receiver>())
        {
            if(!Members.ContainsKey(r.Beam))continue;
            result.ReceiverIds.Add(r.Beam);result.Visible.Add(r.Beam);
            if(r.Load>primaryLoad){primaryLoad=r.Load;result.PrimaryBeam=r.Beam;}
        }
        // Physical vertical paths are presentation connections, not an allocation of force.
        var seeds=new List<int>();foreach(int n in frame.LoadedNodes??Array.Empty<int>())if(Nodes.ContainsKey(n))seeds.Add(n);
        float greatestReaction=-1;
        foreach(var r in frame.Reactions??Array.Empty<SupportReactionRecord>())
        {
            result.ReactionZ+=r.fz;
            if(r.declared&&Supports.Contains(r.node)&&Math.Abs(r.fz)>greatestReaction){greatestReaction=Math.Abs(r.fz);result.ReactionSupport=r.node;}
        }
        result.BalanceAvailable=frame.ReactionsAvailable && (frame.Reactions?.Length??0)>0;
        result.ForceResidual=result.ReactionZ-frame.P;
        result.Path=FindRoute(seeds,result.ReactionSupport,result.ById);
        if(result.Path==null)result.Path=FindRoute(seeds,0,result.ById);
        if(result.Path!=null)foreach(var edge in result.Path.Edges)result.Visible.Add(edge.Id);
        // AUTO reserves a few vertical members even when beams dominate the chosen component.
        if(threshold==0)
        {
            int count=0;foreach(var r in result.Ranked)if(r.Edge.Type=="columna"||r.Edge.Type=="muro_eq")
            {result.Visible.Add(r.Edge.Id);if(++count==3)break;}
        }
        return result;
    }
    private sealed class Visit : IComparable<Visit>
    {
        public float Cost;public int Node;
        public int CompareTo(Visit other){int c=Cost.CompareTo(other.Cost);return c!=0?c:Node.CompareTo(other.Node);}
    }
    public Route FindRoute(IEnumerable<int> starts,int target,Dictionary<int,Reading> scores)
    {
        var queue=new SortedSet<Visit>();var cost=new Dictionary<int,float>();var parent=new Dictionary<int,Edge>();
        foreach(int start in starts)if(Nodes.ContainsKey(start)&&!cost.ContainsKey(start)){cost[start]=0;queue.Add(new Visit{Node=start});}
        int goal=0;
        while(queue.Count>0)
        {
            var visit=queue.Min;queue.Remove(visit);int node=visit.Node;
            if(visit.Cost>cost[node])continue;
            if(target!=0?node==target:Supports.Contains(node)){goal=node;break;}
            foreach(var edge in Adjacency[node])
            {
                // XY diaphragm MPC does not directly transmit the selected vertical point load.
                // Keep it in the graph/explanation but do not draw it as a gravity-transfer bar.
                if(edge.Constraint)continue;
                int next=edge.I==node?edge.J:edge.I;float intensity=scores.TryGetValue(edge.Id,out var r)?r.Intensity:0;
                float weight=edge.Link ? .8f : 1f+2f*(1-intensity);
                if(Nodes[next].z>Nodes[node].z+.001f)weight+=5f;
                float candidate=visit.Cost+weight;
                if(cost.TryGetValue(next,out var previous)&&candidate>=previous)continue;
                cost[next]=candidate;parent[next]=edge;queue.Add(new Visit{Node=next,Cost=candidate});
            }
        }
        if(goal==0)return null;
        var route=new Route{Support=goal};route.Nodes.Add(goal);int current=goal;
        while(parent.TryGetValue(current,out var e)){route.Edges.Add(e);current=e.I==current?e.J:e.I;route.Nodes.Add(current);}
        route.Edges.Reverse();route.Nodes.Reverse();return route;
    }
    public string Explain(Result result,int id)
    {
        if(!Members.TryGetValue(id,out var edge))return "No existe esa arista en el modelo analítico.";
        var lines=new List<string>{$"elementTag: {edge.Tag} · ID analítico {edge.Id}",$"Tipo: {edge.Type} · nodos {edge.I} → {edge.J}",
            $"Origen: losa {result.Frame.Slab}, ({result.Frame.X:0.###}, {result.Frame.Y:0.###}) m · P={result.Frame.P:0.###} kN",
            $"Respuesta incremental OpenSees · muestra seq {result.Frame.Sequence} · base: {result.Frame.BaseLabel}"};
        if(result.ById.TryGetValue(id,out var reading)&&reading.Available)
            lines.Add($"Δ{result.Metric}: {reading.Signed:0.######} {Units(result.Metric)} · máximo |Δ|={reading.Value:0.######} · x/L={reading.Position*100:0.#}%\nRanking: {(reading.Rank>0?reading.Rank.ToString():"sin incremento significativo")} · intensidad relativa {reading.Intensity:0.###} · {reading.Category}");
        else lines.Add("Componente no disponible; no se sustituye por cero.");
        foreach(var receiver in result.Frame.Receivers??Array.Empty<Receiver>())if(receiver.Beam==id)
            lines.Add($"RECEPTOR REAL del reparto de la persona: {receiver.Load:0.###} kN · borde {receiver.Side} · proyección {receiver.Position*100:0.#}%. No representa un porcentaje de flujo por el resto del edificio.");
        var connected=new HashSet<string>();var constraints=new HashSet<string>();
        foreach(int node in new[]{edge.I,edge.J})foreach(var adjacent in Adjacency[node])
        {
            if(adjacent.Id==id)continue;
            if(adjacent.Constraint)constraints.Add(adjacent.Tag);else connected.Add(adjacent.Tag+" ["+adjacent.Type+"]");
        }
        lines.Add("Conectados por I/J (sin atribuir causalidad): "+string.Join(", ",connected.Take(10)));
        if(constraints.Count>0)lines.Add("Restricciones XY/Rz en sus nodos: "+string.Join(", ",constraints.Take(4))+". No son barras de transferencia vertical.");
        if(result.Path!=null&&result.Path.Edges.Exists(e=>e.Id==id))lines.Add("Incluido también para mostrar una conexión real hacia el apoyo N"+result.Path.Support+"; no es una trayectoria exacta de fuerzas.");
        lines.Add("Pulso = secuencia explicativa. Intensidad = incremento relativo de la métrica, no daño ni D/C.");
        return string.Join("\n",lines);
    }
}

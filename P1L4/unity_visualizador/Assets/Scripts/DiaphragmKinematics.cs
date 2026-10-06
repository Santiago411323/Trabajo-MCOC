using System;
using System.Collections.Generic;
using UnityEngine;

// OpenSees global axes: Z vertical. Unity displays (X,Z,Y), including translations.
public static class DiaphragmKinematics
{
    public sealed class Motion
    {
        public bool available;
        public int samples;
        public double ux, uy, rz, residual, rotationResidual;
        public Dictionary<int, Vector3> displacements = new Dictionary<int, Vector3>();
    }

    public static string Validate(RigidDiaphragmRecord group, Dictionary<int, NodeData> nodes)
    {
        if (group == null || group.normalAxis != 3 || group.slaveTags == null || group.slaveTags.Length < 2)
            return "Falta exportar la membresía real de rigidDiaphragm(3).";
        if (group.slaves != group.slaveTags.Length) return "Cantidad de nodos inconsistente.";
        var seen = new HashSet<int>();
        foreach (int id in group.slaveTags)
        {
            if (id == group.master || !seen.Add(id)) return "Nodo repetido o maestro incluido en vinculados.";
            if (!nodes.TryGetValue(id, out var node)) return "Nodo vinculado ausente: " + id;
            if (Math.Abs(node.z - group.z) > .0001) return "Nodo fuera del plano: " + id;
        }
        if (!Equal(group.constrainedDofs, new[] {1,2,6}) || !Equal(group.masterFixity, new[] {0,0,1,1,1,0}))
            return "Restricciones distintas al contrato del visualizador.";
        return null;
    }

    private static bool Equal(int[] a, int[] b)
    {
        if(a==null || a.Length!=b.Length)return false;
        for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;
        return true;
    }

    public static Motion Evaluate(RigidDiaphragmRecord group, Dictionary<int, NodeData> nodes,
        Dictionary<int, DisplacementRecord> records)
    {
        var result = new Motion();
        if(Validate(group,nodes)!=null)return result;
        foreach (int id in group.slaveTags)
        {
            if(!records.TryGetValue(id,out var d) || !Finite(d))continue;
            var n=nodes[id];
            result.ux += d.ux + d.rz*(n.y-group.y);
            result.uy += d.uy - d.rz*(n.x-group.x);
            result.rz += d.rz;
            result.displacements[id]=new Vector3(d.ux,d.uz,d.uy);
            result.samples++;
        }
        if(result.samples==0)return result;
        result.ux/=result.samples; result.uy/=result.samples; result.rz/=result.samples;
        foreach(var pair in result.displacements)
        {
            var n=nodes[pair.Key]; var d=records[pair.Key];
            double px=result.ux-result.rz*(n.y-group.y), py=result.uy+result.rz*(n.x-group.x);
            result.residual=Math.Max(result.residual,Math.Max(Math.Abs(d.ux-px),Math.Abs(d.uy-py)));
            result.rotationResidual=Math.Max(result.rotationResidual,Math.Abs(d.rz-result.rz));
        }
        result.available=result.samples==group.slaveTags.Length;
        return result;
    }

    public static bool Finite(DisplacementRecord d) =>
        !float.IsNaN(d.ux+d.uy+d.uz+d.rx+d.ry+d.rz) && !float.IsInfinity(d.ux+d.uy+d.uz+d.rx+d.ry+d.rz);
}

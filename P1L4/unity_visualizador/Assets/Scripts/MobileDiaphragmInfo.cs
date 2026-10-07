using System.Linq;
using System.Text;

// Read-only inspection of exported rigidDiaphragm membership and static displacements.
public static class MobileDiaphragmInfo
{
    public static string Describe(ElementData element,string combo)
    {
        if(element==null)return "Selecciona una viga o columna para consultar sus diafragmas.";
        var data=UnityData.Structure;var groups=data?.p1l4?.analysisModel?.diafragmas;
        if(groups==null)return "Este modelo no contiene membresía real de diafragmas.";
        var nodes=data.nodes.ToDictionary(n=>n.id);
        var text=new StringBuilder(element.elementTag+" · "+combo+"\n");int found=0;
        foreach(var group in groups)
        {
            bool i=group.master==element.nodeI || group.slaveTags?.Contains(element.nodeI)==true;
            bool j=group.master==element.nodeJ || group.slaveTags?.Contains(element.nodeJ)==true;
            if(!i&&!j)continue;found++;
            var issue=DiaphragmKinematics.Validate(group,nodes);
            text.AppendLine($"Nodo {(i&&j?"I/J":i?"I":"J")} · Unity Y={group.z:0.##} m");
            text.AppendLine($"Maestro {group.master} · {group.slaves} vinculados");
            if(issue!=null){text.AppendLine(issue);continue;}
            if(UnityData.DisplacementsByCombo==null || !UnityData.DisplacementsByCombo.TryGetValue(combo,out var rows))
            {text.AppendLine("Desplazamientos del caso no disponibles.");continue;}
            var motion=DiaphragmKinematics.Evaluate(group,nodes,rows.ToDictionary(r=>r.node));
            if(!motion.available){text.AppendLine("Desplazamientos incompletos.");continue;}
            text.AppendLine($"Ux={motion.ux*1000:0.###} · Uy={motion.uy*1000:0.###} mm");
            text.AppendLine($"Rz={motion.rz:0.#####} rad · error={motion.residual*1000:0.#####} mm");
        }
        if(found==0)text.AppendLine("Los nodos de este elemento no pertenecen a un grupo exportado.");
        text.Append("OpenSees: plano XY, Z vertical.\nUnity: Y vertical; X/Z horizontales.\nDatos reales sin amplificar; consulta sin reanálisis.");
        return text.ToString();
    }
}

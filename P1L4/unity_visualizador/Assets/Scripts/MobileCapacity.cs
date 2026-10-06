using System;
using UnityEngine;

public static class MobileCapacity
{
    public static SeismicDamageProfile Profile(SeismicMember member)
    {
        var profile=new SeismicDamageProfile { Width=member.width_m,Height=member.height_m,
            Check=SeismicPMCheck.None,Source="Sin capacidad última exportada",
            Limitation="No se adopta As mínima como armadura real. Corte, torsión, fatiga y daño constitutivo no evaluados." };
        PMCurveData curve=UnityData.GetPMCurve(member.sectionId);
        if(curve == null && member.type == "columna" && member.sectionId == "COL70/70")
            curve=UnityData.GetPMCurve("COL70/70_FIBER");
        if(curve == null && member.type == "muro_eq" && member.sectionId.StartsWith("MURO_",StringComparison.Ordinal))
            curve=UnityData.GetPMCurve("W_"+member.sectionId.Substring(5));
        if(curve == null && member.type == "muro_eq" && UnityData.Structure.p1l4.pmCurves != null)
            foreach(PMCurveData candidate in UnityData.Structure.p1l4.pmCurves)
                if(candidate.elementType == "muro" && Mathf.Abs(candidate.b_m-Mathf.Min(member.width_m,member.height_m))<.001f &&
                    Mathf.Abs(candidate.h_m-Mathf.Max(member.width_m,member.height_m))<.001f)
                { if(curve != null) { curve=null; break; } curve=candidate; }
        SectionMaterialData material=UnityData.GetMaterial(curve != null ? curve.sectionId : member.sectionId);
        if(material == null) material=UnityData.GetMaterial(member.sectionId);
        if(material != null) profile.FcMPa=material.fc_MPa;
        bool dimensionsMatch=curve != null && (member.type == "muro_eq" ?
            Mathf.Abs(curve.b_m-Mathf.Min(member.width_m,member.height_m))<.001f &&
                Mathf.Abs(curve.h_m-Mathf.Max(member.width_m,member.height_m))<.001f :
            Mathf.Abs(curve.b_m-member.width_m)<.001f && Mathf.Abs(curve.h_m-member.height_m)<.001f);
        if(!dimensionsMatch || material == null || material.Ast_mm2<=0 || curve.points == null ||
            Mathf.Abs(curve.fc_MPa-material.fc_MPa)>.01f) return profile;
        if(member.type != "muro_eq" && (material.topBars<=0 || material.topBars!=material.bottomBars))
        {
            profile.Source="Faltan curvas P-M por signo para esta armadura asimétrica";
            return profile;
        }
        profile.Curve=Array.ConvertAll(curve.points,p=>new SeismicCapacityPoint(p.P_kN,Math.Abs(p.M_kN_m)));
        profile.Check=member.type == "muro_eq" ? (member.height_m>=member.width_m ? SeismicPMCheck.My : SeismicPMCheck.Mz) :
            // Equality of axes requires rotational, not just mirror, symmetry.
            // 5 upper + 5 lower + 4 interior/side is NOT invariant under 90 degrees.
            Mathf.Abs(member.width_m-member.height_m)<.001f && material.topBars>=2 &&
                material.topBars==material.bottomBars && material.sideBarsEach==material.topBars-2
                ? SeismicPMCheck.IndependentAxes : SeismicPMCheck.My;
        profile.Source="P-M nominal: "+curve.sectionId+" ("+curve.points.Length+" puntos)";
        profile.Limitation=profile.Check == SeismicPMCheck.IndependentAxes ?
            "Chequeos P–My y P–Mz separados; interacción biaxial, corte y daño constitutivo no evaluados." :
            "Solo P–"+(profile.Check==SeismicPMCheck.My ? "My" : "Mz")+"; no evalúa el otro eje, corte ni daño constitutivo.";
        if((curve.interpretation ?? "").IndexOf("supuesta",StringComparison.OrdinalIgnoreCase)>=0)
            profile.Limitation+=" Armadura de muro SUPUESTA en la exportación.";
        return profile;
    }

}

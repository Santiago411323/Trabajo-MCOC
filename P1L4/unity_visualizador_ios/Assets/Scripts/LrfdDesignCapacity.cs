using System;
using System.Collections.Generic;

// Diseño preliminar P-My uniaxial: sección Whitney y acero elastoplástico.
// NO sustituye verificación de corte, torsión, esbeltez, biaxialidad o capítulo sísmico.
public sealed class LrfdDesignCapacity
{
    public PMCurveData Positive,Negative;
    public bool Available;
    public LrfdDesignCapacity(SectionMaterialData material,bool column)
    {
        var check=ReinforcementAssessment.Evaluate(material,column,0);
        if(!check.Valid)return;
        Positive=Build(material,column,false);Negative=Build(material,column,true);
        Available=Positive.points.Length>3 && Negative.points.Length>3;
    }
    private static PMCurveData Build(SectionMaterialData m,bool column,bool reverse)
    {
        double ag=m.b_m*m.h_m*1e6,area=m.steelBars*Math.PI*m.barDiameter_mm*m.barDiameter_mm/4;
        double po=(.85*m.fc_MPa*(ag-area)+m.fy_MPa*area)/1000,pt=m.fy_MPa*area/1000;
        double cap=column?.8*.65*po:double.PositiveInfinity;
        var points=new List<PMPoint>();
        var raw=new List<PMPoint>{new PMPoint{P_kN=(float)(.65*po),M_kN_m=0}};
        for(int i=1;i<160;i++)
        {
            double p=po-(po+pt)*i/160;
            var r=ReinforcementAssessment.Evaluate(m,column,p,reverse);
            if(r.UltimateAvailable)raw.Add(new PMPoint{P_kN=(float)(p*r.Phi),M_kN_m=(float)(r.Mn*r.Phi)});
        }
        raw.Add(new PMPoint{P_kN=(float)(-.9*pt),M_kN_m=0});
        for(int i=0;i<raw.Count;i++)
        {
            if(i>0 && (raw[i-1].P_kN>cap)!=(raw[i].P_kN>cap))
            {
                double t=(cap-raw[i-1].P_kN)/(raw[i].P_kN-raw[i-1].P_kN);
                points.Add(new PMPoint{P_kN=(float)cap,M_kN_m=(float)(raw[i-1].M_kN_m+t*(raw[i].M_kN_m-raw[i-1].M_kN_m))});
            }
            if(raw[i].P_kN<=cap)points.Add(raw[i]);
        }
        return new PMCurveData{sectionId="LRFD_"+m.sectionId,points=points.ToArray()};
    }
    public float Ratio(float pCompression,float moment)
    {return Available?UnityData.CapacityRatio(moment>=0?Positive:Negative,pCompression,Math.Abs(moment)):float.NaN;}
    public float MomentAvailable(float pCompression,float moment)
    {
        if(!Available)return 0;
        float probe=UnityData.CapacityRatio(moment>=0?Positive:Negative,pCompression,1);
        return probe>0 && probe<UnityData.OutOfCurveRatio?1/probe:0;
    }
}

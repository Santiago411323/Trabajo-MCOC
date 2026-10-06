using System;
using System.IO;
using System.Text.Json;

public static class ReinforcementChecks
{
    sealed class ExportedString : System.Text.Json.Serialization.JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader,System.Type type,JsonSerializerOptions options)
        {return reader.TokenType==JsonTokenType.Number?reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture):reader.GetString();}
        public override void Write(Utf8JsonWriter writer,string value,JsonSerializerOptions options){writer.WriteStringValue(value);}
    }
    static int checks;
    static void Check(bool condition,string message) { checks++; if(!condition) throw new Exception(message); }
    static SectionMaterialData Material(int top,int bottom,int side,float diameter=25,float width=.6f,float height=.8f)
    {
        return new SectionMaterialData { b_m=width,h_m=height,fc_MPa=25,fy_MPa=420,
            Es_MPa=200000,cover_mm=50,topBars=top,bottomBars=bottom,sideBarsEach=side,
            steelBars=top+bottom+2*side,barDiameter_mm=diameter };
    }
    static void Close(double a,double b,double tolerance,string message) { Check(Math.Abs(a-b)<tolerance,message); }
    public static void Main(string[] args)
    {
        if(args.Length>0)
        {
            string xrayFolder=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(args[0]),"..","xray_validation"));
            if(File.Exists(Path.Combine(xrayFolder,"frame.json")))
            {
                var opts=new JsonSerializerOptions{IncludeFields=true};opts.Converters.Add(new ExportedString());XrayNetworkChecks.Run(xrayFolder,opts);
            }
            else Console.WriteLine("SKIP: generate actual mobile-response fixture with tests/test_xray_network.py.");
        }
        if(args.Length>0)
        {
            string comparisonFolder=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(args[0]),"..","design_comparison_validation"));
            if(File.Exists(Path.Combine(comparisonFolder,"expected.json")))
            {
                var options=new JsonSerializerOptions{IncludeFields=true};options.Converters.Add(new ExportedString());
                DesignComparisonChecks.Run(comparisonFolder,options);
            }
            else Console.WriteLine("SKIP: generate isolated OpenSees fixture with tests/test_design_comparison.py for before/after checks.");
        }
        var searchItems=new System.Collections.Generic.List<StructuralSearchIndex.Item> {
            new StructuralSearchIndex.Item {Kind="viga",Id="72",Tag="E1_72",SourceId="B3072"},
            new StructuralSearchIndex.Item {Kind="viga",Id="272",Tag="E1_272"},
            new StructuralSearchIndex.Item {Kind="muro",Id="72",Tag="Muro_72"},
            new StructuralSearchIndex.Item {Kind="columna",Id="229",Tag="E1_229"},
            new StructuralSearchIndex.Item {Kind="losa",Id="L72",Tag="L72"}
        };
        Check(StructuralSearchIndex.Find(searchItems,"  e1_72  ",null).Count==1,"Trim/case-insensitive elementTag search");
        var numericMatches=StructuralSearchIndex.Find(searchItems,"72",null);
        Check(numericMatches.Count==2 && numericMatches.TrueForAll(m=>m.Id=="72"),"Exact IDs preferred; beam/wall ambiguity retained");
        Check(StructuralSearchIndex.Find(searchItems,"72","viga").Count==1,"Type resolves ambiguous numeric ID");
        Check(StructuralSearchIndex.Find(searchItems,"b3072",null).Count==1,"Original source identifier search");
        Check(StructuralSearchIndex.Find(searchItems,"L72","losa").Count==1,"Slab string identifier search");
        Check(StructuralSearchIndex.Find(searchItems,"E1_",null).Count==3,"Partial tags retain every candidate");
        Check(StructuralSearchIndex.Find(searchItems,"E1_229","viga").Count==0,"Type mismatch never selects another object");
        Check(StructuralSearchIndex.Find(searchItems,"",null).Count==0,"Empty input does not select first object");
        Check(StructuralSearchIndex.Find(searchItems,"999999",null).Count==0,"Missing ID does not return fake object");
        Close(LrfdScenario.SnowPressure(.1f,300),.2941995,.000001,"Snow depth/density to kN/m²");
        Close(LrfdScenario.WaterPressure(.1f),.980665,.000001,"Retained water depth to kN/m²");
        Close(LrfdScenario.WindPressure(10,1),.0613,.000001,"Reference velocity pressure");
        Close(LrfdScenario.RetainedDepth(50,60,120),.1,.000001,"Weather time acceleration preserves rainfall units");
        for(int combination=0;combination<7;combination++)for(int alternative=0;alternative<3;alternative++)
        {
            var factors=LrfdScenario.Factors(combination,alternative,false);
            Check(factors.Length==7,"All seven actions have factors");
            if(combination==1 || combination==2 || combination==3)
            {
                for(int a=0;a<3;a++)Check(a==alternative?factors[2+a]>0:factors[2+a]==0,"Lr/S/R alternatives never added together");
            }
        }
        var dead=LrfdScenario.Factors(0,0,false);
        Check(!LrfdScenario.HasUnavailableResponse(dead,1,1,1,1,1),"D-only combination does not include unrelated weather actions");
        var gravity=LrfdScenario.Factors(1,1,false);
        Check(LrfdScenario.HasUnavailableResponse(gravity,0,.2f,0,0,0),"Unknown nonzero snow response cannot become zero");
        Check(!LrfdScenario.HasUnavailableResponse(gravity,0,0,0,0,0),"D/L response possible with zero alternative");
        Close(LrfdScenario.Factors(2,0,true)[5],.8,.000001,"Companion wind factor from supplied notes");
        Close(LrfdScenario.Factors(4,0,false)[6],1.4,.000001,"Seismic factor from supplied notes, not Richter");
        float[] simple=new float[12];simple[0]=100;simple[6]=-100;simple[2]=40;simple[8]=40;
        var moment=DemandRadarRanking.Evaluate(simple,8,null,DemandRadarRanking.Metric.Moment);
        Check(moment.Available,"Moment ranking doesn't require capacity");
        Close(moment.Value,80,.001,"Radar detects internal maximum, not just end moments");
        Close(moment.Position,.5,.001,"Maximum position");
        Close(DemandRadarRanking.Evaluate(simple,8,null,DemandRadarRanking.Metric.Shear).Value,40,.001,"Resultant shear");
        Close(DemandRadarRanking.Evaluate(simple,8,null,DemandRadarRanking.Metric.Compression).Value,100,.001,"Compression sign");
        Close(DemandRadarRanking.Evaluate(simple,8,null,DemandRadarRanking.Metric.Tension).Value,0,.001,"Compression excluded from tensile ranking");
        Check(!DemandRadarRanking.Evaluate(simple,8,null,DemandRadarRanking.Metric.Capacity).Available,"Missing capacity never treated as safe");
        var pm=new PMCurveData{points=new[]{new PMPoint{P_kN=200,M_kN_m=0},new PMPoint{P_kN=0,M_kN_m=100},new PMPoint{P_kN=-200,M_kN_m=0}}};
        var dcr=DemandRadarRanking.Evaluate(simple,8,pm,DemandRadarRanking.Metric.Capacity);
        Close(dcr.Value,1.6,.001,"DCR uses concurrent P and M at critical station");
        simple[0]=1000;simple[6]=-1000;
        Check(DemandRadarRanking.Evaluate(simple,8,pm,DemandRadarRanking.Metric.Capacity).OutsideCurve,"Axial demand outside envelope is explicit");
        Check(!DemandRadarRanking.Evaluate(null,8,pm,DemandRadarRanking.Metric.Capacity).Available,"Missing forces excluded");
        simple[0]=float.NaN;
        Check(!DemandRadarRanking.Evaluate(simple,8,pm,DemandRadarRanking.Metric.Capacity).Available,"Invalid forces excluded");
        var ui=new UnityEngine.Vector3(.001f,.004f,.002f);
        var uj=new UnityEngine.Vector3(.003f,.009f,.001f);
        var ri=new UnityEngine.Vector3(0,.001f,0);var rj=-ri;
        var dir=UnityEngine.Vector3.right;
        Close((MemberPreviewKinematics.Interpolate(ui,uj,ri,rj,dir,8,0,true)-ui).magnitude,0,1e-8,"Hermite preserves node I");
        Close((MemberPreviewKinematics.Interpolate(ui,uj,ri,rj,dir,8,1,true)-uj).magnitude,0,1e-8,"Hermite preserves node J");
        var midpoint=MemberPreviewKinematics.Interpolate(ui,uj,ri,rj,dir,8,.5f,true);
        Close(midpoint.x,.002,1e-8,"Axial displacement remains linear");
        Close(midpoint.y,.0045,1e-8,"Structural rotation around Y produces correct Unity vertical deflection");
        Close((MemberPreviewKinematics.Interpolate(ui,uj,ri,rj,dir,8,.5f,false)-(ui+uj)/2).magnitude,0,1e-8,"No rotations: honest nodal interpolation");
        var beam=Material(4,4,2);
        var designBeam=new LrfdDesignCapacity(beam,false);
        Check(designBeam.Available,"Beam design envelope available");
        var nominal=ReinforcementAssessment.Evaluate(beam,false,0);
        Close(designBeam.MomentAvailable(0,1),.9*nominal.Mn,2,"Tension-controlled beam reduction in design envelope");
        Check(designBeam.Ratio(0,(float)(nominal.Mn*1.1))>1,"Nominal moment is not automatically design capacity");
        var r=ReinforcementAssessment.Evaluate(beam,false,0);
        Check(r.Valid && r.UltimateAvailable,"Beam equilibrium");
        Close(r.As,5890.48622548,.0001,"12 Ø25 area");
        Close(r.AsTensionRow,1963.495408,.001,"Only bottom 4 bars in minimum comparator");
        Close(r.AsMin,1500,.001,"ACI minimum 0.6 x d0.75, fc25 fy420");
        Close(r.Cmax,281.25,.001,"cmax = 0.375 d");
        Close(r.Amax,239.0625,.001,"amax = beta1 cmax");
        Close(r.AsMax,7257.254464,.002,"Asmax = 0.85 fc b amax / fy");
        Close(r.RhoMaxReference,r.AsMax/(r.B*r.D),.00000001,"Maximum reference ratio");
        Close(r.EpsY,.0021,.00000001,"Yield strain uses real fy/Es");
        Close(r.RhoBalancedReference,.85*.85*25/420*.003/.0051,.00000001,"Beta1 in balanced ratio");
        Check(r.MinimumOK && r.MaximumOK && r.EpsT>.005,"12 Ø25 60/80 ductile P0");
        Close(r.Residual,0,.001,"Axial residual");
        var reverse=ReinforcementAssessment.Evaluate(beam,false,0,true);
        Close(r.C,reverse.C,.00001,"Symmetric faces");
        Close(r.Mn,reverse.Mn,.00001,"Symmetric moments");
        var asym=Material(2,8,0);
        var positive=ReinforcementAssessment.Evaluate(asym,false,0);
        var negative=ReinforcementAssessment.Evaluate(asym,false,0,true);
        Check(positive.AsTensionRow>negative.AsTensionRow && positive.Mn>negative.Mn,"Asymmetric face capacity changes");
        Check(positive.MinimumOK && !negative.MinimumOK,"Minimum evaluated on each face");
        var tiny=ReinforcementAssessment.Evaluate(Material(2,2,0,10),false,0);
        Check(!tiny.MinimumOK,"Too little reinforcement");
        var overloaded=ReinforcementAssessment.Evaluate(Material(0,4,0,32,.3f,.45f),false,0);
        Check(overloaded.UltimateAvailable && !overloaded.MaximumOK,"Over-reinforced section fails strain limit");
        var column=Material(5,5,4,25,.7f,.7f);
        var designColumn=new LrfdDesignCapacity(column,true);
        Check(designColumn.Available,"Column design envelope available");
        double asColumn=18*Math.PI*25*25/4;
        double poColumn=(.85*25*(700*700-asColumn)+420*asColumn)/1000;
        double peak=0;foreach(var p in designColumn.Positive.points)peak=Math.Max(peak,p.P_kN);
        Close(peak,.8*.65*poColumn,.02,"Tied column axial design cap");
        Check(designColumn.Ratio((float)(peak+1),0)>=UnityData.OutOfCurveRatio,"Column axial cap cannot appear safe");
        var flex=ReinforcementAssessment.Evaluate(column,true,0);
        var axial=ReinforcementAssessment.Evaluate(column,true,7000);
        Check(flex.MinimumOK && flex.MaximumOK,"18 Ø25 column ratio limits");
        Check(axial.UltimateAvailable && axial.C>flex.C && axial.EpsT<flex.EpsT,"Axial load affects failure independently of rho");
        Check(ReinforcementAssessment.Evaluate(Material(2,2,0,10,.7f,.7f),true,0).MinimumOK==false,"Column below 1%");
        Check(ReinforcementAssessment.Evaluate(Material(25,25,0,40,.7f,.7f),true,0).MaximumOK==false,"Column above 8%");
        Check(!ReinforcementAssessment.Evaluate(column,true,100000).UltimateAvailable,"Impossible P cannot show failure classification");
        column.steelBars=99;
        Check(!ReinforcementAssessment.Evaluate(column,true,0).Valid,"Inconsistent layout rejected");
        Check(!ReinforcementAssessment.Evaluate(null,true,0).Valid,"Missing data rejected");
        Check(!ReinforcementAssessment.Evaluate(beam,true,double.NaN).Valid,"Missing demand rejected");
        var rotated=ReinforcementAssessment.Evaluate(beam,false,0,false,true);
        Close(rotated.B,800,.001,"Mz swaps width"); Close(rotated.H,600,.001,"Mz swaps depth");
        Close(rotated.AsTensionRow,r.AsTensionRow,.001,"Mz: 2 corners + 2 side bars, without duplication");
        Close(rotated.D,550,.001,"Mz uses correct effective depth");
        Check(Math.Abs(rotated.Mn-r.Mn)>1,"Rectangular section has different capacity about Mz");
        foreach(string path in args)
        {
            var modelOptions=new JsonSerializerOptions{IncludeFields=true};modelOptions.Converters.Add(new ExportedString());
            var model=JsonSerializer.Deserialize<StructureData>(File.ReadAllText(path),modelOptions);
            UnityData.LoadData(model);
            UnityData.ActiveCombo="SUPER";UnityData.SelectedPresetCombo="C1";UnityData.UseBaseCaseFactors=true;
            UnityData.FactorG=1.2f;UnityData.FactorQ=.7f;UnityData.FactorEX=.9f;UnityData.FactorEY=.1f;
            foreach(var element in model.elements)
            {
                if(element.type!="viga" && element.type!="columna")continue;
                if(!UnityData.TryGetFrameGeometry(element.id,out var frame))continue;
                var adjusted=UnityData.GetElementForcesForComparison("C1",element.id);
                var activeForces=UnityData.GetElementForces("SUPER",element.id);
                if(adjusted!=null && activeForces!=null)
                    for(int i=0;i<12;i++)Close(adjusted[i],activeForces[i],.00001,"Adjusted preset used only for C1");
                foreach(string combo in new[]{"C2","C3"})
                {
                    var comparison=UnityData.GetElementForcesForComparison(combo,element.id);
                    var raw=UnityData.GetElementForcesForCase(combo,element.id);
                    if(comparison==null || raw==null)continue;
                    for(int i=0;i<12;i++)Close(comparison[i],raw[i],.00001,"Independent named cases for radar envelope");
                    var reading=DemandRadarRanking.Evaluate(comparison,frame.Length,null,DemandRadarRanking.Metric.Moment);
                    Check(reading.Available && !float.IsNaN(reading.Value),"Real frame ranking available");
                }
            }
            using(var document=JsonDocument.Parse(File.ReadAllText(path)))
            foreach(var item in document.RootElement.GetProperty("p1l4").GetProperty("sectionMaterials").EnumerateArray())
            {
                var m=JsonSerializer.Deserialize<SectionMaterialData>(item.GetRawText(),new JsonSerializerOptions { IncludeFields=true });
                if(m.elementType!="viga" && m.elementType!="columna") continue;
                if(m.steelBars<=0) continue;
                PMCurveData exported=null;
                foreach(var candidate in document.RootElement.GetProperty("p1l4").GetProperty("pmCurves").EnumerateArray())
                    if(candidate.GetProperty("sectionId").GetString()==m.sectionId)
                        exported=JsonSerializer.Deserialize<PMCurveData>(candidate.GetRawText(),new JsonSerializerOptions{IncludeFields=true});
                if(exported!=null && exported.momentCurvature!=null && exported.momentCurvature.Length>0)
                {
                    var origin=MemberMaterialPlayback.Sample(m,exported,0);
                    Check(origin.Available && origin.Phi==0 && origin.Moment==0,"Playback starts at unloaded origin");
                    var final=MemberMaterialPlayback.Sample(m,exported,1);
                    Check(final.Index==final.LastIndex && final.LastIndex<exported.momentCurvature.Length,"Playback endpoint uses exported sample");
                    if(final.LastIndex<exported.momentCurvature.Length-1)
                        Check(final.MaxConcrete>=.003f,"Stop at material strain limit");
                    var coordinates=ReinforcementAssessment.Evaluate(m,false,0).Bars;
                    for(int i=0;i<=final.LastIndex;i+=Math.Max(1,final.LastIndex/12))
                    {
                        var state=MemberMaterialPlayback.Sample(m,exported,(i+1f)/(final.LastIndex+1));
                        var point=exported.momentCurvature[state.Index];
                        Close(state.Moment,point.M_kN_m,.00001,"Animation moment is exported, never multiplied");
                        Close(state.Phi,point.phi_1_m,.00000001,"Animation curvature is exported");
                        double maximumStrain=0;
                        foreach(var bar in coordinates)
                            maximumStrain=Math.Max(maximumStrain,Math.Abs(state.StrainAt((float)(m.h_m/2-bar.Depth/1000))));
                        Close(maximumStrain,point.max_steel_strain,.000001,"Recovered individual strains match exported steel maximum");
                    }
                }
                for(int axis=0;axis<2;axis++) for(int face=0;face<2;face++)
                {
                    var evaluated=ReinforcementAssessment.Evaluate(m,m.elementType=="columna",0,face==1,axis==1);
                    Check(evaluated.Valid && evaluated.UltimateAvailable,m.sectionId+" section equilibrium");
                    Close(evaluated.As,m.Ast_mm2,.06,m.sectionId+" exported area agrees (rounded source)");
                    Check(evaluated.Bars.Length==m.steelBars,"Real bar count");
                    foreach(var bar in evaluated.Bars)
                        Check(bar.Depth>0 && bar.Depth<evaluated.H && bar.WidthPosition>0 && bar.WidthPosition<evaluated.B,"Bar coordinates inside section");
                }
            }
        }
        Console.WriteLine("PASS: "+checks+" numerical reinforcement checks (areas, minimum/maximum, axes, faces, axial equilibrium and real exported sections).");
    }
}

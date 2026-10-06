using UnityEngine;

public partial class ElementResultsPanel
{
    private bool reinforcementCalculationOpen, reinforcementReverse, reinforcementRotate;
    private bool reinforcementUseAxial = true;
    private float reinforcementContentHeight = 1350f;

    private void DrawReinforcementAssessment(ElementSelectable element, SectionMaterialData material,
                                              PMCurveData curve, float x, float y, float width)
    {
        float startY=y;
        if (!IsBeam(element) && !IsColumn(element))
        {
            GUI.Label(new Rect(x,y,width,45f), "Chequeo de cuantía disponible para vigas y columnas rectangulares. Muros: requieren criterios propios de armadura distribuida.",mutedStyle);
            return;
        }
        bool column=IsColumn(element);
        GUI.Label(new Rect(x,y,width,22f), "ARMADURA · CUANTÍA · COMPORTAMIENTO",titleStyle); y+=27f;
        float cell=(width-10f)/3;
        if(GUI.Button(new Rect(x,y,cell,25f),reinforcementRotate?"EJE: Mz":"EJE: My"))
            reinforcementRotate=!reinforcementRotate;
        if(GUI.Button(new Rect(x+cell+5f,y,cell,25f),reinforcementReverse?"CARA −":"CARA +"))
            reinforcementReverse=!reinforcementReverse;
        if(column && GUI.Button(new Rect(x+2*(cell+5f),y,cell,25f),reinforcementUseAxial?"P: COMBINACIÓN ACTIVA":"P: 0 · FLEXIÓN PURA"))
            reinforcementUseAxial=!reinforcementUseAxial;
        y+=31f;
        float[] forces=element.data!=null?UnityData.GetElementForces(UnityData.ActiveCombo,element.data.id):null;
        bool haveForces=forces!=null && forces.Length>=6;
        double p=column && reinforcementUseAxial ? (haveForces?forces[0]:double.NaN):0;
        var r=ReinforcementAssessment.Evaluate(material,column,p,reinforcementReverse,reinforcementRotate);
        GUI.Label(new Rect(x,y,width,36f),
            $"ACI 318-08 · chequeo general de sección rectangular con estribos · {(reinforcementRotate?"Mz":"My")} {(reinforcementReverse?"−":"+")}\n"+
            (column && reinforcementUseAxial ? $"P de {UnityData.GetActiveLoadLabel()}: {p:0.###} kN (compresión +).":"Referencia de flexión pura: P = 0 kN."),mutedStyle);
        y+=42f;
        if(!r.Valid)
        {
            GUI.Label(new Rect(x,y,width,65f),r.Reason+" Revise distribución, dimensiones, materiales y disponibilidad de resultados.",failureStyle);
            return;
        }
        GUI.Box(new Rect(x,y,width,91f),GUIContent.none,cardStyle);
        GUI.Label(new Rect(x+10,y+6,width-20,42),
            $"As total: {r.As:0.##} mm²     ρ total = As / Ag = {100*r.RhoTotal:0.###}%\n"+
            (column ? $"Ag = {r.Ag:0.##} mm² · {material.steelBars} barras Ø{material.barDiameter_mm:0.#}":
                $"Fila extrema en tracción: {r.AsTensionRow:0.##} mm² · ρ fila = As,fila / (b·d) = {100*r.RhoFlexural:0.###}%"),valueStyle);
        GUI.Label(new Rect(x+10,y+51,width-20,33),column?
            "Límites generales: 1% ≤ ρ total ≤ 8%. La cuantía no determina por sí sola el tipo de falla.":
            "El mínimo se compara conservadoramente con la fila extrema. Las demás barras sí participan en el equilibrio de la sección.",mutedStyle);
        y+=100f;
        DrawReinforcementGauge(new Rect(x+10,y,width-20,54f),
            column?r.RhoTotal:r.RhoFlexural,column?.01:r.RhoMin,
            column?.08:r.RhoMaxReference,!column);
        y+=65f;
        float half=(width-8f)/2;
        DrawReinforcementCard(new Rect(x,y,half,100),"ARMADURA MÍNIMA",
            $"{(r.MinimumOK?"CUMPLE":"NO CUMPLE")} · As,min = {r.AsMin:0.##} mm²\n"+
            (column?"Comparación con As total.":"Comparación con la fila extrema seleccionada."),r.MinimumOK);
        DrawReinforcementCard(new Rect(x+half+8,y,half,100),column?"ARMADURA MÁXIMA":"ARMADURA MÁXIMA · REFERENCIA",
            column?$"{(r.MaximumOK?"CUMPLE":"NO CUMPLE")} · As,max = {r.AsMax:0.##} mm²\nLímite general de acero longitudinal.":
                $"As,max = {r.AsMax:0.##} mm² · {(r.AsTensionRow<=r.AsMax?"FILA BAJO LÍMITE REF.":"FILA SUPERA LÍMITE REF.")}\n"+
                $"cmax = {r.Cmax:0.##} mm · amax = {r.Amax:0.##} mm\nSección simplemente armada · εt = 0,005.",
            column?r.MaximumOK:r.AsTensionRow<=r.AsMax);
        y+=110f;
        if(!column)
        {
            GUI.Label(new Rect(x,y,width,36),r.UltimateAvailable?
                $"DUCTILIDAD DE LA SECCIÓN COMPLETA: {(r.MaximumOK?"CUMPLE":"NO CUMPLE")} · εt = {r.EpsT:0.00000} · límite general ACI 318-08: εt ≥ 0,004. Se evalúan todas las barras.":
                "DUCTILIDAD DE LA SECCIÓN COMPLETA: pendiente; no se obtuvo equilibrio.",r.MaximumOK?successStyle:failureStyle);
            y+=43f;
        }
        GUI.Label(new Rect(x,y,width,36f),
            "Estos indicadores revisan los criterios indicados; no certifican el diseño sísmico, corte, torsión, anclajes, empalmes ni separación de barras.",mutedStyle); y+=43f;
        GUI.Label(new Rect(x,y,width,23f),"ALCANCE DE CAPACIDAD · PERFIL DE DEFORMACIONES",titleStyle); y+=28f;
        if(r.UltimateAvailable)
        {
            Rect plot=new Rect(x+8,y,Mathf.Min(220f,width*.32f),170f);
            DrawUltimateReinforcementProfile(plot,r);
            float ix=plot.xMax+18f,iw=width-(ix-x);
            GUI.Label(new Rect(ix,y,iw,30f),r.Failure,r.EpsT>=.005?successStyle:r.EpsT<=r.EpsY?failureStyle:valueStyle);
            GUI.Label(new Rect(ix,y+36,iw,86),
                $"c = {r.C:0.##} mm   ·   εcu = 0,003\nεt = {r.EpsT:0.00000}   ·   εy = {r.EpsY:0.00000}\n"+
                $"ϕ de referencia (estribos): {r.Phi:0.###}\nMn Whitney = {r.Mn:0.###} kN·m",textStyle);
            GUI.Label(new Rect(ix,y+122,iw,49),r.EpsT<=r.EpsY?
                "El hormigón alcanza εcu antes de la fluencia del acero extremo en tracción.":r.EpsT>=.005?
                "El acero extremo en tracción supera la fluencia; hay mayor capacidad de deformación.":
                "El acero extremo ha fluido, pero no alcanza el límite de sección controlada por tracción.",mutedStyle);
            y+=198f;
        }
        else { GUI.Label(new Rect(x,y,width,58),r.Reason??"No se obtuvo equilibrio; clasificación pendiente.",failureStyle); y+=68f; }
        GUI.Label(new Rect(x,y,width,46f),
            "Whitney + acero elastoplástico · εcu = 0,003 · flexión uniaxial. Es una predicción de sección al límite, no el estado actual de daño ni un análisis de colapso. El factor ϕ mostrado no modifica las curvas exportadas.",mutedStyle); y+=53f;
        bool haveCapacity=haveForces && curve!=null;
        Vector2 demand=haveCapacity?GetDemand(element,curve):Vector2.zero;
        float ratio=haveCapacity?UnityData.CapacityRatio(curve,demand.x,demand.y):0;
        GUI.Label(new Rect(x,y,width,40),haveCapacity?
            $"CAPACIDAD EXPORTADA (verificación independiente): P = {demand.x:0.###} kN · |M| = {demand.y:0.###} kN·m · D/C = {FormatCapacity(curve,demand)} · {(ratio<=1?"DENTRO":"FUERA")} de curva nominal.":
            "CAPACIDAD EXPORTADA: pendiente; faltan demanda o curva. No se infiere a partir de la cuantía.",haveCapacity && ratio<=1?successStyle:mutedStyle); y+=48f;
        if(GUI.Button(new Rect(x,y,width,25),reinforcementCalculationOpen?"OCULTAR CÁLCULO Y CRITERIOS":"VER CÁLCULO PASO A PASO Y CRITERIOS"))
            reinforcementCalculationOpen=!reinforcementCalculationOpen;
        y+=32f;
        if(reinforcementCalculationOpen)
        {
            string steps=
                $"1. Geometría: b = {r.B:0.##} mm; h = {r.H:0.##} mm; d = {r.D:0.##} mm; Ag = b·h = {r.Ag:0.##} mm².\n"+
                $"2. Acero: As = n·π·Ø²/4 = {material.steelBars}·π·{material.barDiameter_mm:0.##}²/4 = {r.As:0.##} mm².\n"+
                $"3. Cuantía total: As/Ag = {100*r.RhoTotal:0.###}%."+
                (column?$" As,min = 0,01·Ag = {r.AsMin:0.##}; As,max = 0,08·Ag = {r.AsMax:0.##} mm² (10.9.1).\n":
                    $" Cuantía fila: As,fila/(b·d) = {100*r.RhoFlexural:0.###}%.\n"+
                    $"4. As,min = máx(0,25√f'c/fy; 1,4/fy)·b·d = {r.AsMin:0.##} mm² (MPa, mm; 10.5.1).\n"+
                    $"   cmax = 0,375·d = 0,375·{r.D:0.##} = {r.Cmax:0.##} mm.\n"+
                    $"   amax = β1·cmax = {r.Beta1:0.###}·{r.Cmax:0.##} = {r.Amax:0.##} mm.\n"+
                    $"   As,max = (0,85·f'c·b·amax)/fy = (0,85·{material.fc_MPa:0.##}·{r.B:0.##}·{r.Amax:0.##})/{material.fy_MPa:0.##} = {r.AsMax:0.##} mm².\n")+
                $"5. β1 = {r.Beta1:0.###}; εy = fy/Es = {material.fy_MPa:0.##}/{(material.Es_MPa>0?material.Es_MPa:200000):0.##} = {r.EpsY:0.00000}.\n"+
                "6. Equilibrio: Pn = 0,85·f'c·b·a + ΣAs,i·fs,i; a = mín(β1·c,h). Se descuenta el hormigón ocupado por barras dentro del bloque.\n"+
                "   εs,i = 0,003·(c−di)/c; fs,i = limitar(Es·εs,i, −fy, +fy). Todas las barras participan.\n"+
                (r.UltimateAvailable?$"7. Pn = P seleccionado: c = {r.C:0.##} mm; residuo = {r.Residual:0.000000} kN. εt = máx(0; 0,003·(dt−c)/c) = {r.EpsT:0.00000}.\n":"7. Sin equilibrio: no se informa tipo de falla ni resistencia.\n")+
                "8. Compresión: εt ≤ εy; transición: εy < εt < 0,005; tracción: εt ≥ 0,005. ϕ = 0,65 → 0,90 para estribos (9.3.2; 10.3).\n"+
                (column?"9. La carga axial cambia el eje neutro y el comportamiento; no usar solo ρ total para clasificar la columna.\n":
                    $"9. Referencias simplemente armadas, P=0: ρb = 0,85·β1·f'c/fy·εcu/(εcu+εy) = {100*r.RhoBalancedReference:0.###}%; ρmax = As,max/(b·d) = {100*r.RhoMaxReference:0.###}%.\n"+
                    "   cmax/d = εcu/(εcu+εt) = 0,003/(0,003+0,005) = 0,375. As,max es referencia simplemente armada; la sección completa se verifica por compatibilidad de deformaciones.\n")+
                "Condición balanceada: acero extremo alcanza εy junto con εcu del hormigón; no garantiza ductilidad. Subarmada: fluencia del acero antes del aplastamiento del hormigón en compresión.\n"+
                "Ámbito: ACI 318-08 general, sin excepciones de mínimo ni requisitos sísmicos específicos del capítulo 21. Recubrimiento almacenado = distancia al centro de las barras.";
            float stepsHeight=textStyle.CalcHeight(new GUIContent(steps),width-16);
            GUI.Label(new Rect(x+8,y,width-16,stepsHeight),steps,textStyle);
            if(GUI.Button(new Rect(x,y+stepsHeight+5,width,24),"FUENTE: MANUAL ACI · CRITERIOS DE FLEXIÓN"))
                Application.OpenURL("https://www.concrete.org/portals/0/files/pdf/previews/sp1711v1.pdf");
            y+=stepsHeight+34f;
        }
        reinforcementContentHeight=335f+memberPlaybackHeight+y-startY+24f;
    }

    private void DrawReinforcementCard(Rect rect,string label,string detail,bool ok)
    {
        GUI.Box(rect,GUIContent.none,cardStyle);
        GUI.Label(new Rect(rect.x+9,rect.y+5,rect.width-18,21),label,titleStyle);
        GUI.Label(new Rect(rect.x+9,rect.y+29,rect.width-18,rect.height-34),detail,ok?successStyle:failureStyle);
    }

    private void DrawReinforcementGauge(Rect rect,double value,double minimum,double maximum,bool reference)
    {
        double upper=System.Math.Max(maximum*1.3,value*1.2);
        Rect track=new Rect(rect.x,rect.y+19,rect.width,12);
        Color old=GUI.color;
        GUI.color=new Color(.18f,.25f,.32f); GUI.DrawTexture(track,Texture2D.whiteTexture);
        GUI.color=new Color(.15f,.7f,.5f,.55f);
        float left=(float)(minimum/upper)*track.width,right=(float)(maximum/upper)*track.width;
        GUI.DrawTexture(new Rect(track.x+left,track.y,right-left,track.height),Texture2D.whiteTexture);
        GUI.color=old;
        float marker=(float)(value/upper)*track.width;
        DrawMarker(new Vector2(track.x+marker,track.center.y),Color.cyan,14);
        GUI.Label(new Rect(rect.x,rect.y,rect.width,18),reference?
            "CUANTÍA DE LA FILA · máximo de referencia simplemente armada, no veredicto":"CUANTÍA TOTAL · banda de límites generales",mutedStyle);
        GUI.Label(new Rect(rect.x,rect.y+34,rect.width,20),
            $"Mín: {100*minimum:0.###}%       Actual: {100*value:0.###}%       {(reference?"Ref. límite":"Máx")}: {100*maximum:0.###}%",valueStyle);
    }

    private void DrawUltimateReinforcementProfile(Rect rect,ReinforcementAssessment.Result r)
    {
        // La sección mantiene la relación real b/h, incluso al cambiar de eje.
        float scale=Mathf.Min(rect.width/(float)r.B,rect.height/(float)r.H);
        Rect section=new Rect(rect.x,rect.y,(float)r.B*scale,(float)r.H*scale);
        Color old=GUI.color; GUI.color=new Color(.13f,.27f,.36f);
        GUI.DrawTexture(section,Texture2D.whiteTexture);
        GUI.color=new Color(1f,.57f,.23f,.48f);
        GUI.DrawTexture(new Rect(section.x,section.y,section.width,
            Mathf.Clamp01((float)(r.C/r.H))*section.height),Texture2D.whiteTexture); GUI.color=old;
        foreach(var bar in r.Bars)
        {
            float yy=section.y+(float)(bar.Depth/r.H)*section.height;
            Color color=bar.Depth>r.C?new Color(.2f,1f,.5f):new Color(1f,.4f,.25f);
            DrawMarker(new Vector2(section.x+(float)(bar.WidthPosition/r.B)*section.width,yy),color,6);
        }
        if(r.C<r.H)
        {
            float neutral=section.y+(float)(r.C/r.H)*section.height;
            DrawLine(new Vector2(section.x,neutral),new Vector2(section.xMax,neutral),Color.yellow,2);
        }
        GUI.Label(new Rect(section.x+3,section.y+3,section.width-6,20),"εcu 0,003",mutedStyle);
        GUI.Label(new Rect(rect.x,rect.yMax+2,rect.width,19),"Naranja: compresión · verde: tracción",mutedStyle);
    }
}

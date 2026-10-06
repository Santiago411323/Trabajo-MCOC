using UnityEngine;

// Read-only calculation sheet inside the existing results drawer. It uses the
// same local end actions as the diagram, but checks their equilibrium against
// the separately exported distributed loads whenever those are available.
public partial class ElementResultsPanel
{
    private void DrawManualCheck(ElementSelectable element, float x, float y, float width)
    {
        float half = (width - 7f) * .5f;
        if (GUI.Button(new Rect(x, y, half, 27f), manualCheckOpen
            ? "OCULTAR VERIFICACIÓN MANUAL" : "VER CÁLCULO PASO A PASO"))
            manualCheckOpen = !manualCheckOpen;
        if (GUI.Button(new Rect(x + half + 7f, y, half, 27f), "TRAZABILIDAD DE ESTE PUNTO"))
        { SetView(ResultView.Audit); return; }
        if (!manualCheckOpen || element.data == null) return;

        Rect card = new Rect(x, y + 33f, width, 450f);
        GUI.Box(card, GUIContent.none, cardStyle);
        float left = card.x + 12f, line = card.y + 9f, available = card.width - 24f;
        int id = element.data.id;
        if (!UnityData.TryGetFrameGeometry(id, out FrameGeometry frame))
        {
            GUI.Label(new Rect(left, line, available, 35f), "No hay geometría analítica para verificar esta barra.", failureStyle);
            return;
        }
        float[] raw = UnityData.GetElementForces(UnityData.ActiveCombo, id);
        if (!FrameForces.IsValid(raw))
        {
            GUI.Label(new Rect(left, line, available, 35f), "No hay doce acciones locales válidas para esta barra.", failureStyle);
            return;
        }

        double length = frame.Length;
        float t = Mathf.Clamp01(manualCheckPosition);
        double position = length * t;
        double qy = (raw[1] + raw[7]) / length;
        double qz = (raw[2] + raw[8]) / length;
        double myI = raw[4], myJ = -raw[10];
        double mzI = raw[5], mzJ = -raw[11];
        bool hasSource = TryGetSourceLoads(id, frame, out double sourceY, out double sourceZ,
            out double gZ, out double qZ);
        bool hasMobile = UnityData.MobileForces.TryGetValue(id, out float[] mobile) && FrameForces.IsValid(mobile);
        double mobileY = hasMobile ? (mobile[1] + mobile[7]) / length : 0;
        double mobileZ = hasMobile ? (mobile[2] + mobile[8]) / length : 0;
        double sourceError = hasSource ? System.Math.Max(System.Math.Abs(qy - mobileY - sourceY),
            System.Math.Abs(qz - mobileZ - sourceZ)) : 0;

        GUI.Label(new Rect(left, line, available, 19f), "01  MODELO Y CARGAS EXPORTADAS", titleStyle);
        line += 20f;
        GUI.Label(new Rect(left, line, available, 18f),
            $"{ElementTag(element)} · nodos {element.data.nodeI} → {element.data.nodeJ} · L = {length:0.###} m · {UnityData.GetActiveLoadLabel()}", textStyle);
        line += 19f;
        if (element.data.type == "viga")
        {
            double slabG = element.data.deadLoad / length;
            double slabQ = element.data.liveLoad / length;
            double beamG = 25.0 * element.data.width_m * System.Math.Max(0, element.data.height_m - .15);
            GUI.Label(new Rect(left, line, available, 19f),
                $"Losa: área {element.data.areaTributaria:0.###} m² · G {element.data.deadLoad:0.###}/{length:0.###} = {slabG:0.###} kN/m · Q {element.data.liveLoad:0.###}/{length:0.###} = {slabQ:0.###} kN/m", mutedStyle);
            line += 19f;
            GUI.Label(new Rect(left, line, available, 19f),
                $"PP viga: {element.data.width_m:0.###} × ({element.data.height_m:0.###} − 0,15) × 25 = {beamG:0.###} kN/m · G exportada {gZ:0.###} · Q exportada {qZ:0.###} kN/m", mutedStyle);
            line += 19f;
        }
        GUI.Label(new Rect(left, line, available, 19f), hasSource
            ? $"Carga local activa: qy = {sourceY:0.###}, qz = {sourceZ:0.###} kN/m (de elementLoads; signo para equilibrio interno)"
            : "Sin elementLoads exportados: q se infiere de las acciones de extremo; chequeo no independiente.",
            hasSource ? textStyle : failureStyle);
        line += 23f;

        GUI.Label(new Rect(left, line, available, 19f), "02  ACCIONES LOCALES DE OPENSEES", titleStyle);
        line += 20f;
        GUI.Label(new Rect(left, line, available, 19f),
            $"Extremo I: Vz = {raw[2]:0.###} kN · My = {myI:0.###} kN·m · Vy = {raw[1]:0.###} kN · Mz = {mzI:0.###} kN·m", textStyle);
        line += 19f;
        GUI.Label(new Rect(left, line, available, 19f),
            $"Extremo J interno = −acción J OpenSees: Vz = {-raw[8]:0.###} · My = {myJ:0.###} · Vy = {-raw[7]:0.###} · Mz = {mzJ:0.###}", mutedStyle);
        line += 24f;

        GUI.Label(new Rect(left, line, available, 19f), "03  RECORRE LA BARRA Y SUSTITUYE", titleStyle);
        line += 20f;
        GUI.Label(new Rect(left, line, 165f, 20f), $"x = {position:0.###} m · {t * 100:0.#}%", valueStyle);
        manualCheckPosition = GUI.HorizontalSlider(new Rect(left + 170f, line + 5f, available - 180f, 18f), t, 0f, 1f);
        if (diagrams != null) diagrams.ShowSelectedComponentCursor(element, forceNames[forceIndex], manualCheckPosition);
        line += 24f;
        FrameSectionForces at = FrameForces.Evaluate(raw, length, t);
        string formula, substitution;
        switch (forceIndex)
        {
            case 0:
                formula = "N(x) = −Ni(1 − x/L) + Nj(x/L)";
                substitution = $"N({position:0.###}) = −({raw[0]:0.###})(1 − {t:0.###}) + ({raw[6]:0.###})({t:0.###}) = {at.N:0.###} kN";
                break;
            case 1:
                formula = "Vy(x) = Vy,I − qy·x";
                substitution = $"Vy({position:0.###}) = {raw[1]:0.###} − ({qy:0.###})({position:0.###}) = {at.Vy:0.###} kN";
                break;
            case 2:
                formula = "Vz(x) = Vz,I − qz·x";
                substitution = $"Vz({position:0.###}) = {raw[2]:0.###} − ({qz:0.###})({position:0.###}) = {at.Vz:0.###} kN";
                break;
            case 3:
                formula = "My(x) = My,I + Vz,I·x − qz·x²/2";
                substitution = $"My({position:0.###}) = {myI:0.###} + ({raw[2]:0.###})({position:0.###}) − ({qz:0.###})({position:0.###})²/2 = {at.My:0.###} kN·m";
                break;
            default:
                formula = "Mz(x) = Mz,I − Vy,I·x + qy·x²/2";
                substitution = $"Mz({position:0.###}) = {mzI:0.###} − ({raw[1]:0.###})({position:0.###}) + ({qy:0.###})({position:0.###})²/2 = {at.Mz:0.###} kN·m";
                break;
        }
        GUI.Label(new Rect(left, line, available, 19f), formula, textStyle);
        line += 19f;
        GUI.Label(new Rect(left, line, available, 38f), substitution, valueStyle);
        line += 42f;

        GUI.Label(new Rect(left, line, available, 19f), "04  EQUILIBRIO EN J", titleStyle);
        line += 20f;
        // Use the independent model load for the base cases. Mobile increments
        // are nodal; their effective q is shown separately and is not mistaken
        // for a physical point load acting inside this member.
        double checkY = hasSource ? sourceY + mobileY : qy;
        double checkZ = hasSource ? sourceZ + mobileZ : qz;
        double vyJ = raw[1] - checkY * length;
        double vzJ = raw[2] - checkZ * length;
        double myAtJ = myI + raw[2] * length - .5 * checkZ * length * length;
        double mzAtJ = mzI - raw[1] * length + .5 * checkY * length * length;
        double forceError = System.Math.Max(System.Math.Abs(vyJ + raw[7]), System.Math.Abs(vzJ + raw[8]));
        double momentError = System.Math.Max(System.Math.Abs(myAtJ - myJ), System.Math.Abs(mzAtJ - mzJ));
        bool passes = hasSource && sourceError <= .05 && forceError <= .05 && momentError <= .25;
        GUI.Label(new Rect(left, line, available, 19f),
            $"Vj: Vy {vyJ:0.###} / {-raw[7]:0.###} · Vz {vzJ:0.###} / {-raw[8]:0.###} kN (calculado / OpenSees)", textStyle);
        line += 19f;
        GUI.Label(new Rect(left, line, available, 19f),
            $"Mj: My {myAtJ:0.###} / {myJ:0.###} · Mz {mzAtJ:0.###} / {mzJ:0.###} kN·m", textStyle);
        line += 19f;
        string verdict = passes ? (hasMobile ? "PASS BASE + INCREMENTO NODAL" : "PASS") : "REVISAR";
        GUI.Label(new Rect(left, line, available, 36f), hasSource
            ? $"{verdict} · Δq {sourceError:0.####} kN/m · ΔV {forceError:0.####} kN · ΔM {momentError:0.####} kN·m  (tol. 0,05 / 0,25)"
            : "SIN VERIFICACIÓN INDEPENDIENTE: falta el registro de carga distribuida.",
            passes ? successStyle : failureStyle);
        line += 35f;
        if (hasMobile)
        {
            GUI.Label(new Rect(left, line, available, 32f),
                "Carga móvil activa: la parte G/Q se contrasta con cargas exportadas; el incremento nodal se toma de fuerzas OpenSees. No se inventa una carga puntual interior sobre esta viga.", mutedStyle);
            line += 35f;
        }
        GUI.Label(new Rect(left, line, available, 32f),
            "Este chequeo verifica cargas y equilibrio de la curva. Los momentos de extremo requieren resolver la rigidez y los giros del pórtico completo; qL²/8 supone apoyos simples.", mutedStyle);
    }

    public static bool TryGetSourceLoads(int elementId, FrameGeometry frame,
        out double qy, out double qz, out double gZ, out double qZ)
    {
        qy = qz = gZ = qZ = 0;
        P1L4Extras extras = UnityData.Structure != null ? UnityData.Structure.p1l4 : null;
        if (extras == null || extras.elementLoads == null) return false;
        double factorG = 0, factorQ = 0, factorEX = 0, factorEY = 0;
        if (UnityData.UseBaseCaseFactors)
        {
            factorG = UnityData.FactorG; factorQ = UnityData.FactorQ;
            factorEX = UnityData.FactorEX; factorEY = UnityData.FactorEY;
        }
        else
        {
            string active = UnityData.ActiveCombo;
            if (extras.combinations != null)
                foreach (ComboInfo combo in extras.combinations)
                    if (combo != null && combo.name == active)
                    {
                        factorG = combo.G; factorQ = combo.Q;
                        factorEX = combo.EX; factorEY = combo.EY;
                        break;
                    }
            if (active == "G") factorG = 1;
            if (active == "Q") factorQ = 1;
            if (active == "EX") factorEX = 1;
            if (active == "EY") factorEY = 1;
        }
        foreach (ElementLoadRecord load in extras.elementLoads)
        {
            if (load == null || load.id != elementId) continue;
            double localY = -(load.wx * frame.Y[0] + load.wy * frame.Y[1] + load.wz * frame.Y[2]);
            double localZ = -(load.wx * frame.Z[0] + load.wy * frame.Z[1] + load.wz * frame.Z[2]);
            double factor = load.@case == "G" ? factorG : load.@case == "Q" ? factorQ :
                load.@case == "EX" ? factorEX : load.@case == "EY" ? factorEY : 0;
            qy += factor * localY; qz += factor * localZ;
            if (load.@case == "G") gZ += localZ;
            if (load.@case == "Q") qZ += localZ;
        }
        return true;
    }
}

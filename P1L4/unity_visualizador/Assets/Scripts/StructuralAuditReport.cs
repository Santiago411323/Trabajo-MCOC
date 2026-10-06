using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum AuditTone { Normal, Pass, Warning, Fail }

public sealed class AuditRow
{
    public readonly string Text;
    public readonly AuditTone Tone;
    public readonly bool Heading;
    public AuditRow(string text, AuditTone tone = AuditTone.Normal, bool heading = false)
    { Text = text; Tone = tone; Heading = heading; }
}

// A trace of the actual exported model, not an additional analysis engine.
// A local equilibrium check does not prove the stiffness solution of the frame.
public sealed class StructuralAuditReport
{
    public readonly List<AuditRow> Rows = new List<AuditRow>();
    private static SlabLoadCatalog slabCatalog;
    private static bool triedSlabCatalog;

    private void Section(string title) => Rows.Add(new AuditRow(title, AuditTone.Normal, true));
    private void Add(string text, AuditTone tone = AuditTone.Normal) => Rows.Add(new AuditRow(text, tone));

    public string AsText()
    {
        var result = new StringBuilder("FICHA DE TRAZABILIDAD ESTRUCTURAL — MCOC\n");
        foreach (AuditRow row in Rows)
            result.Append(row.Heading ? "\n== " : "  ").Append(row.Text)
                .Append(row.Heading ? " ==" : "").Append('\n');
        return result.ToString();
    }

    public static StructuralAuditReport Build(ElementSelectable selected, float position01, int forceIndex)
    {
        var report = new StructuralAuditReport();
        StructureData data = UnityData.Structure;
        if (selected == null || data == null)
        {
            report.Add("No hay elemento o estructura cargada.", AuditTone.Warning);
            return report;
        }
        P1L4Extras extras = data.p1l4;
        bool stale = StructuralModelEditor.ResultsStale;
        ElementData member = selected.data;
        report.Section("01  IDENTIDAD Y PROCEDENCIA");
        string tag = member != null ? member.elementTag : "Muro " + selected.wallId;
        report.Add($"{tag} · ID interno {(member != null ? member.id : selected.wallId)} · ID Unity {selected.gameObject.name}");
        report.Add($"Tipo {(member != null ? member.type : "muro")} · edificio {(member != null ? member.sourceBuilding : selected.wallSourceBuilding)} · piso {selected.visualFloor}");
        report.Add(UnityData.ActiveCombo!=null && UnityData.ActiveCombo.StartsWith("LRFD_")?
            "Fuente: P1L4/lrfd_results/resultados_unity.json · OpenSees localForce · escenario independiente LRFD":
            $"Fuente: Assets/Resources/estructura_p1l4_unity.json · exportación P1L4 v{(extras != null ? extras.version : "?")} · fuerzas {(extras != null ? extras.elementForceCoordinates : "no informadas")}");
        report.Add($"Caso mostrado: {UnityData.GetActiveLoadLabel()}" +
            (UnityData.MobileForces.Count > 0 ? " + incremento móvil OpenSees" : ""));
        if (extras != null && extras.analysisModel != null)
        {
            report.Add("Gravedad: " + extras.analysisModel.gravedad);
            report.Add("Sismo: " + extras.analysisModel.sismo + " · torsión: " + extras.analysisModel.torsionAccidental);
        }
        if (stale)
            report.Add("MODELO EDITADO: los resultados exportados están pendientes de reanálisis. Ningún PASS de respuesta puede considerarse vigente.", AuditTone.Warning);

        report.Section("02  GEOMETRÍA, CONECTIVIDAD Y MATERIAL");
        int ni = member != null ? member.nodeI : selected.nodeIId;
        int nj = member != null ? member.nodeJ : selected.nodeJId;
        report.Add($"Nodos I {ni} → J {nj} · {NodeLocation(data, ni)} → {NodeLocation(data, nj)}");
        report.Add($"Conectados en I: {Connections(data, ni)} · en J: {Connections(data, nj)}");
        report.Add($"Apoyo I: {SupportText(UnityData.GetNodeSupport(ni))} · apoyo J: {SupportText(UnityData.GetNodeSupport(nj))}");
        if (member != null && UnityData.TryGetFrameGeometry(member.id, out FrameGeometry frame))
        {
            report.Add($"Sección {member.sectionId} · {member.width_m:0.###} × {member.height_m:0.###} m · L = {frame.Length:0.###} m");
            report.Add($"Ejes locales globales: x' {Axis(frame.X)} · y' {Axis(frame.Y)} · z' {Axis(frame.Z)}");
        }
        else if (selected.isWall)
            report.Add($"Muro: espesor {selected.wallThickness:0.###} m · longitud {selected.wallLength:0.###} m · sección P–M {selected.pmSectionId}");
        string sectionId = member != null ? member.sectionId : selected.pmSectionId;
        SectionMaterialData material = UnityData.GetMaterial(sectionId);
        if (material != null)
        {
            report.Add($"Material {material.materialName} · f'c {material.fc_MPa:0.###} MPa · fy {material.fy_MPa:0.###} MPa");
        }
        else
            report.Add("No hay ficha de material asociada a esta sección exportada.", AuditTone.Warning);
        SectionMaterialData reinforcement = material != null && material.steelBars > 0 && material.barDiameter_mm > 0f
            ? material : UnityData.GetMaterial(selected.pmSectionId);
        if (reinforcement != null && reinforcement.steelBars > 0 && reinforcement.barDiameter_mm > 0f)
            report.Add($"Armadura sección {reinforcement.sectionId}: {reinforcement.steelBars} barras × Ø{reinforcement.barDiameter_mm:0.#} mm · As {reinforcement.Ast_mm2:0.#} mm²");
        else
            report.Add("Armadura no informada en los datos exportados para esta sección.", AuditTone.Warning);

        if (member != null && member.type == "viga") report.AddBeamTrace(data, member, stale);
        if (member != null) report.AddFrameResponse(member, position01, forceIndex, stale);
        else report.AddWallResponse(selected, stale);
        report.AddGlobalBalance(extras, stale);
        report.Section("LÍMITES DEL RESULTADO");
        report.Add("El equilibrio local comprueba cargas y signos del diagrama; no reemplaza una validación independiente de la rigidez y los giros del pórtico.");
        report.Add("Las losas transfieren cargas por áreas tributarias; no son elementos shell y no tienen Mxx/Myy/Qx/Qy calculados.");
        report.Add("El modelo estructural exportado es lineal. La escala de deformada amplificada cambia solo la visualización, no los desplazamientos numéricos.");
        if (UnityData.MobileForces.Count > 0)
            report.Add("La carga móvil usa respuesta incremental global OpenSees y transferencia nodal; no se infiere una fuerza puntual interior de barra.");
        return report;
    }

    private void AddBeamTrace(StructureData data, ElementData beam, bool stale)
    {
        Section("03  LOSA → VIGA → CARGA");
        SlabLoadCatalog catalog = GetSlabCatalog();
        if (catalog == null || catalog.slabs == null)
        {
            Add("Catálogo slab_load_surfaces.json no disponible: aporte tributario sin cotejo.", AuditTone.Warning);
            return;
        }
        double area = 0, g = 0, q = 0;
        int count = 0;
        foreach (SlabLoadMetadata slab in catalog.slabs)
        {
            if (slab == null || slab.receivers == null) continue;
            foreach (SlabReceiverMetadata receiver in slab.receivers)
            {
                if (receiver == null || receiver.beam != beam.id) continue;
                area += receiver.area; g += receiver.G; q += receiver.Q; count++;
                Add($"Losa {slab.id}: A {receiver.area:0.###} m² · G {receiver.G:0.###} kN · Q {receiver.Q:0.###} kN");
            }
        }
        Add($"Σ losas ({count}): A {area:0.###} m² · G {g:0.###} kN · Q {q:0.###} kN");
        Add($"Viga exportada: A {beam.areaTributaria:0.###} m² · G {beam.deadLoad:0.###} kN · Q {beam.liveLoad:0.###} kN");
        double da = Math.Abs(area - beam.areaTributaria), dg = Math.Abs(g - beam.deadLoad), dq = Math.Abs(q - beam.liveLoad);
        bool pass = count > 0 && da <= .01 && dg <= .1 && dq <= .1;
        Add($"{(stale ? "PENDIENTE" : pass ? "PASS" : "REVISAR")} aportes: ΔA {da:0.####} m² · ΔG {dg:0.####} kN · ΔQ {dq:0.####} kN (tol. 0,01 m² / 0,1 kN)",
            stale ? AuditTone.Warning : pass ? AuditTone.Pass : AuditTone.Fail);
        if (!UnityData.TryGetFrameGeometry(beam.id, out FrameGeometry frame)) return;
        double selfPerM = 25.0 * beam.width_m * Math.Max(0, beam.height_m - .15);
        Add($"PP viga bajo losa: {beam.width_m:0.###}({beam.height_m:0.###} − 0,15)×25 = {selfPerM:0.###} kN/m · {selfPerM * frame.Length:0.###} kN total");
        Add($"Carga lineal esperada: G = {beam.deadLoad:0.###}/{frame.Length:0.###} + {selfPerM:0.###} = {beam.deadLoad / frame.Length + selfPerM:0.###} kN/m; Q = {beam.liveLoad / frame.Length:0.###} kN/m");
        if (ElementResultsPanel.TryGetSourceLoads(beam.id, frame, out _, out _, out double gLoad, out double qLoad))
        {
            double dgLoad = Math.Abs(gLoad - beam.deadLoad / frame.Length - selfPerM);
            double dqLoad = Math.Abs(qLoad - beam.liveLoad / frame.Length);
            bool loadPass = dgLoad < .02 && dqLoad < .02;
            Add($"{(stale ? "PENDIENTE" : loadPass ? "PASS" : "REVISAR")} eleLoad G/Q: exportado {gLoad:0.###} / {qLoad:0.###} kN/m · ΔG {dgLoad:0.####} · ΔQ {dqLoad:0.####} (tol. 0,02)",
                stale ? AuditTone.Warning : loadPass ? AuditTone.Pass : AuditTone.Fail);
        }
        else Add("No se exportó elementLoads: no puede cotejarse la carga lineal.", AuditTone.Warning);
    }

    private void AddFrameResponse(ElementData member, float position01, int forceIndex, bool stale)
    {
        Section(member.type == "viga" ? "04  OPENSEES → DIAGRAMA" : "03  OPENSEES → DIAGRAMA");
        if (!UnityData.TryGetFrameGeometry(member.id, out FrameGeometry frame))
        { Add("Sin geometría analítica.", AuditTone.Warning); return; }
        float[] raw = UnityData.GetElementForces(UnityData.ActiveCombo, member.id);
        if (!FrameForces.IsValid(raw))
        { Add("No hay doce fuerzas locales válidas para el escenario activo.", AuditTone.Warning); return; }
        float t = Mathf.Clamp01(position01);
        FrameSectionForces section = FrameForces.Evaluate(raw, frame.Length, t);
        string[] names = { "N", "Vy", "Vz", "My", "Mz" };
        float[] values = { section.N, section.Vy, section.Vz, section.My, section.Mz };
        int selectedIndex = Mathf.Clamp(forceIndex, 0, 4);
        string unit = selectedIndex > 2 ? "kN·m" : "kN";
        Add($"Acciones resistentes I (N,Vy,Vz,T,My,Mz): {Forces(raw, 0)}");
        Add($"Acciones resistentes J (N,Vy,Vz,T,My,Mz): {Forces(raw, 6)} · en la cara J interna se invierte el signo de V/M.");
        Add($"Lectura actual: {names[selectedIndex]}({t * frame.Length:0.###} m; {t * 100:0.#}% L) = {values[selectedIndex]:0.###} {unit}. Ver sustitución en «Diagramas de esfuerzos».");
        bool source = ElementResultsPanel.TryGetSourceLoads(member.id, frame,
            out double sourceY, out double sourceZ, out _, out _);
        bool mobile = UnityData.MobileForces.TryGetValue(member.id, out float[] increment) && FrameForces.IsValid(increment);
        double extraY = mobile ? (increment[1] + increment[7]) / frame.Length : 0;
        double extraZ = mobile ? (increment[2] + increment[8]) / frame.Length : 0;
        if (!source)
        { Add("Carga distribuida no exportada; el cierre obtenido solo con fuerzas de extremo no sería independiente.", AuditTone.Warning); return; }
        double qy = sourceY + extraY, qz = sourceZ + extraZ;
        double vyJ = raw[1] - qy * frame.Length;
        double vzJ = raw[2] - qz * frame.Length;
        double myJ = raw[4] + raw[2] * frame.Length - .5 * qz * frame.Length * frame.Length;
        double mzJ = raw[5] - raw[1] * frame.Length + .5 * qy * frame.Length * frame.Length;
        double forceError = Math.Max(Math.Abs(vyJ + raw[7]), Math.Abs(vzJ + raw[8]));
        double momentError = Math.Max(Math.Abs(myJ + raw[10]), Math.Abs(mzJ + raw[11]));
        bool pass = forceError <= .05 && momentError <= .25;
        Add($"{(stale ? "PENDIENTE" : pass ? "PASS" : "REVISAR")} equilibrio I→J: ΔV {forceError:0.####} kN (tol. 0,05) · ΔM {momentError:0.####} kN·m (tol. 0,25)" +
            (mobile ? " · incremento móvil nodal no validado independientemente" : ""),
            stale ? AuditTone.Warning : pass ? AuditTone.Pass : AuditTone.Fail);
    }

    private void AddWallResponse(ElementSelectable wall, bool stale)
    {
        Section("03  RESULTADOS DEL MURO");
        DemandRecord demand = null;
        if (!UnityData.UseBaseCaseFactors && wall.pmDemands != null)
            foreach (DemandRecord candidate in wall.pmDemands)
                if (candidate != null && candidate.combo == UnityData.ActiveCombo)
                { demand = candidate; break; }
        if (demand != null)
            Add($"Demanda exportada: {demand.combo} · P {demand.P_kN:0.###} kN · M {demand.M_kN_m:0.###} kN·m" +
                (stale ? " · PENDIENTE DE REANÁLISIS" : ""), stale ? AuditTone.Warning : AuditTone.Normal);
        else Add("Sin demanda P–M exportada para el caso activo; no se muestra otro caso como si fuera este.", AuditTone.Warning);
        PMCurveData curve = UnityData.GetPMCurve(wall.pmSectionId);
        Add(curve != null ? $"Curva P–M exportada: {curve.sectionId} · {(curve.points != null ? curve.points.Length : 0)} puntos." :
            "Sin curva P–M exportada para esta sección.", curve != null ? AuditTone.Normal : AuditTone.Warning);
        Add("Este muro no dispone aquí de un vector de doce fuerzas de barra. No se construye un diagrama N/V/M de viga.");
    }

    private void AddGlobalBalance(P1L4Extras extras, bool stale)
    {
        Section("BALANCE GLOBAL DEL MODELO");
        if (UnityData.UseBaseCaseFactors)
        { Add("Factores G/Q/EX/EY editados: este escenario no trae un balance global exportado propio. Los chequeos locales sí usan la superposición activa.", AuditTone.Warning); return; }
        GlobalEquilibriumRecord balance = extras != null && extras.analysisModel != null && extras.analysisModel.equilibrio != null
            ? extras.analysisModel.equilibrio.For(UnityData.ActiveCombo) : null;
        if (balance == null || balance.desbalance_kN == null || balance.desbalance_kN.Length < 3)
        { Add("No hay resumen de reacciones globales exportado para este caso.", AuditTone.Warning); return; }
        double error = Math.Sqrt(balance.desbalance_kN[0] * balance.desbalance_kN[0] +
            balance.desbalance_kN[1] * balance.desbalance_kN[1] + balance.desbalance_kN[2] * balance.desbalance_kN[2]);
        bool pass = error <= .1;
        Add($"{(stale ? "PENDIENTE" : pass ? "PASS" : "REVISAR")} carga + reacciones = ({balance.desbalance_kN[0]:0.####}, {balance.desbalance_kN[1]:0.####}, {balance.desbalance_kN[2]:0.####}) kN · |Δ| {error:0.####} kN (tol. 0,1)",
            stale ? AuditTone.Warning : pass ? AuditTone.Pass : AuditTone.Fail);
        Add("Balance global leído del análisis exportado; Unity no volvió a ejecutar OpenSees para este chequeo.");
    }

    private static SlabLoadCatalog GetSlabCatalog()
    {
        if (!triedSlabCatalog)
        {
            triedSlabCatalog = true;
            string asset = DesktopModelFile.SlabCatalogJson;
            if (asset != null) slabCatalog = JsonUtility.FromJson<SlabLoadCatalog>(asset);
        }
        return slabCatalog;
    }

    private static string NodeLocation(StructureData data, int id)
    {
        foreach (NodeData node in data.nodes ?? new NodeData[0])
            if (node.id == id) return $"({node.x:0.##}, {node.y:0.##}, {node.z:0.##}) m";
        return "sin coordenadas";
    }

    private static string Connections(StructureData data, int nodeId)
    {
        var labels = new List<string>();
        int count = 0;
        foreach (ElementData element in data.elements ?? new ElementData[0])
            if (element.nodeI == nodeId || element.nodeJ == nodeId)
            {
                count++;
                if (labels.Count < 4) labels.Add(element.elementTag);
            }
        foreach (WallData wall in data.walls ?? new WallData[0])
            if (wall.nodeI == nodeId || wall.nodeJ == nodeId)
            {
                count++;
                if (labels.Count < 4) labels.Add("Muro " + wall.id);
            }
        return count == 0 ? "ninguno registrado" : string.Join(", ", labels.ToArray()) + (count > 4 ? $" (+{count - 4})" : "");
    }

    private static string SupportText(SupportData support)
    {
        return support == null ? "sin fijación al suelo registrada (no implica articulación)" :
            $"{support.type} [Ux{support.ux} Uy{support.uy} Uz{support.uz} Rx{support.rx} Ry{support.ry} Rz{support.rz}]";
    }

    private static string Axis(double[] v) => $"({v[0]:0.##}, {v[1]:0.##}, {v[2]:0.##})";
    private static string Forces(float[] v, int first) =>
        $"{v[first]:0.##}, {v[first+1]:0.##}, {v[first+2]:0.##}, {v[first+3]:0.##}, {v[first+4]:0.##}, {v[first+5]:0.##}";
}

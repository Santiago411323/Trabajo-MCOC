using System;
using UnityEditor;
using UnityEngine;

public static class ManualDiagramValidation
{
    [MenuItem("MCOC/Resultados/Validar verificación manual E1_72")]
    public static void ValidateBatch()
    {
        TextAsset json = Resources.Load<TextAsset>("estructura_p1l4_unity");
        Require(json != null, "Falta el JSON estructural de Unity.");
        StructureData data = JsonUtility.FromJson<StructureData>(json.text);
        Require(data != null && data.p1l4 != null && data.p1l4.elementLoads != null,
            "No se leyeron las cargas distribuidas exportadas.");
        UnityData.LoadData(data);
        Require(UnityData.TryGetFrameGeometry(72, out FrameGeometry frame), "Falta la geometría de E1_72.");
        Require(Math.Abs(frame.Length - 7.51) < .001, "Longitud distinta de 7,51 m.");
        UnityData.ActiveCombo = "C1";
        Require(ElementResultsPanel.TryGetSourceLoads(72, frame, out double qy, out double qz,
            out double g, out double q), "No se encontraron cargas para E1_72.");
        Require(Math.Abs(g - 19.0593248754) < .002 && Math.Abs(q - 7.3301770672) < .002,
            "La carga G/Q no coincide con el modelo exportado.");
        Require(Math.Abs(qy) < .001 && Math.Abs(qz - (g + .5 * q)) < .002,
            "La combinación C1 de cargas no coincide con el modelo.");
        float[] ends = UnityData.GetElementForces("C1", 72);
        Require(FrameForces.IsValid(ends), "Faltan las acciones locales C1.");
        FrameSectionForces middle = FrameForces.Evaluate(ends, frame.Length, .5f);
        Require(Math.Abs(middle.My - 74.869f) < .05f, "Momento central inesperado.");
        double predictedJ = ends[4] + ends[2] * frame.Length - .5 * qz * frame.Length * frame.Length;
        Require(Math.Abs(predictedJ + ends[10]) < .05, "El momento calculado en J no cierra.");

        UnityData.UseBaseCaseFactors = true;
        UnityData.FactorG = .88f; UnityData.FactorQ = .98f;
        UnityData.FactorEX = UnityData.FactorEY = 0f;
        Require(ElementResultsPanel.TryGetSourceLoads(72, frame, out _, out double edited, out _, out _)
            && Math.Abs(edited - (.88 * g + .98 * q)) < .002,
            "La carga del escenario editado no sigue los factores del panel superior.");
        UnityData.UseBaseCaseFactors = false;
        GameObject objectToCheck = new GameObject("Elemento_72_viga_V60/80");
        try
        {
            ElementSelectable selected = objectToCheck.AddComponent<ElementSelectable>();
            foreach (ElementData element in data.elements)
                if (element.id == 72) { selected.data = element; break; }
            Require(selected.data != null, "Falta E1_72 en el catálogo de elementos.");
            selected.visualFloor = selected.data.piso;
            string sheet = StructuralAuditReport.Build(selected, .5f, 3).AsText();
            Require(sheet.Contains("Losa L96") && sheet.Contains("PASS aportes") &&
                sheet.Contains("PASS eleLoad G/Q") && sheet.Contains("PASS equilibrio I→J"),
                "La ficha no traza losa, cargas y equilibrio de E1_72.");
            Require(sheet.Contains("BALANCE GLOBAL") && sheet.Contains("PASS carga + reacciones"),
                "Falta el balance global exportado de C1.");
            foreach (ElementData element in data.elements)
                if (element.type == "columna") { selected.data = element; break; }
            Require(selected.data != null && selected.data.type == "columna" &&
                StructuralAuditReport.Build(selected, .5f, 4).AsText().Contains("OPENSEES → DIAGRAMA"),
                "La ficha de columna no muestra sus fuerzas locales.");
            selected.data = null;
            selected.isWall = true;
            selected.wallId = data.walls[0].id;
            selected.nodeIId = data.walls[0].nodeI;
            selected.nodeJId = data.walls[0].nodeJ;
            string wallSheet = StructuralAuditReport.Build(selected, .5f, 4).AsText();
            Require(wallSheet.Contains("RESULTADOS DEL MURO") &&
                wallSheet.Contains("No se construye un diagrama N/V/M de viga"),
                "La ficha de muro debe declarar los límites de sus resultados.");
        }
        finally { UnityEngine.Object.DestroyImmediate(objectToCheck); }
        Debug.Log("[ManualDiagramValidation][PASS] E1_72: cargas G/Q, C1, equilibrio I-J, factores editables y ficha de trazabilidad L96→viga→OpenSees.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[ManualDiagramValidation][FAIL] " + message);
    }
}

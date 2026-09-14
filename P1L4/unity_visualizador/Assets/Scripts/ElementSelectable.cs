using UnityEngine;

public class ElementSelectable : MonoBehaviour
{
    public ElementData data;
    public Vector3 startPoint;
    public Vector3 endPoint;
    public string customLabel;

    public string pmSectionId;

    public SupportData nodeISupport;
    public SupportData nodeJSupport;
    public int nodeIId;
    public int nodeJId;

    private Color originalColor;
    private bool hasOriginalColor;

    public void OnSelected()
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            if (!hasOriginalColor)
            {
                originalColor = renderer.material.color;
                hasOriginalColor = true;
            }
            renderer.material.color = Color.yellow;
        }
    }

    public void OnDeselected()
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null && renderer.material != null && hasOriginalColor)
        {
            renderer.material.color = originalColor;
        }
    }

    public string GetValuesAt(Vector3 hitPoint)
    {
        if (!string.IsNullOrEmpty(customLabel))
        {
            return customLabel;
        }

        if (data == null)
        {
            return "Elemento sin datos.";
        }

        Vector3 axis = endPoint - startPoint;
        float t = axis.sqrMagnitude > 0.0001f
            ? Mathf.Clamp01(Vector3.Dot(hitPoint - startPoint, axis) / axis.sqrMagnitude)
            : 0.5f;
        float length = axis.magnitude;
        float localS = t * length;

        string tag = !string.IsNullOrEmpty(data.elementTag) ? data.elementTag : data.id.ToString();
        string secId = !string.IsNullOrEmpty(data.sectionId) ? data.sectionId : data.seccion;
        string building = !string.IsNullOrEmpty(data.sourceBuilding) ? data.sourceBuilding : "?";

        float n, vy, vz, my, mz, torsion;
        GetForces(t, length, out n, out vy, out vz, out my, out mz, out torsion);

        string result =
            $"=== Elemento {tag} ({data.type}) ===\n" +
            $"ID Unity: {gameObject.name}\n" +
            $"elementTag OpenSees: {tag}\n" +
            $"Nodo I: {data.nodeI}  Nodo J: {data.nodeJ}\n" +
            $"Piso: {data.piso ?? "-"}\n" +
            $"Edificio: {building}\n" +
            $"\n--- Seccion y Material ---\n" +
            $"Seccion: {secId} ({data.width_m:0.00} x {data.height_m:0.00} m)\n";

        var mat = UnityData.GetMaterial(secId);
        if (mat != null)
        {
            result += $"Material: {mat.materialName}\n" +
                      $"fc' = {mat.fc_MPa:0.0} MPa | fy = {mat.fy_MPa:0.0} MPa\n";
            if (mat.steelBars > 0)
            {
                result += $"Acero: {mat.steelBars} barras phi {mat.barDiameter_mm:0.0} mm\n" +
                          $"Ast = {mat.Ast_mm2:0.0} mm2 | rho = {mat.rho_percent:0.###}%\n";
            }
        }

        result += $"\n--- Restricciones ---\n";
        result += FormatSupport("Nodo I", nodeISupport);
        result += FormatSupport("Nodo J", nodeJSupport);

        result += $"\n--- Ejes Locales ---\n";
        Vector3 localX = axis.normalized;
        result += $"X' (axial): {localX.x:0.000}, {localX.z:0.000}, {localX.y:0.000} (global)\n";

        result += $"\n--- Fuerzas en {t * 100f:0.0}% ({localS:0.00} m de {length:0.00} m) ---\n" +
                  $"N  = {n:0.###} kN\n" +
                  $"Vy = {vy:0.###} kN\n" +
                  $"Vz = {vz:0.###} kN\n" +
                  $"T  = {torsion:0.###} kN*m\n" +
                  $"My = {my:0.###} kN*m\n" +
                  $"Mz = {mz:0.###} kN*m\n";

        if (data.type == "viga" && data.areaTributaria > 0f)
        {
            result += $"\n--- Cargas Tributarias ---\n" +
                      $"Area: {data.areaTributaria:0.###} m2\n" +
                      $"D: {data.deadLoad:0.###} kN | L: {data.liveLoad:0.###} kN\n" +
                      $"U=1.4D: {data.factoredLoad14D:0.###} kN\n" +
                      $"U=1.2D+1.6L: {data.factoredLoad12D16L:0.###} kN\n";
        }

        if (UnityData.ActiveCombo != null)
        {
            result += $"\n--- Demanda (caso {UnityData.ActiveCombo}) ---\n";
            float pComp = -n;
            float mTotal = Mathf.Sqrt(my * my + mz * mz);
            result += $"P = {pComp:0.###} kN (compresion+)\n" +
                      $"M = {mTotal:0.###} kN*m (resultante)\n";

            if (!string.IsNullOrEmpty(pmSectionId))
            {
                result += $"Curva P-M: {pmSectionId}\n";
            }
        }

        result += $"\n--- Trazabilidad ---\n" +
                  $"OpenSees tag: {tag}\n" +
                  $"Unity obj: {gameObject.name}\n" +
                  "Resultado: " + (string.IsNullOrEmpty(UnityData.ActiveCombo) ? "G (sin combo)" : UnityData.ActiveCombo) + "\n" +
                  $"Seccion/Capacidad: {secId} -> {pmSectionId ?? "sin curva"}\n";

        return result;
    }

    private void GetForces(float t, float length, out float n, out float vy, out float vz, out float my, out float mz, out float torsion)
    {
        n = 0f; vy = 0f; vz = 0f; my = 0f; mz = 0f; torsion = 0f;

        if (!string.IsNullOrEmpty(UnityData.ActiveCombo) && UnityData.ElementForcesByCombo != null)
        {
            var forces = UnityData.GetElementForces(UnityData.ActiveCombo, data.id);
            if (forces != null && forces.Length >= 12)
            {
                float nI = forces[0], nJ = forces[6];
                float vyI = forces[1], vyJ = forces[7];
                float vzI = forces[2], vzJ = forces[8];
                float tI = forces[3], tJ = forces[9];
                float myI = forces[4], myJ = forces[10];
                float mzI = forces[5], mzJ = forces[11];

                n = Mathf.Lerp(nI, nJ, t);
                vy = Mathf.Lerp(vyI, vyJ, t);
                vz = Mathf.Lerp(vzI, vzJ, t);
                torsion = Mathf.Lerp(tI, tJ, t);
                my = Mathf.Lerp(myI, myJ, t);
                mz = Mathf.Lerp(mzI, mzJ, t);

                if (data.type == "viga" && Mathf.Abs(data.uniformLoad) > 1e-9f)
                {
                    mz += Mathf.Abs(data.uniformLoad) * length * length * t * (1f - t) / 2f;
                }
                return;
            }
        }

        n = Mathf.Lerp(data.axialI, data.axialJ, t);
        vz = Mathf.Lerp(data.shearI, data.shearJ, t);
        my = Mathf.Lerp(data.momentI, data.momentJ, t);
        if (data.type == "viga" && Mathf.Abs(data.uniformLoad) > 1e-9f)
        {
            my += Mathf.Abs(data.uniformLoad) * length * length * t * (1f - t) / 2f;
        }
    }

    public Vector3 GetDemandPoint()
    {
        if (data == null || string.IsNullOrEmpty(UnityData.ActiveCombo) || UnityData.ElementForcesByCombo == null)
        {
            return Vector3.zero;
        }

        float[] forces = UnityData.GetElementForces(UnityData.ActiveCombo, data.id);
        if (forces == null || forces.Length < 6)
        {
            return Vector3.zero;
        }

        float pComp = -forces[0];
        float mTotal = Mathf.Sqrt(forces[4] * forces[4] + forces[5] * forces[5]);
        return new Vector2(pComp, mTotal);
    }

    private string FormatSupport(string label, SupportData support)
    {
        if (support == null)
        {
            return $"{label}: sin apoyo registrado\n";
        }

        bool fixedAll = support.ux == 1 && support.uy == 1 && support.uz == 1;
        string type = !string.IsNullOrEmpty(support.type) ? support.type :
                      (fixedAll ? "Empotrado" : $"ux={support.ux} uy={support.uy} uz={support.uz}");
        return $"{label} (N{support.node}): {type}\n";
    }
}

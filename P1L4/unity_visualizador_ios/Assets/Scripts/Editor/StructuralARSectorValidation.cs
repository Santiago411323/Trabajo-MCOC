using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Integration checks using exported OpenSees data, without requiring a device anchor provider.</summary>
public static class StructuralARSectorValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static object Get(object obj, string name) => obj.GetType().GetField(name, Private | BindingFlags.Public).GetValue(obj);
    public static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private | BindingFlags.Public).SetValue(obj, value);
    public static void Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);
    private static void Require(bool ok, string message) { if (!ok) throw new Exception("Sector AR: " + message); }
    public static object CreateFixture(StructuralARController controller, ElementData element, float length, Vector3 center)
    {
        Call(controller, "DetachCurrentPlacement");
        Set(controller, "selectedElement", element);
        Set(controller, "placedLengthMeters", length);
        Set(controller, "memberLengthMeters", length);
        Set(controller, "calibratedPlacement", true);
        controller.uniformScale = 1f;
        controller.positionOffsetMeters = controller.rotationOffsetEuler = Vector3.zero;
        GameObject root = new GameObject("Validation placement");
        root.transform.position = center;
        Call(controller, "CreateElement", root.transform);
        Set(controller, "elementCreated", true);
        Call(controller, "RegisterCurrentPlacement", root);
        foreach (Transform child in root.transform) child.gameObject.SetActive(true);
        Call(controller, "UpdateDiagram");
        return Get(controller, "currentPlacement");
    }
    private static Vector3 Point(StructuralARController controller, int index)
    {
        LineRenderer line = (LineRenderer)Get(controller, "diagramBaseline");
        return line.transform.TransformPoint(line.GetPosition(index));
    }

    [MenuItem("MCOC/iPhone/Validar sector, ajuste y comparacion")]
    public static void Validate()
    {
        GameObject host = new GameObject("Sector AR validation");
        StructuralARController controller = host.AddComponent<StructuralARController>();
        try
        {
            controller.structureJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/estructura_p1l4_unity.json");
            StructureData data = JsonUtility.FromJson<StructureData>(controller.structureJson.text);
            Call(controller, "LoadStructure");
            ElementData beam = data.elements.First(element => element.elementTag == "E1_72");
            ElementData column = data.elements.First(element => element.elementTag == "E1_229");
            object a = CreateFixture(controller, beam, 7.51f, Vector3.zero);
            Call(controller, "SetCombination", "C2");
            Call(controller, "SetActiveResult", "Vy");
            Set(a, "Compare", true);
            Call(controller, "UpdateDiagram");
            Require(!((GameObject)Get(a, "Member")).GetComponent<Renderer>().enabled, "volumen opaco visible");
            Collider collider = ((GameObject)Get(a, "Member")).GetComponent<Collider>();
            Physics.SyncTransforms();
            Require(collider.Raycast(new Ray(collider.transform.position + collider.transform.forward * 5f, -collider.transform.forward), out _, 10f), "seleccion invisible sin collider");
            List<LineRenderer> lines = (List<LineRenderer>)Get(a, "Comparisons");
            Require(lines.Count == 3 && lines.All(line => line.enabled && line.positionCount == 41), "faltan curvas comparadas");
            Dictionary<string, Vector2> extrema = (Dictionary<string, Vector2>)Get(a, "Extrema");
            float largest = 0f;
            string governing = null;
            string[] cases = { "C1", "C2", "C3" };
            float[][] values = new float[3][];
            for (int c = 0; c < 3; c++)
            {
                values[c] = new float[41];
                for (int k = 0; k < 41; k++)
                {
                    Require(UnityData.TryGetSectionForces(beam.id, cases[c], k / 40f, out FrameSectionForces force), "faltan datos reales");
                    values[c][k] = force.Vy;
                }
                Require(Mathf.Abs(extrema[cases[c]].x - values[c].Min()) < .0001f &&
                    Mathf.Abs(extrema[cases[c]].y - values[c].Max()) < .0001f, "extremos distintos de OpenSees");
                float peak = values[c].Max(value => Mathf.Abs(value));
                if (governing == null || peak > largest) { governing = cases[c]; largest = peak; }
            }
            Require((string)Get(a, "GoverningCombination") == governing, "combinacion de mayor esfuerzo incorrecta");
            LineRenderer baseline = (LineRenderer)Get(a, "Baseline");
            Vector3 axis = baseline.GetPosition(1) - baseline.GetPosition(0);
            float height = Mathf.Max(controller.diagramHeightMeters, axis.magnitude * .12f);
            for (int c = 0; c < 3; c++) for (int k = 0; k < 41; k++)
            {
                float drawn = (lines[c].GetPosition(k) - baseline.GetPosition(0) - axis * (k / 40f)).magnitude;
                float expected = Mathf.Abs(values[c][k]) / Mathf.Max(largest, 1e-6f) * height;
                Require(Mathf.Abs(drawn - expected) < .0001f, "las curvas no comparten escala");
            }
            object b = CreateFixture(controller, column, 3f, Vector3.right * 10f);
            Call(controller, "SetCombination", "C3"); Call(controller, "SetActiveResult", "My");
            Require(((IList)Get(controller, "placements")).Count == 2, "agregar borra el anterior");
            Call(controller, "SelectPlacement", a);
            Require(controller.ActiveElementTag == beam.elementTag && controller.ActiveCombination == "C2" &&
                (string)Get(controller, "activeResult") == "Vy" && (bool)Get(a, "Compare"), "la seleccion mezcla estados");
            Vector3 i = Point(controller, 0), j = Point(controller, 1);
            Set(controller, "fineTarget", 2); Call(controller, "FineMove", new Vector3(.02f, 0f, .03f));
            Require((Point(controller, 0) - i).magnitude < .0001f &&
                (Point(controller, 1) - j - new Vector3(.02f, 0f, .03f)).magnitude < .0001f, "corregir J mueve I");
            j = Point(controller, 1);
            Set(controller, "fineTarget", 1); Call(controller, "FineMove", Vector3.up * .01f);
            Require((Point(controller, 1) - j).magnitude < .0001f, "corregir I mueve J");
            i = Point(controller, 0); j = Point(controller, 1);
            Set(controller, "fineTarget", 0); Call(controller, "FineMove", Vector3.up * .02f);
            Require((Point(controller, 0) - i - Vector3.up * .02f).magnitude < .0001f &&
                (Point(controller, 1) - j - Vector3.up * .02f).magnitude < .0001f, "traslacion cambia longitud");
            Vector3 midpoint = (Point(controller, 0) + Point(controller, 1)) * .5f;
            float length = Vector3.Distance(Point(controller, 0), Point(controller, 1));
            Call(controller, "FineRotate", 5f, false);
            Require(((Point(controller, 0) + Point(controller, 1)) * .5f - midpoint).magnitude < .0001f &&
                Mathf.Abs(Vector3.Distance(Point(controller, 0), Point(controller, 1)) - length) < .0001f, "giro cambia centro o longitud");
            Require(((GameObject)Get(b, "Root")).transform.position == Vector3.right * 10f, "ajuste mueve otro elemento");
            Require(controller.ActiveElementTag == beam.elementTag && controller.ActiveCombination == "C2", "ajuste cambia identidad o caso");
            Call(controller, "SelectPlacement", b);
            Require(controller.ActiveElementTag == column.elementTag && controller.ActiveCombination == "C3", "columna pierde estado propio");
            Call(controller, "StartNewElement"); Call(controller, "CancelGuidedPlacement");
            Require(((IList)Get(controller, "placements")).Count == 2, "cancelar borra el sector");
            Call(controller, "SelectPlacement", a); Call(controller, "RemoveSelectedPlacement");
            Require(((IList)Get(controller, "placements")).Count == 1 && controller.ActiveElementTag == column.elementTag, "quitar borra otros elementos");
            ValidateMomentOrientation(controller, data);
            Debug.Log("[Sector AR Validation] PASS: dos elementos independientes, I/J y ajuste fino, IDs/casos conservados, comparacion con escala comun y extremos reales, cancelar/quitar sin borrar sector.");
        }
        finally { Call(controller, "ReleaseOtherPlacements"); UnityEngine.Object.DestroyImmediate(host); }
    }
    public static void ValidateBatch()
    {
        try { Validate(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void ValidateMomentOrientation(StructuralARController controller, StructureData data)
    {
        GameObject cameraObject = new GameObject("Moment validation camera");
        controller.arCamera = cameraObject.AddComponent<Camera>();
        try
        {
            foreach (string tag in new[] { "E1_72", "E1_94" })
            {
                ElementData beam = data.elements.First(e => e.elementTag == tag);
                object placement = CreateFixture(controller, beam, 4f, Vector3.zero);
                GameObject root = (GameObject)Get(placement, "Root");
                Vector3 axis = Point(controller, 1) - Point(controller, 0);
                root.transform.rotation = Quaternion.AngleAxis(120f, axis.normalized);
                foreach (string component in new[] { "My", "Mz" })
                foreach (bool compare in new[] { false, true })
                foreach (string combo in new[] { "C1", "C2", "C3" })
                foreach (float cameraSide in new[] { -1f, 1f })
                {
                    cameraObject.transform.position = new Vector3(3, 2, cameraSide * 10);
                    Set(placement, "Compare", compare);
                    Call(controller, "SetCombination", combo);
                    Call(controller, "SetActiveResult", component);
                    LineRenderer baseline = (LineRenderer)Get(placement, "Baseline");
                    var curves = compare ? (List<LineRenderer>)Get(placement, "Comparisons") :
                        new List<LineRenderer> { (LineRenderer)Get(placement, "Diagram") };
                    float denominator = 0f;
                    foreach (string c in compare ? new[] { "C1", "C2", "C3" } : new[] { combo })
                    for (int k = 0; k < 41; k++)
                    {
                        Require(UnityData.TryGetSectionForces(beam.id, c, k / 40f, out FrameSectionForces force), "faltan momentos");
                        denominator = Mathf.Max(denominator, Mathf.Abs(component == "My" ? force.My : force.Mz));
                    }
                    float height = Mathf.Max(controller.diagramHeightMeters, 4f * .12f);
                    for (int c = 0; c < curves.Count; c++) for (int k = 0; k < 41; k++)
                    {
                        UnityData.TryGetSectionForces(beam.id, compare ? new[] { "C1", "C2", "C3" }[c] : combo, k / 40f, out FrameSectionForces force);
                        float value = component == "My" ? force.My : force.Mz;
                        Vector3 basePoint = Vector3.Lerp(baseline.GetPosition(0), baseline.GetPosition(1), k / 40f);
                        Vector3 worldOffset = root.transform.TransformVector(curves[c].GetPosition(k) - basePoint);
                        Vector3 expected = Vector3.down * (value / Mathf.Max(denominator, 1e-6f) * height);
                        Require((worldOffset - expected).magnitude < .0001f,
                            tag + " " + component + ": momento positivo no apunta abajo o cambia con la camara");
                    }
                    if (compare)
                    {
                        var ranges = (Dictionary<string, Vector2>)Get(placement, "Extrema");
                        foreach (string c in new[] { "C1", "C2", "C3" })
                        {
                            float[] samples = Enumerable.Range(0, 41).Select(k => {
                                UnityData.TryGetSectionForces(beam.id, c, k / 40f, out FrameSectionForces force);
                                return component == "My" ? force.My : force.Mz;
                            }).ToArray();
                            Require(Mathf.Abs(ranges[c].x - samples.Min()) < .0001f && Mathf.Abs(ranges[c].y - samples.Max()) < .0001f,
                                "la orientacion modifica los signos numericos");
                        }
                    }
                }
            }
            Debug.Log("[Moment AR Validation] PASS: E1_72/E1_94 My/Mz positivos abajo, negativos arriba; C1/C2/C3 individual y superpuesto; independiente de camara/cara; valores intactos.");
        }
        finally { controller.arCamera = null; UnityEngine.Object.DestroyImmediate(cameraObject); }
    }
}

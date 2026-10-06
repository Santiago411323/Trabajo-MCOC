using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public sealed partial class StructuralARController
{
    private sealed class PlacementRecord
    {
        public int Number;
        public ElementData Element;
        public ARAnchor Anchor;
        public GameObject Root, Member;
        public LineRenderer Diagram, Baseline;
        public Material DiagramMaterial, BaselineMaterial;
        public float Length, ModelLength, Scale, Minimum, Maximum;
        public Vector3 Offset, Rotation;
        public bool Ready, HasResults, Approximate, Compare;
        public string Combination = "C1", Component = "My", GoverningCombination;
        public readonly List<LineRenderer> Comparisons = new List<LineRenderer>();
        public readonly List<Material> ComparisonMaterials = new List<Material>();
        public readonly Dictionary<string, Vector2> Extrema = new Dictionary<string, Vector2>();
    }

    private readonly List<PlacementRecord> placements = new List<PlacementRecord>();
    private PlacementRecord currentPlacement;
    private int nextPlacementNumber = 1, placementRevision;
    private int fineTarget; // 0 whole member, 1 I, 2 J
    private float fineStepMeters = 0.02f;
    private Vector2 sectorScroll;
    private string lockedTab = "Resultados";

    private void SaveCurrentPlacement()
    {
        if (currentPlacement == null) return;
        PlacementRecord item = currentPlacement;
        item.Element = selectedElement;
        item.Anchor = structuralAnchor;
        item.Member = elementObject;
        item.Diagram = diagramLine;
        item.Baseline = diagramBaseline;
        item.DiagramMaterial = diagramMaterial;
        item.BaselineMaterial = baselineMaterial;
        item.Length = placedLengthMeters;
        item.ModelLength = memberLengthMeters;
        item.Scale = uniformScale;
        item.Offset = positionOffsetMeters;
        item.Rotation = rotationOffsetEuler;
        item.Ready = elementCreated;
        item.HasResults = resultsAvailable;
        item.Approximate = approximatePlacement;
        item.Combination = activeCombo;
        item.Component = activeResult;
        item.Minimum = diagramMinimum;
        item.Maximum = diagramMaximum;
    }

    private void DetachCurrentPlacement()
    {
        SaveCurrentPlacement();
        currentPlacement = null;
        structuralAnchor = null;
        elementObject = null;
        diagramLine = diagramBaseline = null;
        diagramMaterial = baselineMaterial = null;
        elementCreated = elementSelected = resultsAvailable = anchorRequested = false;
    }

    private void RegisterCurrentPlacement(GameObject root)
    {
        currentPlacement = new PlacementRecord { Number = nextPlacementNumber++, Root = root };
        placements.Add(currentPlacement);
        SaveCurrentPlacement();
        root.name = "AR #" + currentPlacement.Number + " " + selectedElement.elementTag;
    }

    private void SelectPlacement(PlacementRecord item)
    {
        if (item == null || placementInProgress) return;
        SaveCurrentPlacement();
        currentPlacement = item;
        selectedElement = item.Element;
        structuralAnchor = item.Anchor;
        elementObject = item.Member;
        diagramLine = item.Diagram;
        diagramBaseline = item.Baseline;
        diagramMaterial = item.DiagramMaterial;
        baselineMaterial = item.BaselineMaterial;
        placedLengthMeters = item.Length;
        memberLengthMeters = item.ModelLength;
        uniformScale = item.Scale;
        positionOffsetMeters = item.Offset;
        rotationOffsetEuler = item.Rotation;
        elementCreated = item.Ready;
        resultsAvailable = item.HasResults;
        approximatePlacement = item.Approximate;
        activeCombo = item.Combination;
        activeResult = item.Component;
        diagramMinimum = item.Minimum;
        diagramMaximum = item.Maximum;
        pointDetected = poseObtained = calibratedPlacement = elementSelected = true;
        anchorRequested = item.Anchor != null;
        calibrationStage = CalibrationStage.Locked;
        elementSearch = item.Element.elementTag;
        selectedType = item.Element.type;
        UnityData.ActiveCombo = activeCombo;
        panelScroll = Vector2.zero;
        menuExpanded = true;
        UpdateDiagram();
        SetStatus("Seleccionado #" + item.Number + " " + item.Element.elementTag + ".");
    }

    private void StartNewElement()
    {
        if (placementInProgress) return;
        placementRevision++;
        DetachCurrentPlacement();
        ClearCalibrationVisuals();
        selectedElement = null;
        calibrationStage = CalibrationStage.Idle;
        pointDetected = poseObtained = calibratedPlacement = false;
        panelScroll = Vector2.zero;
        menuExpanded = true;
        SetStatus("Elige el elemento que vas a agregar al sector.");
    }

    private void ChooseElement()
    {
        ElementData match = FindElement(elementSearch);
        if (match == null) { SetStatus("Elige un ID valido."); return; }
        selectedElement = match;
        elementSearch = match.elementTag;
        calibrationStage = CalibrationStage.FindI;
        panelScroll = Vector2.zero;
        menuExpanded = false;
        SetStatus("Apunta al nodo I de " + match.elementTag + " y ajusta la profundidad.");
    }

    private void CancelGuidedPlacement()
    {
        placementRevision++;
        DetachCurrentPlacement();
        selectedElement = null;
        calibrationStage = CalibrationStage.Idle;
        if (placements.Count > 0) SelectPlacement(placements[placements.Count - 1]);
    }

    private void UpdateOtherPlacements()
    {
        foreach (PlacementRecord item in placements)
        {
            if (item == currentPlacement || item.Anchor == null || item.Root == null) continue;
            bool tracking = item.Anchor.trackingState == TrackingState.Tracking;
            item.Root.SetActive(tracking);
            if (item.Member != null) item.Member.SetActive(tracking);
            if (item.Diagram != null) item.Diagram.gameObject.SetActive(tracking);
            if (item.Baseline != null) item.Baseline.gameObject.SetActive(tracking);
        }
    }

    private bool EditablePlacement => currentPlacement != null && currentPlacement.Root != null &&
        structuralAnchor != null && structuralAnchor.trackingState == TrackingState.Tracking;

    private Vector3 PlacedPoint(int index) => diagramBaseline.transform.TransformPoint(diagramBaseline.GetPosition(index));

    private void FineMove(Vector3 worldDelta)
    {
        if (currentPlacement == null || diagramBaseline == null) return;
        Transform root = currentPlacement.Root.transform;
        if (fineTarget == 0) root.position += worldDelta;
        else
        {
            Vector3 i = PlacedPoint(0), j = PlacedPoint(1);
            Vector3 oldAxis = j - i;
            if (fineTarget == 1) i += worldDelta; else j += worldDelta;
            if ((j - i).magnitude < 0.05f) { SetStatus("El tramo debe medir al menos 5 cm."); return; }
            root.position = (i + j) * .5f;
            root.rotation = Quaternion.FromToRotation(oldAxis, j - i) * root.rotation;
            placedLengthMeters = (j - i).magnitude;
        }
        approximatePlacement = true;
        ApplyElementTransform();
        SaveCurrentPlacement();
        SetStatus("Ajuste visual de #" + currentPlacement.Number + ". Los resultados OpenSees se conservan.");
    }

    private void FineRotate(float degrees, bool roll)
    {
        if (currentPlacement == null || diagramBaseline == null) return;
        Vector3 axis = roll ? (PlacedPoint(1) - PlacedPoint(0)).normalized : Vector3.up;
        currentPlacement.Root.transform.Rotate(axis, degrees, Space.World);
        approximatePlacement = true;
        UpdateDiagram();
        SaveCurrentPlacement();
    }

    private void RemoveSelectedPlacement()
    {
        if (currentPlacement == null || placementInProgress) return;
        PlacementRecord item = currentPlacement;
        placements.Remove(item);
        DetachCurrentPlacement();
        DisposePlacement(item);
        if (placements.Count > 0) SelectPlacement(placements[placements.Count - 1]);
        else StartNewElement();
    }

    private void DisposePlacement(PlacementRecord item)
    {
        ReleasePlacementObject(item.Anchor != null ? item.Anchor.gameObject : item.Root);
        ReleasePlacementObject(item.DiagramMaterial);
        ReleasePlacementObject(item.BaselineMaterial);
        foreach (Material material in item.ComparisonMaterials) ReleasePlacementObject(material);
    }

    private static void ReleasePlacementObject(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }

    private void ReleaseOtherPlacements()
    {
        foreach (PlacementRecord item in placements) DisposePlacement(item);
        placements.Clear();
        currentPlacement = null;
        diagramMaterial = baselineMaterial = null;
    }

    private static readonly string[] ComparisonCases = { "C1", "C2", "C3" };
    private static Color CaseColor(int index) => index == 0 ? new Color(1f, .78f, .16f) :
        index == 1 ? new Color(.25f, .95f, .55f) : new Color(.95f, .30f, .90f);

    private void UpdateComparisonDiagrams()
    {
        PlacementRecord item = currentPlacement;
        if (item == null || diagramBaseline == null || diagramLine == null) return;
        if (!item.Compare || MobileSeismicPlayback.IsActive)
        {
            foreach (LineRenderer line in item.Comparisons) if (line != null) line.enabled = false;
            return;
        }
        int count = Mathf.Clamp(diagramSamples, 11, 101);
        float[][] values = new float[3][];
        bool[] valid = new bool[3];
        float largest = 0f;
        item.Extrema.Clear();
        item.GoverningCombination = null;
        for (int c = 0; c < 3; c++)
        {
            values[c] = new float[count];
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            valid[c] = true;
            for (int k = 0; k < count; k++)
            {
                if (!UnityData.TryGetSectionForces(item.Element.id, ComparisonCases[c], k / (float)(count - 1), out FrameSectionForces force))
                { valid[c] = false; break; }
                values[c][k] = ComponentValue(force, activeResult);
                minimum = Mathf.Min(minimum, values[c][k]); maximum = Mathf.Max(maximum, values[c][k]);
            }
            if (!valid[c]) continue;
            item.Extrema[ComparisonCases[c]] = new Vector2(minimum, maximum);
            float peak = Mathf.Max(Mathf.Abs(minimum), Mathf.Abs(maximum));
            if (item.GoverningCombination == null || peak > largest)
            { largest = peak; item.GoverningCombination = ComparisonCases[c]; }
        }
        while (item.Comparisons.Count < 3)
        {
            int c = item.Comparisons.Count;
            Material material = new Material(diagramMaterial) { color = CaseColor(c) };
            GameObject child = new GameObject("AR " + ComparisonCases[c] + " " + item.Element.elementTag);
            child.transform.SetParent(item.Root.transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            ConfigureLine(line, material, 0.014f);
            item.Comparisons.Add(line); item.ComparisonMaterials.Add(material);
        }
        Vector3 start = diagramBaseline.GetPosition(0), axis = diagramBaseline.GetPosition(1) - start;
        Vector3 side = PositiveDiagramDirection(item.Root.transform, axis);
        float height = Mathf.Max(diagramHeightMeters, axis.magnitude * .12f);
        for (int c = 0; c < 3; c++)
        {
            LineRenderer line = item.Comparisons[c];
            line.enabled = valid[c];
            line.positionCount = count;
            line.startColor = line.endColor = CaseColor(c);
            if (!valid[c]) continue;
            for (int k = 0; k < count; k++)
                line.SetPosition(k, start + axis * (k / (float)(count - 1)) + side * (values[c][k] / Mathf.Max(largest, 1e-6f) * height));
        }
        diagramLine.enabled = false;
        diagramBaseline.enabled = item.Extrema.Count > 0;
    }
}

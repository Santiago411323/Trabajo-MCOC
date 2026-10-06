using UnityEngine;

public sealed partial class StructuralARController
{
    private void DrawGuidedInterface()
    {
        if(lockedTab!="Ingeniería" || calibrationStage!=CalibrationStage.Locked || !menuExpanded)engineering.Preview?.Hide();
        uiScale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 390f);
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
        float width = Screen.width / uiScale, height = Screen.height / uiScale;
        if (calibrationStage == CalibrationStage.FindI || calibrationStage == CalibrationStage.FindJ || calibrationStage == CalibrationStage.ChooseFace)
            GUI.Label(new Rect(width * .5f - 18f, height * .5f - 18f, 36f, 36f), "+",
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 25 });
        foreach (PlacementRecord item in placements)
        {
            if (arCamera == null || item.Root == null || !item.Root.activeInHierarchy) continue;
            Vector3 screen = arCamera.WorldToScreenPoint(item.Root.transform.position);
            if (screen.z <= 0f) continue;
            GUIStyle label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            label.normal.textColor = item == currentPlacement ? Color.cyan : Color.white;
            GUI.Label(new Rect(screen.x / uiScale - 95f, (Screen.height - screen.y) / uiScale - 28f, 190f, 24f),
                "#" + item.Number + " " + item.Element.elementTag, label);
        }
        Rect pixels = Screen.safeArea;
        Rect safe = new Rect(pixels.x / uiScale, (Screen.height - pixels.yMax) / uiScale, pixels.width / uiScale, pixels.height / uiScale);
        panelRect.width = Mathf.Min(430f, safe.width - 16f);
        panelRect.height = menuExpanded ? Mathf.Min(360f, safe.height * .46f) : Mathf.Min(192f, safe.height * .32f);
        panelRect.x = safe.x + (safe.width - panelRect.width) * .5f;
        panelRect.y = safe.yMax - panelRect.height - 8f;
        GUI.Box(panelRect, GUIContent.none);
        GUILayout.BeginArea(new Rect(panelRect.x + 10f, panelRect.y + 6f, panelRect.width - 20f, panelRect.height - 12f));
        GUILayout.BeginHorizontal();
        GUILayout.Label("AR v3 | " + StepTitle(), HeaderStyle());
        if (GUILayout.Button(menuExpanded ? "Contraer" : "Menu", GUILayout.Height(30f), GUILayout.Width(85f)))
            menuExpanded = !menuExpanded;
        GUILayout.EndHorizontal();
        panelScroll = GUILayout.BeginScrollView(panelScroll, false, true);
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && !placementInProgress;
        DrawCurrentStep();
        GUI.enabled = enabled;
        if (placementInProgress) GUILayout.Label("Creando anchor...", WrapStyle());
        GUILayout.Label(status, WrapStyle());
        GUILayout.EndScrollView();
        GUI.enabled = enabled && !placementInProgress;
        DrawPrimaryStepAction();
        GUI.enabled = enabled;
        GUILayout.EndArea();
        GUI.matrix = previousMatrix;
    }

    private string StepTitle()
    {
        switch (calibrationStage)
        {
            case CalibrationStage.FindI: return "2/5 Marcar I";
            case CalibrationStage.FindJ: return "3/5 Marcar J";
            case CalibrationStage.ChooseFace: return "4/5 Ajustar cara";
            case CalibrationStage.ConfirmAnchor: return "5/5 Anclar";
            case CalibrationStage.Locked: return "Sector: " + placements.Count + " elementos";
            default: return "1/5 Elegir elemento";
        }
    }

    private void DrawCurrentStep()
    {
        if (calibrationStage == CalibrationStage.Idle) { DrawElementChoice(); return; }
        if (selectedElement != null)
            GUILayout.Label((currentPlacement == null ? "" : "#" + currentPlacement.Number + " ") +
                selectedElement.elementTag + " | " + selectedElement.type + " | ID " + selectedElement.id, HeaderStyle());
        switch (calibrationStage)
        {
            case CalibrationStage.FindI:
                GUILayout.Label("Apunta al extremo I. La esfera celeste indica el punto que vas a fijar.", WrapStyle());
                DrawPlacementDepth();
                if (GUILayout.Button("Volver a elegir elemento")) { calibrationStage = CalibrationStage.Idle; menuExpanded = true; }
                break;
            case CalibrationStage.FindJ:
                GUILayout.Label("I esta conservado. Apunta al extremo J.", WrapStyle());
                DrawPlacementDepth();
                if (GUILayout.Button("Corregir I")) { ClearCalibrationVisuals(); calibrationStage = CalibrationStage.FindI; }
                break;
            case CalibrationStage.ChooseFace:
                GUILayout.Label("Alinea la flecha magenta con la cara de la viga o columna.", WrapStyle());
                if (CameraAction("Capturar cara con la mira")) CaptureFace();
                if (GUILayout.Button("Girar cara 90 grados", GUILayout.Height(32f))) RotateFace();
                if (GUILayout.Button("Corregir J (conservar I)")) AdjustJAgain();
                break;
            case CalibrationStage.ConfirmAnchor:
                DrawLengths();
                GUILayout.Label("Se conservara este elementTag y se agregara al sector junto a los anteriores.", WrapStyle());
                if (GUILayout.Button("Volver a ajustar cara")) calibrationStage = CalibrationStage.ChooseFace;
                break;
            case CalibrationStage.Locked:
                if (MobileSeismicPlayback.IsActive) { DrawMobileSeismicResult(); break; }
                DrawCombinationButtons();
                if (menuExpanded)
                {
                    GUILayout.BeginHorizontal();
                    foreach (string tab in new[] { "Resultados", "Ajuste", "Sector", "Ingeniería" })
                        if (GUILayout.Button(tab, GUILayout.Height(32f))) { lockedTab = tab; panelScroll = Vector2.zero; }
                    GUILayout.EndHorizontal();
                    if (lockedTab == "Ajuste") DrawFineControls();
                    else if (lockedTab == "Sector") DrawSector();
                    else if(lockedTab=="Ingeniería") DrawEngineering();
                    else DrawComparisonResults();
                }
                break;
        }
        if (calibrationStage != CalibrationStage.Idle && calibrationStage != CalibrationStage.Locked &&
            GUILayout.Button("Cancelar esta colocacion")) ResetCalibration();
    }

    // Keep the next action visible even when instructions need scrolling.
    private void DrawPrimaryStepAction()
    {
        switch (calibrationStage)
        {
            case CalibrationStage.FindI:
                if (CameraAction("FIJAR NODO I")) { BeginCalibration(); panelScroll = Vector2.zero; }
                break;
            case CalibrationStage.FindJ:
                if (CameraAction("FIJAR NODO J")) { CaptureJ(); panelScroll = Vector2.zero; }
                break;
            case CalibrationStage.ChooseFace:
                if (GUILayout.Button("Cara lista → Revisar anclaje", GUILayout.Height(34f)))
                { calibrationStage = CalibrationStage.ConfirmAnchor; panelScroll = Vector2.zero; }
                break;
            case CalibrationStage.ConfirmAnchor:
                if (CameraAction("ANCLAR ELEMENTO")) ConfirmCalibration();
                break;
            case CalibrationStage.Locked:
                if (GUILayout.Button("+ Agregar otro elemento", GUILayout.Height(34f))) StartNewElement();
                break;
        }
    }

    private bool CameraAction(string title)
    {
        bool enabled = GUI.enabled;
        GUI.enabled = enabled;
        bool clicked = GUILayout.Button(title, GUILayout.Height(34f));
        GUI.enabled = enabled;
        return clicked;
    }

    private void DrawElementChoice()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Vigas", GUILayout.Height(32f)) && selectedType != "viga") { selectedType = "viga"; elementSearch = ""; }
        if (GUILayout.Button("Columnas", GUILayout.Height(32f)) && selectedType != "columna") { selectedType = "columna"; elementSearch = ""; }
        GUILayout.EndHorizontal();
        GUILayout.Label("elementTag o ID interno:");
        elementSearch = GUILayout.TextField(elementSearch, GUILayout.Height(32f));
        selectionScroll = GUILayout.BeginScrollView(selectionScroll, GUILayout.Height(85f));
        int shown = 0;
        foreach (ElementData item in availableElements)
        {
            if (item.type != selectedType) continue;
            if (elementSearch.Length > 0 && item.elementTag.IndexOf(elementSearch, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                item.id.ToString().IndexOf(elementSearch, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (GUILayout.Button(item.elementTag + " | ID " + item.id + " | " + item.piso, GUILayout.Height(30f))) elementSearch = item.elementTag;
            if (++shown >= 20) break;
        }
        GUILayout.EndScrollView();
        ElementData match = FindElement(elementSearch);
        if (match != null) GUILayout.Label(match.seccion + " | " + match.piso, WrapStyle());
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && match != null;
        if (GUILayout.Button("Usar elemento → Marcar I", GUILayout.Height(34f))) ChooseElement();
        GUI.enabled = enabled;
        if (placements.Count > 0)
        {
            GUILayout.Label("Los elementos ya colocados se conservan.", WrapStyle());
            if (GUILayout.Button("Volver al sector")) CancelGuidedPlacement();
        }
    }

    private void DrawPlacementDepth()
    {
        GUILayout.Label("Profundidad: " + fallbackDistanceMeters.ToString("0.00") + " m");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("−", GUILayout.Width(40f), GUILayout.Height(32f))) fallbackDistanceMeters = Mathf.Max(.3f, fallbackDistanceMeters - .25f);
        fallbackDistanceMeters = GUILayout.HorizontalSlider(fallbackDistanceMeters, .3f, 20f);
        if (GUILayout.Button("+", GUILayout.Width(40f), GUILayout.Height(32f))) fallbackDistanceMeters = Mathf.Min(20f, fallbackDistanceMeters + .25f);
        GUILayout.EndHorizontal();
    }

    private void DrawCombinationButtons()
    {
        GUILayout.BeginHorizontal();
        Color previous = GUI.backgroundColor;
        for (int c = 0; c < 3; c++)
        {
            GUI.backgroundColor = activeCombo == ComparisonCases[c] ? CaseColor(c) : previous;
            if (GUILayout.Button(ComparisonCases[c], GUILayout.Height(32f))) SetCombination(ComparisonCases[c]);
        }
        GUI.backgroundColor = previous;
        GUILayout.EndHorizontal();
    }

    private void DrawLengths()
    {
        GUILayout.Label("I-J visual: " + placedLengthMeters.ToString("0.00") + " m | Modelo: " + memberLengthMeters.ToString("0.00") + " m", WrapStyle());
    }

    private void DrawComparisonResults()
    {
        DrawLengths();
        GUILayout.BeginHorizontal();
        foreach (string component in new[] { "N", "Vy", "Vz", "My", "Mz" })
            if (GUILayout.Button(component, GUILayout.Height(32f))) SetActiveResult(component);
        GUILayout.EndHorizontal();
        if (currentPlacement == null) return;
        bool compare = GUILayout.Toggle(currentPlacement.Compare, "Superponer C1 / C2 / C3 (misma escala)", GUILayout.Height(32f));
        if (compare != currentPlacement.Compare) { currentPlacement.Compare = compare; UpdateDiagram(); }
        string unit = activeResult.StartsWith("M") ? "kN·m" : "kN";
        if (compare)
        {
            for (int c = 0; c < 3; c++)
            {
                GUIStyle legend = WrapStyle(); legend.normal.textColor = CaseColor(c);
                if (currentPlacement.Extrema.TryGetValue(ComparisonCases[c], out Vector2 range))
                    GUILayout.Label($"{ComparisonCases[c]}: min {range.x:0.000} / max {range.y:0.000} {unit}", legend);
                else GUILayout.Label(ComparisonCases[c] + ": sin datos", legend);
            }
            GUILayout.Label("Mayor |" + activeResult + "| entre las muestras: " +
                (currentPlacement.GoverningCombination ?? "sin datos"), WrapStyle());
        }
        else GUILayout.Label($"{activeCombo} · {activeResult}: min {diagramMinimum:0.000} / max {diagramMaximum:0.000} {unit}", WrapStyle());
        if (TryReadForces(activeCombo, out FrameSectionForces f))
            GUILayout.Label($"Seccion media {activeCombo}\nN {f.N:0.000} kN  |  Vy {f.Vy:0.000} kN  |  Vz {f.Vz:0.000} kN\nMy {f.My:0.000} kN·m  |  Mz {f.Mz:0.000} kN·m", WrapStyle());
        GUILayout.Label("Resultados precalculados. La altura del dibujo se normaliza; el ajuste visual no recalcula OpenSees.", WrapStyle());
    }

    private void DrawFineControls()
    {
        DrawLengths();
        GUILayout.Label("Ajustar: elemento completo, nodo I o nodo J.", WrapStyle());
        fineTarget = GUILayout.Toolbar(fineTarget, new[] { "Todo", "Nodo I", "Nodo J" }, GUILayout.Height(32f));
        GUILayout.BeginHorizontal();
        foreach (float step in new[] { .01f, .02f, .05f })
            if (GUILayout.Button((step * 100f).ToString("0") + " cm", GUILayout.Height(30f))) fineStepMeters = step;
        GUILayout.EndHorizontal();
        GUILayout.Label("Paso: " + (fineStepMeters * 100f).ToString("0") + " cm. Direcciones segun la camara.", WrapStyle());
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && EditablePlacement;
        Vector3 right = arCamera != null ? Vector3.ProjectOnPlane(arCamera.transform.right, Vector3.up).normalized : Vector3.right;
        Vector3 forward = arCamera != null ? Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up).normalized : Vector3.forward;
        if (right.sqrMagnitude < .5f) right = Vector3.right;
        if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
        DrawMovePair("Izquierda", "Derecha", right);
        DrawMovePair("Bajar", "Subir", Vector3.up);
        DrawMovePair("Acercar", "Alejar", forward);
        GUILayout.Label("Rotacion del elemento completo:", WrapStyle());
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Giro −5°", GUILayout.Height(32f))) FineRotate(-5f, false);
        if (GUILayout.Button("Giro +5°", GUILayout.Height(32f))) FineRotate(5f, false);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Cara −5°", GUILayout.Height(32f))) FineRotate(-5f, true);
        if (GUILayout.Button("Cara +5°", GUILayout.Height(32f))) FineRotate(5f, true);
        GUILayout.EndHorizontal();
        GUI.enabled = enabled;
        if (!EditablePlacement) GUILayout.Label("Para ajustar, recupera el seguimiento del anchor.", WrapStyle());
        GUILayout.Label("Se conserva el anchor y el ID. Los nodos del modelo OpenSees no se modifican.", WrapStyle());
    }

    private void DrawMovePair(string minus, string plus, Vector3 axis)
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(minus, GUILayout.Height(32f))) FineMove(-axis * fineStepMeters);
        if (GUILayout.Button(plus, GUILayout.Height(32f))) FineMove(axis * fineStepMeters);
        GUILayout.EndHorizontal();
    }

    private void DrawSector()
    {
        GUILayout.Label("Toca un elemento en la vista AR o selecciona su numero aqui.", WrapStyle());
        sectorScroll = GUILayout.BeginScrollView(sectorScroll, GUILayout.Height(100f));
        foreach (PlacementRecord item in placements)
            if (GUILayout.Button("#" + item.Number + " " + item.Element.elementTag + " | " + item.Element.type + " | " + item.Combination, GUILayout.Height(32f)))
            { SelectPlacement(item); break; }
        GUILayout.EndScrollView();
        if (currentPlacement != null && GUILayout.Button("Quitar solo el elemento seleccionado", GUILayout.Height(32f))) RemoveSelectedPlacement();
        GUILayout.Label("El sector se mantiene durante esta sesion; no se guarda al cerrar la app.", WrapStyle());
    }
}

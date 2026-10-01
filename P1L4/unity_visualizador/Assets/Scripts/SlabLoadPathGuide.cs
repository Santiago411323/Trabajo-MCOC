using System.Collections.Generic;
using UnityEngine;

// Presenta los aportes tributarios exportados y la conectividad del modelo.
// Las rutas bajo las vigas son topológicas: no asignan fuerzas ficticias a columnas.
public sealed class SlabLoadPathGuide
{
    private struct Edge
    {
        public int from, to, id;
        public bool rigid;
        public bool beam;
    }

    private readonly Dictionary<int, NodeData> nodes = new Dictionary<int, NodeData>();
    private readonly Dictionary<int, ElementData> members = new Dictionary<int, ElementData>();
    private readonly Dictionary<int, List<Edge>> graph = new Dictionary<int, List<Edge>>();
    private readonly List<ElementData> receivers = new List<ElementData>();
    private readonly HashSet<int> receiverIds = new HashSet<int>();
    private readonly HashSet<int> intermediateBeams = new HashSet<int>();
    private readonly HashSet<int> firstVerticals = new HashSet<int>();
    private readonly HashSet<int> pathVerticals = new HashSet<int>();
    private readonly HashSet<int> supportNodes = new HashSet<int>();
    private readonly Dictionary<int, Vector3> supportMarkerPositions = new Dictionary<int, Vector3>();
    private SlabSelectable selected;
    private GameObject overlay;
    private Material lineMaterial;
    private Material supportMaterial;
    private int step;

    public void Select(SlabSelectable slab)
    {
        if (selected == slab) return;
        ClearOverlay();
        selected = slab;
        step = 0;
        nodes.Clear(); members.Clear(); graph.Clear(); receivers.Clear(); receiverIds.Clear();
        intermediateBeams.Clear();
        firstVerticals.Clear(); pathVerticals.Clear(); supportNodes.Clear();
        if (slab == null || slab.structure == null) return;
        StructureData data = slab.structure;
        foreach (NodeData node in data.nodes ?? new NodeData[0])
            if (node != null) nodes[node.id] = node;
        foreach (ElementData member in data.elements ?? new ElementData[0])
        {
            if (member == null) continue;
            members[member.id] = member;
            if (!nodes.TryGetValue(member.nodeI, out NodeData ni) ||
                !nodes.TryGetValue(member.nodeJ, out NodeData nj)) continue;
            bool rigid = member.type == "brazo_rigido";
            bool vertical = (member.type == "columna" || member.type == "muro_eq") &&
                Mathf.Abs(ni.z - nj.z) > .05f;
            bool beam = member.type == "viga" && Mathf.Abs(ni.z - nj.z) <= .05f;
            if (!rigid && !vertical && !beam) continue;
            AddEdge(member.nodeI, member.nodeJ, member.id, rigid, beam);
            AddEdge(member.nodeJ, member.nodeI, member.id, rigid, beam);
        }
        var seen = new HashSet<int>();
        if (slab.metadata != null)
            foreach (SlabReceiverMetadata receiver in slab.metadata.receivers ?? new SlabReceiverMetadata[0])
                if (receiver != null && seen.Add(receiver.beam) &&
                    members.TryGetValue(receiver.beam, out ElementData beam))
                { receivers.Add(beam); receiverIds.Add(beam.id); }
        FindVerticalConnections();
        TraceSupports();
    }

    public void Clear()
    {
        ClearOverlay();
        if (lineMaterial != null) Object.Destroy(lineMaterial);
        if (supportMaterial != null) Object.Destroy(supportMaterial);
        lineMaterial = null;
        supportMaterial = null;
        selected = null;
    }

    public void Draw(Rect rect, GUIStyle titleStyle, GUIStyle labelStyle)
    {
        if (selected == null) return;
        GUI.Box(rect, GUIContent.none);
        string[] names = { "1/4  LOSA", "2/4  VIGAS RECEPTORAS", "3/4  ELEMENTOS VERTICALES", "4/4  APOYOS" };
        GUI.Label(new Rect(rect.x + 10f, rect.y + 5f, rect.width - 20f, 20f),
            "RUTA DE CARGA  ·  " + names[step], titleStyle);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 29f, rect.width - 20f, 82f), StepText(), labelStyle);
        if (GUI.Button(new Rect(rect.x + 10f, rect.yMax - 31f, 105f, 24f), "ANTERIOR") && step > 0)
            SetStep(step - 1);
        if (GUI.Button(new Rect(rect.x + 121f, rect.yMax - 31f, 105f, 24f), "SIGUIENTE") && step < 3)
            SetStep(step + 1);
        GUI.Label(new Rect(rect.x + 235f, rect.yMax - 28f, rect.width - 245f, 20f),
            "Cian: vigas  ·  Ámbar: conexión vertical  ·  Verde: apoyo", labelStyle);
        if (step == 3) DrawSupportCallouts(rect);
    }

    private void DrawSupportCallouts(Rect panel)
    {
        if (Camera.main == null) return;
        int shown = 0;
        foreach (KeyValuePair<int, Vector3> marker in supportMarkerPositions)
        {
            if (shown++ >= 8) break;
            Vector3 screen = Camera.main.WorldToScreenPoint(marker.Value);
            float x = screen.x;
            float y = Screen.height - screen.y;
            if (screen.z <= 0f || x < 290f || x > panel.x - 105f ||
                y < 238f || y > Screen.height - 27f) continue;
            Color old = GUI.color;
            GUI.color = new Color(.28f, 1f, .52f);
            GUI.Box(new Rect(x - 45f, y - 12f, 90f, 24f), "APOYO N" + marker.Key);
            GUI.color = old;
        }
    }

    private string StepText()
    {
        SlabLoadMetadata metadata = selected.metadata;
        if (metadata == null) return "Esta losa no tiene ficha tributaria exportada; no se dibuja una ruta supuesta.";
        if (step == 0)
            return $"Losa {selected.slab.id} · {selected.slab.nivel} · área {metadata.area:0.###} m²\n" +
                $"G = {metadata.totalG:0.###} kN  ·  Q = {metadata.totalQ:0.###} kN. " +
                "La losa transfiere carga; no es un elemento shell.";
        if (step == 1)
        {
            float area = 0f, g = 0f, q = 0f;
            foreach (SlabReceiverMetadata receiver in metadata.receivers ?? new SlabReceiverMetadata[0])
            { area += receiver.area; g += receiver.G; q += receiver.Q; }
            float error = Mathf.Abs(area - metadata.area);
            bool pass = error <= Mathf.Max(.0001f, metadata.area * .0001f) &&
                Mathf.Abs(g - metadata.totalG) <= .01f && Mathf.Abs(q - metadata.totalQ) <= .01f;
            return $"{receivers.Count} vigas receptoras · Σ área {area:0.###} m² ({(pass ? "PASS" : "REVISAR")}; Δ {error:0.####} m²)\n" +
                $"Aporte exportado: G {g:0.###} kN  ·  Q {q:0.###} kN. " +
                "Cada viga y su aporte están en el inspector inferior.";
        }
        if (step == 2)
            return $"{firstVerticals.Count} segmentos verticales conectados a extremos de las vigas " +
                "(también mediante brazos rígidos).\n" +
                "Conectividad, sin asignar G/Q por columna o muro. " + Labels(firstVerticals, false);
        return $"{supportNodes.Count} apoyos alcanzados; {intermediateBeams.Count} vigas intermedias. " +
            Labels(supportNodes, true) + "\n" +
            "Marcador elevado con línea al nodo real; no representa una reacción calculada.";
    }

    private string Labels(HashSet<int> ids, bool nodeLabels)
    {
        var labels = new List<string>();
        foreach (int id in ids)
        {
            labels.Add(nodeLabels ? "N" + id : members.TryGetValue(id, out ElementData member)
                ? member.elementTag : id.ToString());
            if (labels.Count == 3) break;
        }
        return labels.Count == 0 ? "Sin ruta directa en el modelo." : string.Join(", ", labels.ToArray()) +
            (ids.Count > labels.Count ? "…" : "");
    }

    private void AddEdge(int from, int to, int id, bool rigid, bool beam)
    {
        if (!graph.TryGetValue(from, out List<Edge> edges))
            graph[from] = edges = new List<Edge>();
        edges.Add(new Edge { from = from, to = to, id = id, rigid = rigid, beam = beam });
    }

    private void FindVerticalConnections()
    {
        foreach (ElementData beam in receivers)
            foreach (int end in new[] { beam.nodeI, beam.nodeJ })
            {
                var nearby = new HashSet<int> { end };
                if (graph.TryGetValue(end, out List<Edge> edges))
                    foreach (Edge edge in edges) if (edge.rigid) nearby.Add(edge.to);
                foreach (int node in nearby)
                {
                    if (!graph.TryGetValue(node, out List<Edge> adjacent)) continue;
                    foreach (Edge edge in adjacent)
                        if (!edge.rigid && !edge.beam && IsDownward(edge)) firstVerticals.Add(edge.id);
                }
            }
    }

    private void TraceSupports()
    {
        foreach (ElementData beam in receivers)
            foreach (int start in new[] { beam.nodeI, beam.nodeJ })
            {
                long startKey = (long)start * 2;
                var visited = new HashSet<long> { startKey };
                var previous = new Dictionary<long, Edge>();
                var queue = new Queue<long>();
                queue.Enqueue(startKey);
                long found = -1;
                while (queue.Count > 0 && visited.Count < 512)
                {
                    long currentKey = queue.Dequeue();
                    int current = (int)(currentKey / 2);
                    int extraBeams = (int)(currentKey % 2);
                    if (UnityData.GetNodeSupport(current) != null) { found = currentKey; break; }
                    if (!graph.TryGetValue(current, out List<Edge> edges)) continue;
                    foreach (Edge edge in edges)
                    {
                        int nextExtra = extraBeams + (edge.beam && !receiverIds.Contains(edge.id) ? 1 : 0);
                        long nextKey = (long)edge.to * 2 + nextExtra;
                        if (nextExtra > 1 || !IsDownward(edge) || !visited.Add(nextKey)) continue;
                        previous[nextKey] = edge;
                        queue.Enqueue(nextKey);
                    }
                }
                if (found < 0) continue;
                supportNodes.Add((int)(found / 2));
                while (found != startKey && previous.TryGetValue(found, out Edge edge))
                {
                    if (edge.beam && !receiverIds.Contains(edge.id)) intermediateBeams.Add(edge.id);
                    else if (!edge.rigid && !edge.beam) pathVerticals.Add(edge.id);
                    int precedingExtra = (int)(found % 2) - (edge.beam && !receiverIds.Contains(edge.id) ? 1 : 0);
                    found = (long)edge.from * 2 + precedingExtra;
                }
            }
    }

    private bool IsDownward(Edge edge)
    {
        return nodes.TryGetValue(edge.from, out NodeData a) &&
            nodes.TryGetValue(edge.to, out NodeData b) && b.z <= a.z + .05f;
    }

    private void SetStep(int next)
    {
        step = Mathf.Clamp(next, 0, 3);
        ClearOverlay();
        if (step == 0 || selected == null) return;
        overlay = new GameObject("Recorrido de carga " + selected.slab.id);
        overlay.hideFlags = HideFlags.DontSave;
        if (lineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            lineMaterial = new Material(shader);
        }
        Color beamColor = step == 1 ? new Color(.12f, .95f, 1f) : new Color(.12f, .75f, .87f, .48f);
        foreach (ElementData beam in receivers) DrawMember(beam, beamColor, .17f, true);
        if (step == 3)
            foreach (int id in intermediateBeams)
                if (members.TryGetValue(id, out ElementData beam))
                    DrawMember(beam, new Color(.12f, .95f, 1f), .13f, true);
        if (step >= 2)
            foreach (int id in step == 2 ? firstVerticals : pathVerticals)
                if (members.TryGetValue(id, out ElementData member))
                    DrawMember(member, new Color(1f, .69f, .18f), .35f, false);
        if (step == 3)
            foreach (int id in supportNodes)
                if (nodes.TryGetValue(id, out NodeData node)) DrawSupport(node);
    }

    private void DrawMember(ElementData member, Color color, float width, bool beam)
    {
        if (!nodes.TryGetValue(member.nodeI, out NodeData a) ||
            !nodes.TryGetValue(member.nodeJ, out NodeData b)) return;
        var lineObject = new GameObject(member.elementTag ?? member.id.ToString());
        lineObject.transform.SetParent(overlay.transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.sharedMaterial = lineMaterial;
        line.useWorldSpace = true;
        line.positionCount = 2;
        Vector3 offset = Vector3.up * .46f;
        if (!beam)
        {
            Vector3 middle = new Vector3((a.x + b.x) * .5f, (a.z + b.z) * .5f,
                (a.y + b.y) * .5f);
            Vector3 towardCamera = Camera.main != null
                ? Camera.main.transform.position - middle : new Vector3(1f, 0f, 1f);
            towardCamera.y = 0f;
            offset = towardCamera.sqrMagnitude > .001f
                ? towardCamera.normalized * .7f : new Vector3(.5f, 0f, .5f);
        }
        line.SetPosition(0, new Vector3(a.x, a.z, a.y) + offset);
        line.SetPosition(1, new Vector3(b.x, b.z, b.y) + offset);
        line.startWidth = line.endWidth = width;
        line.startColor = line.endColor = color;
        line.numCapVertices = 4;
    }

    private void DrawSupport(NodeData node)
    {
        Vector3 truePosition = new Vector3(node.x, node.z, node.y);
        Vector3 towardCamera = Camera.main != null
            ? Camera.main.transform.position - truePosition : new Vector3(1f, 0f, 1f);
        towardCamera.y = 0f;
        Vector3 markerPosition = new Vector3(node.x, Mathf.Max(node.z + .9f, 1.2f), node.y) +
            (towardCamera.sqrMagnitude > .001f ? towardCamera.normalized * 1.5f : Vector3.right * 1.5f);

        var tether = new GameObject("Referencia apoyo N" + node.id);
        tether.transform.SetParent(overlay.transform, false);
        LineRenderer pointer = tether.AddComponent<LineRenderer>();
        pointer.sharedMaterial = lineMaterial;
        pointer.useWorldSpace = true;
        pointer.positionCount = 2;
        pointer.SetPosition(0, truePosition + Vector3.up * .1f);
        pointer.SetPosition(1, markerPosition);
        pointer.startWidth = pointer.endWidth = .08f;
        pointer.startColor = pointer.endColor = new Color(.25f, 1f, .55f);

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Apoyo N" + node.id;
        marker.transform.SetParent(overlay.transform, false);
        marker.transform.position = markerPosition;
        supportMarkerPositions[node.id] = markerPosition;
        marker.transform.localScale = Vector3.one * .7f;
        Collider collider = marker.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);
        if (supportMaterial == null)
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            supportMaterial = new Material(shader) { color = new Color(.25f, 1f, .55f) };
        }
        marker.GetComponent<Renderer>().sharedMaterial = supportMaterial;
    }

    private void ClearOverlay()
    {
        if (overlay != null) Object.Destroy(overlay);
        overlay = null;
        supportMarkerPositions.Clear();
    }
}

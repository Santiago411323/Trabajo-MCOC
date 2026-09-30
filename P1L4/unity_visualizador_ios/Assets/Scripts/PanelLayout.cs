using System.Collections.Generic;
using UnityEngine;

public static class PanelLayout
{
    private static readonly Dictionary<string, Rect> positions = new Dictionary<string, Rect>();
    private static string draggingId;
    private static Vector2 dragOffset;

    public static Rect Get(string id, Rect defaultRect)
    {
        Rect saved;
        return positions.TryGetValue(id, out saved) ? saved : defaultRect;
    }

    public static Rect Apply(string id, Rect defaultRect, float headerHeight = 26f)
    {
        Rect rect = Get(id, defaultRect);
        Event current = Event.current;
        Rect header = new Rect(rect.x, rect.y, rect.width, headerHeight);

        if (current.type == EventType.MouseDown && current.button == 0 && header.Contains(current.mousePosition))
        {
            draggingId = id;
            dragOffset = current.mousePosition - rect.position;
            current.Use();
        }
        else if (current.type == EventType.MouseDrag && current.button == 0 && draggingId == id)
        {
            rect.position = current.mousePosition - dragOffset;
            positions[id] = rect;
            current.Use();
        }
        else if (current.type == EventType.MouseUp && current.button == 0 && draggingId == id)
        {
            draggingId = null;
            current.Use();
        }

        positions[id] = rect;
        return rect;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The framed box an item sits in. Its frame is drawn here rather than by UI Toolkit, whose
/// border-radius antialiases a one-pixel border into a smudge. Here each corner is cut by one
/// pixel, crisply: the sides stop short of it and the corner is left empty.
/// The skin still owns the frame, as it does <see cref="AoButton"/>'s: the colours come from
/// the properties below, and the line's thickness per side from the ordinary border-width, which
/// also insets the contents. Leave border-color transparent.
/// </summary>
public class AoSlotFrame : VisualElement
{
    static readonly CustomStyleProperty<Color> FillProperty = new("--ao-slot-fill");
    static readonly CustomStyleProperty<Color> LineProperty = new("--ao-slot-line");

    Color _fill = Color.clear;
    Color _line = Color.clear;

    public AoSlotFrame()
    {
        RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        generateVisualContent += DrawFrame;
    }

    void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
    {
        ICustomStyle custom = evt.customStyle;
        _fill = custom.TryGetValue(FillProperty, out Color fill) ? fill : Color.clear;
        _line = custom.TryGetValue(LineProperty, out Color line) ? line : Color.clear;
        MarkDirtyRepaint();
    }

    void DrawFrame(MeshGenerationContext mgc)
    {
        int w = Mathf.RoundToInt(layout.width);
        int h = Mathf.RoundToInt(layout.height);
        int left = Mathf.RoundToInt(resolvedStyle.borderLeftWidth);
        int right = Mathf.RoundToInt(resolvedStyle.borderRightWidth);
        int top = Mathf.RoundToInt(resolvedStyle.borderTopWidth);
        int bottom = Mathf.RoundToInt(resolvedStyle.borderBottomWidth);
        int x1 = w - right;
        int y1 = h - bottom;
        if (x1 - left < 2 || y1 - top < 2)
            return;

        var rects = new List<(int x0, int y0, int x1, int y1, Color32 c)>();
        void Rect(int ax, int ay, int bx, int by, Color c)
        {
            if (bx > ax && by > ay && c.a > 0f)
                rects.Add((ax, ay, bx, by, c));
        }

        Rect(left, top, x1, y1, _fill);

        // The four sides stop short of the corners.
        Rect(left, 0, x1, top, _line);
        Rect(left, y1, x1, h, _line);
        Rect(0, top, left, y1, _line);
        Rect(x1, top, w, y1, _line);

        if (rects.Count == 0)
            return;

        MeshWriteData mesh = mgc.Allocate(rects.Count * 4, rects.Count * 6);
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            ushort b = (ushort)(i * 4);
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.x0, r.y0, Vertex.nearZ), tint = r.c });
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.x1, r.y0, Vertex.nearZ), tint = r.c });
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.x1, r.y1, Vertex.nearZ), tint = r.c });
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.x0, r.y1, Vertex.nearZ), tint = r.c });

            mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 1)); mesh.SetNextIndex((ushort)(b + 2));
            mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 2)); mesh.SetNextIndex((ushort)(b + 3));
        }
    }
}

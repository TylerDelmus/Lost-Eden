using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A 5x3 pixel triangle, pointing down, drawn as three solid rows (5, 3 and 1 pixels wide)
/// in the element's text <c>color</c>. The skin colours it through <c>color</c>, the same way
/// it colours text. Class <c>ao-caret</c>; <c>ao-caret--up</c> flips it.
///
/// It is geometry rather than a glyph because the pixel fonts are latin1 and hold no
/// triangle. It is whole rows rather than a painted path because a path is antialiased.
/// </summary>
public class AoCaret : VisualElement
{
    const int Width = 5;
    const int Height = 3;

    public AoCaret()
    {
        AddToClassList("ao-caret");
        pickingMode = PickingMode.Ignore;
        style.width = Width;
        style.height = Height;
        style.flexShrink = 0f;
        generateVisualContent += Draw;
    }

    public bool PointsUp
    {
        get => ClassListContains("ao-caret--up");
        set
        {
            EnableInClassList("ao-caret--up", value);
            MarkDirtyRepaint();
        }
    }

    void Draw(MeshGenerationContext mgc)
    {
        Color32 color = resolvedStyle.color;
        MeshWriteData mesh = mgc.Allocate(Height * 4, Height * 6);
        bool up = PointsUp;

        for (int row = 0; row < Height; row++)
        {
            int inset = up ? Height - 1 - row : row;   // 0, 1, 2 pixels in from each side
            float x0 = inset, x1 = Width - inset, y0 = row, y1 = row + 1;
            ushort b = (ushort)(row * 4);

            mesh.SetNextVertex(new Vertex { position = new Vector3(x0, y0, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x1, y0, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x1, y1, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x0, y1, Vertex.nearZ), tint = color });

            mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 1)); mesh.SetNextIndex((ushort)(b + 2));
            mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 2)); mesh.SetNextIndex((ushort)(b + 3));
        }
    }
}

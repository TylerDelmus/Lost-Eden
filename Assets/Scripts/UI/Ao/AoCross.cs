using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A 7x7 pixel X — the close glyph of a window's header button — drawn as whole pixels in the
/// element's text <c>color</c>, as <see cref="AoCaret"/> draws its triangle. Class <c>ao-cross</c>.
///
/// It is geometry rather than a glyph for the same reason as the caret: a letter X in the pixel
/// font does not sit centred in a 13px button, and a painted path is antialiased.
/// </summary>
public class AoCross : VisualElement
{
    const int Size = 7;

    public AoCross()
    {
        AddToClassList("ao-cross");
        pickingMode = PickingMode.Ignore;
        style.width = Size;
        style.height = Size;
        style.flexShrink = 0f;
        generateVisualContent += Draw;
    }

    void Draw(MeshGenerationContext mgc)
    {
        Color32 color = resolvedStyle.color;

        // One pixel per row on each diagonal; the centre row's two meet in one.
        int quads = Size * 2 - 1;
        MeshWriteData mesh = mgc.Allocate(quads * 4, quads * 6);
        int n = 0;

        for (int row = 0; row < Size; row++)
        {
            Pixel(mesh, ref n, row, row, color);
            if (Size - 1 - row != row)
                Pixel(mesh, ref n, Size - 1 - row, row, color);
        }
    }

    static void Pixel(MeshWriteData mesh, ref int n, int x, int y, Color32 color)
    {
        ushort b = (ushort)(n * 4);
        mesh.SetNextVertex(new Vertex { position = new Vector3(x, y, Vertex.nearZ), tint = color });
        mesh.SetNextVertex(new Vertex { position = new Vector3(x + 1, y, Vertex.nearZ), tint = color });
        mesh.SetNextVertex(new Vertex { position = new Vector3(x + 1, y + 1, Vertex.nearZ), tint = color });
        mesh.SetNextVertex(new Vertex { position = new Vector3(x, y + 1, Vertex.nearZ), tint = color });
        mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 1)); mesh.SetNextIndex((ushort)(b + 2));
        mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 2)); mesh.SetNextIndex((ushort)(b + 3));
        n++;
    }
}

using System;

/// <summary>
/// How DisplaySystem's <c>GfxVisualCord4</c> draws its link list (render <c>1000f072</c>, geometry
/// <c>1000f040</c>): the links, newest first, are taken in consecutive pairs; each pair gets a
/// sideways vector from the pair's on-screen direction (<c>1000ee68</c>), and every link but the last
/// (oldest) one gets two vertices, P - s and P + s, where s is the average of the side vectors of the
/// two pairs that meet there (the first link: its own pair's) times the link's size
/// (<c>1000e3e8</c>). The vertices form one triangle strip, u 0 on the minus side and 1 on the plus
/// side, v = 1 - life / lifeScale when the visual was built with its fourth flag, else 0.25.
///
/// Side vector: both link positions go to camera space (x right, y up, z forward, as D3D); x and y
/// are divided by z unless |z| &lt; 0.0001; d = b - a (z undivided) is scaled to length 1, and the side
/// is right * d.y - up * d.x, right and up being the camera axes in the visual's frame. When d has
/// no x or y the side is the camera's up axis as it is.
/// </summary>
public static class Cord4Strip
{
    const float DepthEpsilon = 0.0001f;

    /// <summary>
    /// Builds the strip for <paramref name="count"/> links given newest first. <paramref name="local"/>
    /// holds link positions in the visual's frame, <paramref name="camera"/> the same points in camera
    /// space. Writes positions (visual frame) and D3D (tu, tv); returns the vertex count, 2 * (count - 1).
    /// </summary>
    public static int Build(
        int count,
        float[] local,
        float[] camera,
        float rx, float ry, float rz,
        float ux, float uy, float uz,
        float[] size,
        float[] life,
        float lifeScale,
        bool lifeV,
        float[] outPositions,
        float[] outUvs)
    {
        if (count < 3)
            return 0;

        // Side vectors of the count - 1 pairs.
        var side = new float[(count - 1) * 3];
        for (int p = 0; p < count - 1; p++)
            PairSide(camera, p, p + 1, rx, ry, rz, ux, uy, uz, side, p);

        int v = 0;
        // First link: its own pair's side.
        Emit(local, 0, side[0] * size[0], side[1] * size[0], side[2] * size[0], VCoord(life, 0, lifeScale, lifeV),
            outPositions, outUvs, ref v);

        // Links 1 .. count - 2: the average of the pairs on either side.
        for (int i = 1; i < count - 1; i++)
        {
            float ax = (side[(i - 1) * 3] + side[i * 3]) * 0.5f;
            float ay = (side[(i - 1) * 3 + 1] + side[i * 3 + 1]) * 0.5f;
            float az = (side[(i - 1) * 3 + 2] + side[i * 3 + 2]) * 0.5f;
            Emit(local, i, ax * size[i], ay * size[i], az * size[i], VCoord(life, i, lifeScale, lifeV),
                outPositions, outUvs, ref v);
        }

        return v;
    }

    static float VCoord(float[] life, int i, float lifeScale, bool lifeV)
        => lifeV ? 1f - life[i] / lifeScale : 0.25f;

    static void Emit(float[] local, int i, float sx, float sy, float sz, float tv, float[] pos, float[] uv, ref int v)
    {
        float px = local[i * 3], py = local[i * 3 + 1], pz = local[i * 3 + 2];
        Set(pos, v, px - sx, py - sy, pz - sz);
        uv[v * 2] = 0f;
        uv[v * 2 + 1] = tv;
        v++;
        Set(pos, v, px + sx, py + sy, pz + sz);
        uv[v * 2] = 1f;
        uv[v * 2 + 1] = tv;
        v++;
    }

    static void PairSide(
        float[] cam, int a, int b,
        float rx, float ry, float rz,
        float ux, float uy, float uz,
        float[] side, int pair)
    {
        float ax = cam[a * 3], ay = cam[a * 3 + 1], az = cam[a * 3 + 2];
        float bx = cam[b * 3], by = cam[b * 3 + 1], bz = cam[b * 3 + 2];
        if (az <= -DepthEpsilon || az >= DepthEpsilon)
        {
            ax /= az;
            ay /= az;
        }
        if (bz <= -DepthEpsilon || bz >= DepthEpsilon)
        {
            bx /= bz;
            by /= bz;
        }

        float dx = bx - ax, dy = by - ay, dz = bz - az;
        float sx, sy, sz;
        if (dx == 0f && dy == 0f)
        {
            sx = ux; sy = uy; sz = uz;
        }
        else
        {
            float k = 1f / (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx *= k;
            dy *= k;
            sx = rx * dy - ux * dx;
            sy = ry * dy - uy * dx;
            sz = rz * dy - uz * dx;
        }

        side[pair * 3] = sx;
        side[pair * 3 + 1] = sy;
        side[pair * 3 + 2] = sz;
    }

    static void Set(float[] p, int i, float x, float y, float z)
    {
        p[i * 3] = x;
        p[i * 3 + 1] = y;
        p[i * 3 + 2] = z;
    }
}

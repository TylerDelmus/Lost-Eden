using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// ABIFF diffuse textures packed into texture arrays, so materials that differ only in their base
/// texture can be one material: the mesh carries the slice index (UV1.x) and the AOLit shader graph
/// samples the array with it. Unlike an atlas, every slice keeps its own mips and repeat wrapping, which
/// AO's tiling UVs need.
///
/// <para>
/// Two arrays, by size class: textures up to 256 on their longer side are resampled to 256x256, larger
/// ones to 512x512. The source textures are GPU-only (decoded with markNonReadable), so slices are
/// filled with a GPU blit, not a CPU copy. Slots are append-only -- a texture keeps its slice for the
/// life of the factory, because merged meshes bake the index into their vertices.
/// </para>
/// </summary>
public sealed class AbiffTextureArrays
{
    public const int SmallSize = 256;
    public const int LargeSize = 512;
    const int InitialCapacity = 32;

    static readonly int BaseColorArrayId = Shader.PropertyToID("_BaseColorArray");

    sealed class ArraySet
    {
        public int Size;
        public RenderTexture Array;
        public int Count;
        public readonly List<Material> Users = new List<Material>();
    }

    readonly ArraySet[] _sets =
    {
        new ArraySet { Size = SmallSize },
        new ArraySet { Size = LargeSize },
    };

    readonly Dictionary<int, (int set, int slice)> _slices = new Dictionary<int, (int set, int slice)>();

    public int SetCount => _sets.Length;

    public bool TryGetSlice(int textureId, out int set, out int slice)
    {
        if (_slices.TryGetValue(textureId, out (int set, int slice) entry))
        {
            set = entry.set;
            slice = entry.slice;
            return true;
        }

        set = -1;
        slice = -1;
        return false;
    }

    /// <summary>
    /// Gives every texture in <paramref name="textureIds"/> a slice, growing the arrays at most once each
    /// and regenerating their mips once. Call before building meshes that reference the slices.
    /// </summary>
    public void Add(IEnumerable<int> textureIds, System.Func<int, Texture2D> load)
    {
        var pending = new List<(int id, Texture2D texture, int set)>();
        foreach (int id in textureIds)
        {
            if (id <= 0 || _slices.ContainsKey(id))
                continue;

            Texture2D texture = load(id);
            if (texture == null)
                continue;

            int set = Mathf.Max(texture.width, texture.height) <= SmallSize ? 0 : 1;
            _slices[id] = (set, _sets[set].Count + CountPending(pending, set));
            pending.Add((id, texture, set));
        }

        if (pending.Count == 0)
            return;

        for (int s = 0; s < _sets.Length; s++)
        {
            int added = CountPending(pending, s);
            if (added > 0)
                EnsureCapacity(_sets[s], _sets[s].Count + added);
        }

        foreach (var entry in pending)
            Graphics.Blit(entry.texture, _sets[entry.set].Array, 0, _slices[entry.id].slice);

        for (int s = 0; s < _sets.Length; s++)
        {
            int added = CountPending(pending, s);
            if (added == 0)
                continue;

            _sets[s].Count += added;
            _sets[s].Array.GenerateMips();
        }
    }

    /// <summary>Points <paramref name="material"/> at the array for <paramref name="set"/>, now and after any growth.</summary>
    public void Bind(Material material, int set)
    {
        ArraySet arraySet = _sets[set];
        arraySet.Users.Add(material);
        if (arraySet.Array != null)
            material.SetTexture(BaseColorArrayId, arraySet.Array);
    }

    static int CountPending(List<(int id, Texture2D texture, int set)> pending, int set)
    {
        int n = 0;
        foreach (var entry in pending)
        {
            if (entry.set == set)
                n++;
        }
        return n;
    }

    /// <summary>
    /// Reallocates with room for <paramref name="needed"/> slices, doubling, copying the existing slices
    /// and every mip across on the GPU and rebinding the materials that use it.
    /// </summary>
    static void EnsureCapacity(ArraySet set, int needed)
    {
        int capacity = set.Array != null ? set.Array.volumeDepth : 0;
        if (needed <= capacity)
            return;

        int newCapacity = Mathf.Max(InitialCapacity, capacity);
        while (newCapacity < needed)
            newCapacity *= 2;

        var descriptor = new RenderTextureDescriptor(set.Size, set.Size, GraphicsFormat.R8G8B8A8_SRGB, 0)
        {
            dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
            volumeDepth = newCapacity,
            useMipMap = true,
            autoGenerateMips = false,
            msaaSamples = 1,
        };

        var array = new RenderTexture(descriptor)
        {
            name = $"AbiffBaseColorArray_{set.Size}",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
        };
        array.Create();

        if (set.Array != null)
        {
            int mips = set.Array.mipmapCount;
            for (int slice = 0; slice < set.Count; slice++)
            {
                for (int mip = 0; mip < mips; mip++)
                    Graphics.CopyTexture(set.Array, slice, mip, array, slice, mip);
            }

            set.Array.Release();
            if (Application.isPlaying)
                Object.Destroy(set.Array);
            else
                Object.DestroyImmediate(set.Array);
        }

        set.Array = array;
        foreach (Material user in set.Users)
        {
            if (user != null)
                user.SetTexture(BaseColorArrayId, array);
        }
    }
}

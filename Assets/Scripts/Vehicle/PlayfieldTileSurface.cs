using N3Lite;
using N3Lite.Surfaces;
using AODB.Common.RDBObjects;
using N3Lite.Surfaces;
using UnityEngine;

namespace LostEden.Vehicles
{
    /// <summary>
    /// Builds a <see cref="TilemapSurface"/> from the playfield's AODB <see cref="Tilemap"/>, in the
    /// same world frame the rendered terrain uses.
    ///
    /// <para>
    /// <b>The frames line up by construction.</b> <c>TerrainChunkBuilder.Build</c> places a sample at
    /// <c>((chunkX * (chunkSize - 1) + localX) * mapScale, sample * heightMod, …)</c>, and
    /// <see cref="ChunkedTileHeightSource"/> resolves the same global sample index by
    /// <c>x >> shift</c> / <c>x &amp; mask</c> with <c>step = chunkSize - 1</c>. So a surface built
    /// here answers in the terrain root's local space — the caller converts if the root is not at
    /// the origin.
    /// </para>
    ///
    /// <para>
    /// <b>Outdoor only.</b> Indoor tilemaps (<c>GNDA</c>/<c>UOHA</c>, <c>Tilemap.IsIndoor</c>) store
    /// their floor heights differently and stock samples them through a different path
    /// (<c>10016454</c>, unported) — see <c>N3Lite/docs/Movement.md</c> §5.2a.
    /// </para>
    /// </summary>
    public static class PlayfieldTileSurface
    {
        /// <summary>
        /// Attempts to build a surface. Returns false with a reason rather than throwing, because a
        /// playfield with no usable heightmap is a normal case (indoor maps, empty records).
        /// </summary>
        public static bool TryCreate(Tilemap tilemap, out TilemapSurface surface, out string reason)
        {
            surface = null;

            if (tilemap == null)
            {
                reason = "no tilemap";
                return false;
            }

            if (tilemap.IsIndoor)
            {
                reason = "indoor tilemap; the indoor height path is not ported";
                return false;
            }

            if (tilemap.Heightmap == null || tilemap.Heightmap.Count == 0)
            {
                reason = "tilemap has no heightmap chunks";
                return false;
            }

            if (tilemap.ChunkSize <= 1 || tilemap.GridWidth <= 0)
            {
                reason = $"unusable chunk shape (size {tilemap.ChunkSize}, grid {tilemap.GridWidth})";
                return false;
            }

            if (tilemap.MapScale <= 0f)
            {
                reason = $"map scale is {tilemap.MapScale}";
                return false;
            }

            ChunkedTileHeightSource source;
            try
            {
                source = new ChunkedTileHeightSource(
                    tilemap.Heightmap.ToArray(),
                    tilemap.GridWidth,
                    tilemap.ChunkSize,
                    tilemap.HeightMod,
                    (int)tilemap.MapWidth,
                    (int)tilemap.MapHeight,
                    tilemap.MapScale);
            }
            catch (System.ArgumentException ex)
            {
                reason = ex.Message;
                return false;
            }

            surface = new TilemapSurface(source);
            reason = null;
            return true;
        }

        /// <summary>
        /// Convenience wrapper that logs the reason when a surface cannot be built, so a character
        /// silently falling through the world is traceable.
        /// </summary>
        public static TilemapSurface CreateOrWarn(Tilemap tilemap, int playfieldId)
        {
            if (TryCreate(tilemap, out TilemapSurface surface, out string reason))
                return surface;

            Debug.LogWarning($"[Vehicle] No collision surface for playfield {playfieldId}: {reason}");
            return null;
        }

        /// <summary>
        /// Loads the playfield's tilemap and builds its surface.
        ///
        /// <para>
        /// Uses the <b>same record id <c>TerrainParser</c> uses</b> — the playfield id itself — so
        /// the collision geometry and the rendered mesh can never come from different tilemaps.
        /// </para>
        /// </summary>
        public static TilemapSurface CreateForPlayfield(ResourceDatabase database, int playfieldId)
        {
            if (database == null)
            {
                Debug.LogWarning($"[Vehicle] No collision surface for playfield {playfieldId}: no resource database");
                return null;
            }

            Tilemap tilemap;
            try
            {
                tilemap = database.Get<Tilemap>(playfieldId);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Vehicle] No collision surface for playfield {playfieldId}: {ex.Message}");
                return null;
            }

            TilemapSurface surface = CreateOrWarn(tilemap, playfieldId);

            // The heightmap alone is terrain-only. Stock reaches statels through the same Surface_i by
            // hanging a CellSurface off the tilemap surface (N3 1001879d) -- see N3Lite/docs/Movement.md §8.
            // The grid is created empty here; SurfaceCellLoader streams cells into it.
            if (surface != null)
                PlayfieldCellSurface.AttachEmptyGrid(surface, tilemap);

            return surface;
        }
    }
}

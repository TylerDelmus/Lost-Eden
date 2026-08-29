using System;
using AODB.Common.RDBObjects;
using UnityEngine;

public static class PlayfieldLayoutFactory
{
    const int PlayfieldRecordType = 1000001;
    const int ZoneSizeOffset = 0x38;
    const int NumZonesOffset = 0x3C;

    public static bool TryCreate(ResourceDatabase database, int playfieldId, out IPlayfieldCellLayout layout)
    {
        layout = null;
        if (database == null || !database.IsInitialized)
        {
            Debug.LogError("[PlayfieldLayout] ResourceDatabase is not initialized.");
            return false;
        }

        RDBPlayfield playfield;
        try
        {
            playfield = database.Get<RDBPlayfield>(playfieldId);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayfieldLayout] Failed to load RDBPlayfield {playfieldId}: {ex.Message}");
            return false;
        }

        if (playfield == null)
        {
            Debug.LogError($"[PlayfieldLayout] RDBPlayfield {playfieldId} not found.");
            return false;
        }

        if (playfield.IsIndoor)
        {
            Debug.Log($"[PlayfieldLayout] Playfield {playfieldId} is indoor — cell locality stubbed until rooms are implemented.");
            layout = new IndoorCellLayout(playfieldId);
            return true;
        }

        if (!TryReadOutdoorGrid(database, playfieldId, playfield, out int zoneSize, out int numZones))
            return false;

        int tilemapId = playfield.TilemapId != 0 ? playfield.TilemapId : playfieldId;
        Tilemap tilemap;
        try
        {
            tilemap = database.Get<Tilemap>(tilemapId);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayfieldLayout] Failed to load Tilemap {tilemapId}: {ex.Message}");
            return false;
        }

        if (tilemap == null)
        {
            Debug.LogError($"[PlayfieldLayout] Tilemap {tilemapId} not found.");
            return false;
        }

        int spanW = Math.Max(1, (int)tilemap.MapWidth);
        int spanH = Math.Max(1, (int)tilemap.MapHeight);
        DeriveGridDimensions(numZones, zoneSize, spanW, spanH, out int numZonesX, out int numZonesZ);

        float mapScale = tilemap.MapScale > 0f ? tilemap.MapScale : 1f;
        float cellWorldSize = zoneSize * mapScale;

        layout = new OutdoorCellLayout(playfieldId, numZonesX, numZonesZ, cellWorldSize);
        Debug.Log(
            $"[PlayfieldLayout] Outdoor {playfieldId} \"{playfield.Name}\": " +
            $"zoneSize={zoneSize} zones={numZonesX}x{numZonesZ} ({numZones}) cellWorld={cellWorldSize:F1}");
        return true;
    }

    static bool TryReadOutdoorGrid(
        ResourceDatabase database,
        int playfieldId,
        RDBPlayfield playfield,
        out int zoneSize,
        out int numZones)
    {
        zoneSize = playfield.Unknown2 > 0 ? playfield.Unknown2 : 10;
        numZones = 0;

        byte[] raw;
        try
        {
            raw = database.GetRaw(PlayfieldRecordType, playfieldId);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayfieldLayout] Failed to read raw playfield {playfieldId}: {ex.Message}");
            return false;
        }

        if (raw == null || raw.Length < NumZonesOffset + 4)
        {
            Debug.LogError($"[PlayfieldLayout] Playfield {playfieldId} raw blob too small for zone grid.");
            return false;
        }

        int rawZoneSize = BitConverter.ToInt32(raw, ZoneSizeOffset);
        numZones = BitConverter.ToInt32(raw, NumZonesOffset);

        if (rawZoneSize > 0)
            zoneSize = rawZoneSize;

        if (zoneSize <= 0 || numZones <= 0)
        {
            Debug.LogError($"[PlayfieldLayout] Invalid outdoor grid for {playfieldId}: zoneSize={zoneSize} numZones={numZones}");
            return false;
        }

        return true;
    }

    static void DeriveGridDimensions(int numZones, int zoneSize, int spanW, int spanH, out int numZonesX, out int numZonesZ)
    {
        int root = (int)Math.Round(Math.Sqrt(numZones));
        if (root > 0 && root * root == numZones)
        {
            numZonesX = root;
            numZonesZ = root;
            return;
        }

        int bestX = 1;
        int bestZ = numZones;
        int bestScore = int.MaxValue;

        for (int x = 1; x * x <= numZones; x++)
        {
            if (numZones % x != 0)
                continue;

            int z = numZones / x;
            ScoreCandidate(x, z, zoneSize, spanW, spanH, ref bestX, ref bestZ, ref bestScore);
            ScoreCandidate(z, x, zoneSize, spanW, spanH, ref bestX, ref bestZ, ref bestScore);
        }

        numZonesX = bestX;
        numZonesZ = bestZ;
    }

    static void ScoreCandidate(
        int nx,
        int nz,
        int zoneSize,
        int spanW,
        int spanH,
        ref int bestX,
        ref int bestZ,
        ref int bestScore)
    {
        int score = Math.Abs(nx * zoneSize - spanW) + Math.Abs(nz * zoneSize - spanH);
        if (score >= bestScore)
            return;

        bestScore = score;
        bestX = nx;
        bestZ = nz;
    }
}

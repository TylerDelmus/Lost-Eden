using System.Collections.Generic;
using UnityEngine;

public sealed class OutdoorCellLayout : IPlayfieldCellLayout
{
    public OutdoorCellLayout(int playfieldId, int numZonesX, int numZonesZ, float cellWorldSize)
    {
        PlayfieldId = playfieldId;
        NumZonesX = numZonesX;
        NumZonesZ = numZonesZ;
        CellWorldSize = cellWorldSize;
    }

    public int PlayfieldId { get; }
    public bool IsIndoor => false;
    public int NumZonesX { get; }
    public int NumZonesZ { get; }
    public float CellWorldSize { get; }

    public bool TryGetCellId(Vector3 worldPosition, out int cellId)
    {
        if (CellWorldSize <= 0f || NumZonesX <= 0 || NumZonesZ <= 0)
        {
            cellId = -1;
            return false;
        }

        int ix = Mathf.FloorToInt(worldPosition.x / CellWorldSize);
        int iz = Mathf.FloorToInt(worldPosition.z / CellWorldSize);
        if (ix < 0 || iz < 0 || ix >= NumZonesX || iz >= NumZonesZ)
        {
            cellId = -1;
            return false;
        }

        cellId = GetCellId(ix, iz);
        return true;
    }

    public void GetCellCoords(int cellId, out int ix, out int iz)
    {
        ix = cellId % NumZonesX;
        iz = cellId / NumZonesX;
    }

    public int GetCellId(int ix, int iz) => iz * NumZonesX + ix;

    public void CollectNeighbors(int cellId, int radius, List<int> results)
    {
        results.Clear();
        if (radius < 0 || NumZonesX <= 0 || NumZonesZ <= 0)
            return;

        GetCellCoords(cellId, out int cx, out int cz);
        int minX = Mathf.Max(0, cx - radius);
        int maxX = Mathf.Min(NumZonesX - 1, cx + radius);
        int minZ = Mathf.Max(0, cz - radius);
        int maxZ = Mathf.Min(NumZonesZ - 1, cz + radius);

        for (int iz = minZ; iz <= maxZ; iz++)
        {
            for (int ix = minX; ix <= maxX; ix++)
                results.Add(GetCellId(ix, iz));
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Indoor rooms-as-cells are not implemented yet. Locality stays empty until room graphs exist.
/// </summary>
public sealed class IndoorCellLayout : IPlayfieldCellLayout
{
    public IndoorCellLayout(int playfieldId)
    {
        PlayfieldId = playfieldId;
    }

    public int PlayfieldId { get; }
    public bool IsIndoor => true;
    public int NumZonesX => 0;
    public int NumZonesZ => 0;
    public float CellWorldSize => 0f;

    public bool TryGetCellId(Vector3 worldPosition, out int cellId)
    {
        cellId = -1;
        return false;
    }

    public void GetCellCoords(int cellId, out int ix, out int iz)
    {
        ix = 0;
        iz = 0;
    }

    public int GetCellId(int ix, int iz) => -1;

    public void CollectNeighbors(int cellId, int radius, List<int> results)
    {
        results.Clear();
    }
}

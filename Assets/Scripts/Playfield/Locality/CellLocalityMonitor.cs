using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CellLocalityMonitor
{
    public const int NeighborLevel = 2;

    readonly IPlayfieldCellLayout _layout;
    readonly HashSet<int> _desired = new();
    readonly HashSet<int> _nextDesired = new();
    readonly List<int> _neighborBuffer = new();
    readonly List<int> _foundBuffer = new();
    readonly List<int> _lostBuffer = new();

    int _currentCellId = -1;

    public CellLocalityMonitor(IPlayfieldCellLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    }

    public int CurrentCellId => _currentCellId;
    public IReadOnlyCollection<int> DesiredCells => _desired;

    public event Action<IReadOnlyList<int>> CellsFound;
    public event Action<IReadOnlyList<int>> CellsLost;

    public void Clear()
    {
        if (_desired.Count > 0)
        {
            _lostBuffer.Clear();
            _lostBuffer.AddRange(_desired);
            _desired.Clear();
            CellsLost?.Invoke(_lostBuffer);
        }

        _currentCellId = -1;
    }

    public void Update(Vector3 worldPosition)
    {
        if (_layout.IsIndoor)
            return;

        if (!_layout.TryGetCellId(worldPosition, out int cellId))
        {
            if (_currentCellId != -1)
                Clear();
            return;
        }

        if (cellId == _currentCellId)
            return;

        _currentCellId = cellId;
        _layout.CollectNeighbors(cellId, NeighborLevel, _neighborBuffer);

        _nextDesired.Clear();
        for (int i = 0; i < _neighborBuffer.Count; i++)
            _nextDesired.Add(_neighborBuffer[i]);

        _foundBuffer.Clear();
        _lostBuffer.Clear();

        foreach (int id in _nextDesired)
        {
            if (!_desired.Contains(id))
                _foundBuffer.Add(id);
        }

        foreach (int id in _desired)
        {
            if (!_nextDesired.Contains(id))
                _lostBuffer.Add(id);
        }

        _desired.Clear();
        foreach (int id in _nextDesired)
            _desired.Add(id);

        if (_lostBuffer.Count > 0)
            CellsLost?.Invoke(_lostBuffer);
        if (_foundBuffer.Count > 0)
            CellsFound?.Invoke(_foundBuffer);
    }
}

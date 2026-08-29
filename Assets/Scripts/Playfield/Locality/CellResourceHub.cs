using System;
using System.Collections.Generic;

public sealed class CellResourceHub
{
    readonly CellLocalityMonitor _monitor;
    readonly List<ICellResourceLoader> _loaders = new();

    public CellResourceHub(CellLocalityMonitor monitor)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _monitor.CellsFound += OnCellsFound;
        _monitor.CellsLost += OnCellsLost;
    }

    public void AddLoader(ICellResourceLoader loader)
    {
        if (loader == null)
            throw new ArgumentNullException(nameof(loader));
        _loaders.Add(loader);
    }

    public void Tick()
    {
        for (int i = 0; i < _loaders.Count; i++)
            _loaders[i].Tick();
    }

    public void Clear()
    {
        for (int i = 0; i < _loaders.Count; i++)
            _loaders[i].Clear();
    }

    public void Dispose()
    {
        _monitor.CellsFound -= OnCellsFound;
        _monitor.CellsLost -= OnCellsLost;
        Clear();
        _loaders.Clear();
    }

    void OnCellsFound(IReadOnlyList<int> cellIds)
    {
        for (int i = 0; i < _loaders.Count; i++)
            _loaders[i].OnCellsFound(cellIds);
    }

    void OnCellsLost(IReadOnlyList<int> cellIds)
    {
        for (int i = 0; i < _loaders.Count; i++)
            _loaders[i].OnCellsLost(cellIds);
    }
}

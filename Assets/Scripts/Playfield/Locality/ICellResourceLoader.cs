using System.Collections.Generic;

public interface ICellResourceLoader
{
    void OnCellsFound(IReadOnlyList<int> cellIds);
    void OnCellsLost(IReadOnlyList<int> cellIds);
    void Tick();
    void Clear();
}

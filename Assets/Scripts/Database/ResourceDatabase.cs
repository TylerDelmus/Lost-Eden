using AODB;
using AODB.Common.RDBObjects;
using System;

public sealed class ResourceDatabase
{
    public RdbController Rdb { get; private set; }

    public void Initialize(string aoBasePath)
    {
        Rdb?.Dispose();
        Rdb = new RdbController(aoBasePath);
    }

    public T Get<T>(int instance) where T : RDBObject, new()
    {
        if (Rdb == null)
            throw new InvalidOperationException("ResourceDatabase has not been initialized.");

        return Rdb.Get<T>(instance);
    }

    public T Get<T>(ResourceTypeId type, int instance) where T : RDBObject, new()
    {
        if (Rdb == null)
            throw new InvalidOperationException("ResourceDatabase has not been initialized.");

        return Rdb.Get<T>(type, instance);
    }
}

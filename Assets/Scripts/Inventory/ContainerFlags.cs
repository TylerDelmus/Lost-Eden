using System;

[Flags]
public enum ContainerFlags
{
    None = 0,
    CanAdd = 1 << 0,
    CanRemove = 1 << 1,
}

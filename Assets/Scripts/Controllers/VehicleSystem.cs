using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Steps every <see cref="N3CharVehicle"/> in one loop, in place of an <c>Update</c> per body.
///
/// <para>
/// It runs after input and locality (<see cref="PlayerController"/> at -100,
/// <c>PlayfieldLocality</c> at -50) and before the dynel scripts at the default order, so
/// <c>Character</c>'s animation choice always reads this frame's step rather than whichever order
/// Unity happened to pick.
/// </para>
///
/// <para>
/// A frame is three passes over every body: the transforms go into the sims, the sims run, the
/// results come back out. The middle pass touches nothing but the sims and the collision surfaces,
/// which is what lets it move to worker threads later. Bodies do not read each other during a step,
/// so the passes give each body exactly the step it had when it updated itself.
/// </para>
///
/// <para>
/// All three passes run on the main thread for now. Before the middle one can go wide,
/// <see cref="N3Lite.VehicleSim.DeltaTimeNow"/> (a shared static) has to become per-thread
/// and <see cref="N3CharVehicle.JumpLanded"/> (raised from inside the sim) has to be deferred.
/// </para>
/// </summary>
[DefaultExecutionOrder(-10)]
public sealed class VehicleSystem : MonoBehaviour
{
    static VehicleSystem s_instance;

    readonly List<N3CharVehicle> _vehicles = new();
    bool _stepping;
    bool _hasHoles;

    /// <summary>Domain reload on entering Play is off, so the instance has to be forgotten by hand.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_instance = null;

    /// <summary>How many bodies are registered.</summary>
    public static int Count => s_instance != null ? s_instance._vehicles.Count : 0;

    /// <summary>Called from <see cref="N3CharVehicle"/>'s <c>OnEnable</c>. Creates the system on first use.</summary>
    internal static void Register(N3CharVehicle vehicle)
    {
        if (s_instance == null)
        {
            var go = new GameObject(nameof(VehicleSystem));
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<VehicleSystem>();
        }

        s_instance.Add(vehicle);
    }

    /// <summary>Called from <see cref="N3CharVehicle"/>'s <c>OnDisable</c>.</summary>
    internal static void Unregister(N3CharVehicle vehicle)
    {
        if (s_instance != null)
            s_instance.Remove(vehicle);
    }

    void Add(N3CharVehicle vehicle)
    {
        if (vehicle.SystemIndex >= 0)
            return;

        // A body added mid-frame lands past the count the passes captured, so it first steps next
        // frame -- as a freshly enabled component would.
        vehicle.SystemIndex = _vehicles.Count;
        _vehicles.Add(vehicle);
    }

    void Remove(N3CharVehicle vehicle)
    {
        int index = vehicle.SystemIndex;
        if (index < 0 || index >= _vehicles.Count || !ReferenceEquals(_vehicles[index], vehicle))
            return;

        vehicle.SystemIndex = -1;

        // Mid-frame, leave a hole so the passes' indices stay put; it is closed after the frame.
        if (_stepping)
        {
            _vehicles[index] = null;
            _hasHoles = true;
            return;
        }

        int last = _vehicles.Count - 1;
        if (index != last)
        {
            N3CharVehicle moved = _vehicles[last];
            _vehicles[index] = moved;
            moved.SystemIndex = index;
        }

        _vehicles.RemoveAt(last);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        int count = _vehicles.Count;

        _stepping = true;
        try
        {
            for (int i = 0; i < count; i++)
            {
                N3CharVehicle v = _vehicles[i];
                if ((object)v == null)
                    continue;
                try { v.BeginStep(dt); }
                catch (Exception e) { Debug.LogException(e, v); }
            }

            for (int i = 0; i < count; i++)
            {
                N3CharVehicle v = _vehicles[i];
                if ((object)v == null)
                    continue;
                try { v.RunStep(dt); }
                catch (Exception e) { Debug.LogException(e, v); }
            }

            for (int i = 0; i < count; i++)
            {
                N3CharVehicle v = _vehicles[i];
                if ((object)v == null)
                    continue;
                try { v.EndStep(); }
                catch (Exception e) { Debug.LogException(e, v); }
            }
        }
        finally
        {
            _stepping = false;
        }

        if (_hasHoles)
            CloseHoles();
    }

    void CloseHoles()
    {
        _hasHoles = false;

        int write = 0;
        for (int read = 0; read < _vehicles.Count; read++)
        {
            N3CharVehicle v = _vehicles[read];
            if ((object)v == null)
                continue;
            v.SystemIndex = write;
            _vehicles[write++] = v;
        }

        _vehicles.RemoveRange(write, _vehicles.Count - write);
    }
}

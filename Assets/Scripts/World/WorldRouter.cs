using Reflex.Core;
using Reflex.Injectors;
using UnityEngine;

/// <summary>
/// Owns which world is alive. Stock builds and tears down <c>LoginWorld_c</c> around the
/// playing world rather than keeping both, which is the split used here: services stay in the
/// scene container, worlds come and go.
///
/// (Stock's ResourceManager refcount in that constructor is a scoped guard — +1 on entry and
/// -1 before returning — not a hold that outlives the world. It is not modelled here.)
///
/// Worlds are created after the container is built, so they are injected explicitly through
/// <see cref="GameObjectInjector"/> rather than by Reflex's scene sweep.
/// </summary>
public sealed class WorldRouter
{
    readonly Transform _parent;
    readonly GameObject _loginWorldPrefab;
    readonly GameObject _gameWorldPrefab;

    Container _container;
    GameObject _current;

    public WorldKind Current { get; private set; } = WorldKind.None;

    public LoginWorld LoginWorld => _current != null ? _current.GetComponent<LoginWorld>() : null;

    public WorldRouter(Transform parent, GameObject loginWorldPrefab, GameObject gameWorldPrefab)
    {
        _parent = parent;
        _loginWorldPrefab = loginWorldPrefab;
        _gameWorldPrefab = gameWorldPrefab;
    }

    /// <summary>Called once the scene container exists; nothing can be spawned before this.</summary>
    public void Bind(Container container)
    {
        _container = container;
    }

    public LoginWorld EnterLogin()
    {
        Enter(WorldKind.Login, _loginWorldPrefab, "LoginWorld", go => go.AddComponent<LoginWorld>());
        return LoginWorld;
    }

    public void EnterGame()
    {
        Enter(WorldKind.Game, _gameWorldPrefab, "GameWorld", null);
    }

    public void Leave()
    {
        if (_current != null)
            Object.Destroy(_current);

        _current = null;
        Current = WorldKind.None;
    }

    void Enter(WorldKind kind, GameObject prefab, string fallbackName, System.Action<GameObject> fallbackBuild)
    {
        if (_container == null)
        {
            Debug.LogError("[WorldRouter] Bind() has not run; the container is not available yet.");
            return;
        }

        if (Current == kind)
            return;

        Leave();

        if (prefab != null)
        {
            _current = Object.Instantiate(prefab, _parent);
            _current.name = prefab.name;
        }
        else
        {
            _current = new GameObject(fallbackName);
            _current.transform.SetParent(_parent, false);
            fallbackBuild?.Invoke(_current);
        }

        // Instantiated after the scene sweep, so inject by hand.
        GameObjectInjector.InjectRecursive(_current, _container);
        Current = kind;
    }
}

public enum WorldKind
{
    None,
    Login,
    Game
}

using UnityEngine;
using System.Collections.Generic;

public enum CursorState
{
    Default,
    Combat,
    Pickup
}

[System.Serializable]
public class CursorData
{
    public CursorState state;
    public Texture2D texture;
}

public class CursorController : MonoBehaviour
{
    public static CursorController Instance { get; private set; }

    [Header("Cursors")]
    [SerializeField] private List<CursorData> cursorList = new List<CursorData>();
    [SerializeField] private Vector2 hotspot = Vector2.zero;

    private Dictionary<CursorState, Texture2D> cursors = new Dictionary<CursorState, Texture2D>();
    private CursorState currentState;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Build dictionary from list
        cursors.Clear();
        foreach (var cursorData in cursorList)
        {
            if (cursorData.texture != null && !cursors.ContainsKey(cursorData.state))
            {
                cursors[cursorData.state] = cursorData.texture;
            }
        }

        SetCursor(CursorState.Default, force: true);
    }

    public void SetCursor(CursorState state, bool force = false)
    {
        if (!force && state == currentState)
            return;

        currentState = state;

        if (cursors.TryGetValue(state, out Texture2D cursorTexture))
        {
            Cursor.SetCursor(cursorTexture, hotspot, CursorMode.Auto);
        }
        else
        {
            Debug.LogWarning($"Cursor texture for state {state} not found!");
            Cursor.SetCursor(null, hotspot, CursorMode.Auto);
        }
    }
}

using UnityEngine;

public sealed class AbiffUvAnimator : MonoBehaviour
{
    MeshRenderer _renderer;
    AbiffUvKey[] _keys;
    bool _loop;
    float _duration;
    float _time;
    int _stPropertyId;
    MaterialPropertyBlock _mpb;
    bool _ready;

    void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
        _mpb = new MaterialPropertyBlock();
        ResolveTextureProperty();
        _ready = true;
        if (_keys != null && _keys.Length > 0)
            ApplyFrame();
    }

    public void Init(AbiffUvKey[] keys, bool loop, float duration)
    {
        _keys = keys;
        _loop = loop;
        _duration = duration > 0f
            ? duration
            : (keys != null && keys.Length > 0 ? keys[keys.Length - 1].Time : 0f);
        _time = 0f;

        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        ResolveTextureProperty();

        if (_ready)
            ApplyFrame();
    }

    void Update()
    {
        if (!_ready || _keys == null || _keys.Length < 2 || _renderer == null)
            return;

        _time += Time.deltaTime;
        float duration = Mathf.Max(_duration, 0.0001f);
        if (_loop)
        {
            if (_time >= duration)
                _time %= duration;
        }
        else if (_time > duration)
        {
            _time = duration;
        }

        ApplyFrame();
    }

    void ResolveTextureProperty()
    {
        Material mat = _renderer != null ? _renderer.sharedMaterial : null;
        _stPropertyId = mat != null && mat.HasProperty("_BaseColorMap")
            ? Shader.PropertyToID("_BaseColorMap_ST")
            : Shader.PropertyToID("_MainTex_ST");
    }

    void ApplyFrame()
    {
        if (_mpb == null || _keys == null || _keys.Length == 0 || _renderer == null)
            return;

        Sample(_time, out Vector2 offset, out Vector2 tiling);

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetVector(_stPropertyId, new Vector4(tiling.x, tiling.y, offset.x, offset.y));
        _renderer.SetPropertyBlock(_mpb);
    }

    void Sample(float time, out Vector2 offset, out Vector2 tiling)
    {
        if (_keys.Length == 1)
        {
            offset = _keys[0].Offset;
            tiling = _keys[0].Tiling;
            return;
        }

        int last = _keys.Length - 1;
        if (time <= _keys[0].Time)
        {
            offset = _keys[0].Offset;
            tiling = _keys[0].Tiling;
            return;
        }

        if (time >= _keys[last].Time)
        {
            offset = _keys[last].Offset;
            tiling = _keys[last].Tiling;
            return;
        }

        int i = 0;
        while (i < last - 1 && _keys[i + 1].Time < time)
            i++;

        float t0 = _keys[i].Time;
        float t1 = _keys[i + 1].Time;
        float seg = t1 - t0;
        float u = seg > 0.0001f ? Mathf.Clamp01((time - t0) / seg) : 0f;

        offset = Vector2.Lerp(_keys[i].Offset, _keys[i + 1].Offset, u);
        tiling = Vector2.Lerp(_keys[i].Tiling, _keys[i + 1].Tiling, u);
    }
}

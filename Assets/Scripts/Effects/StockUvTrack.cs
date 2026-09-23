using System;

/// <summary>
/// The UV track of an ABIFF animation (randy31 <c>FAFAnim_t</c>, vftable <c>1008a7cc</c>), as its evaluate
/// (slot 3, <c>10028fde</c>) sets the tiling (+0x84) and offset (+0x8c) for a time; and the node's time
/// (<c>RRefFrame_t::SetAnimationTime</c>, <c>1004506b</c>).
///
/// Keys are (tiling u, v, offset u, v, time, lerp flag), 0x18 bytes each (AODB's <c>UVKey.Unk2</c> is the flag).
/// Evaluate(t):
/// <list type="bullet">
/// <item>Looping, a t past the total time is fmod(t, total); not looping, it is held at the total.</item>
/// <item>With fewer than two keys nothing is set (the port returns tiling 1, offset 0).</item>
/// <item>t is held at the last key's time. The key index is cached: it steps back while key[k].time &gt; t,
/// then, only when the cached key's time was below t, forward while key[k + 1].time &lt; t.</item>
/// <item>Off key k's time, f = (t - k.time) / (k+1.time - k.time): the offset lerps from k to k + 1 when k's
/// flag is set, else it is k's; the tiling always lerps. On key k's time both are k's.</item>
/// </list>
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StockUvTrack
{
    readonly float[] _times, _tileU, _tileV, _offU, _offV;
    readonly bool[] _lerp;

    public bool Loop { get; }

    /// <summary>The animation's total time (+0x7c), its <c>tot_time</c>.</summary>
    public float TotalTime { get; }

    public int Count => _times.Length;

    public StockUvTrack(float[] times, float[] tileU, float[] tileV, float[] offU, float[] offV, bool[] lerp,
        bool loop, float totalTime)
    {
        _times = times ?? Array.Empty<float>();
        _tileU = tileU;
        _tileV = tileV;
        _offU = offU;
        _offV = offV;
        _lerp = lerp;
        Loop = loop;
        TotalTime = totalTime;
    }

    /// <summary>
    /// <c>RRefFrame_t::SetAnimationTime</c>: fmod(t, the animation's total time), where a whole number of
    /// totals above 0 gives the total rather than 0.
    /// </summary>
    public static float NodeTime(float t, float total)
    {
        float r = (float)((double)t % total);
        if (r == 0f && 0f < t)
            r = total;
        return r;
    }

    /// <summary>The evaluate's UV part; <paramref name="index"/> is the cached key (+0x38), kept per instance.</summary>
    public void Evaluate(float t, ref int index, out float tileU, out float tileV, out float offU, out float offV)
    {
        tileU = 1f;
        tileV = 1f;
        offU = 0f;
        offV = 0f;

        // 10028fea: the loop.
        if (Loop)
        {
            if (TotalTime < t)
                t = (float)((double)t % TotalTime);
        }
        else if (TotalTime < t)
        {
            t = TotalTime;
        }

        int n = _times.Length;
        if (n <= 1)
            return;

        // 10029086..100290c5: held at the last key.
        if (_times[n - 1] < t)
            t = _times[n - 1];

        if (index < 0 || index >= n)
            index = 0;

        // 1002924b..100292ab: the cached search.
        float cached = _times[index];
        if (!(cached <= t))
        {
            do
                index--;
            while (index > 0 && !(_times[index] <= t));
        }
        if (cached < t)
        {
            while (index + 1 < n && _times[index + 1] < t)
                index++;
        }

        int k = index;
        int next = Math.Min(k + 1, n - 1);
        float tk = _times[k];
        bool onKey = tk == t;

        float f = 0f;
        if (!onKey)
        {
            float d = (float)((double)t - tk);
            f = (float)(d / ((double)_times[next] - tk));
        }
        double g = 1.0 - f;

        // 100293fb / 10029465: the offset lerps only with the key's flag.
        if (!onKey && _lerp[k])
        {
            offU = (float)(_offU[next] * (double)f + _offU[k] * g);
            offV = (float)(_offV[next] * (double)f + _offV[k] * g);
        }
        else
        {
            offU = _offU[k];
            offV = _offV[k];
        }

        if (!onKey)
        {
            tileU = (float)(_tileU[next] * (double)f + _tileU[k] * g);
            tileV = (float)(_tileV[next] * (double)f + _tileV[k] * g);
        }
        else
        {
            tileU = _tileU[k];
            tileV = _tileV[k];
        }
    }
}

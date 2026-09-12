using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Parses <c>{AO}/Setupf/gfxtweak.bin</c> as stock CMSBlock records:
/// header count, then per record <c>id, typeCode, count, data[count]</c> (variable length).
/// </summary>
public static class GfxTweakParser
{
    const int HeaderBytes = 4;
    const int RecordHeaderBytes = 12; // id + typeCode + count

    public static string ResolvePath(string aoBasePath)
        => Path.Combine(AoInstallPath.Normalize(aoBasePath), "Setupf", "gfxtweak.bin");

    public static bool TryLoad(string aoBasePath, out Dictionary<int, GfxTweakRecord> records)
    {
        records = new Dictionary<int, GfxTweakRecord>();
        string path = ResolvePath(aoBasePath);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"GfxTweakParser: missing {path}");
            return false;
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < HeaderBytes)
            return false;

        int total = BitConverter.ToInt32(bytes, 0);
        if (total <= 0)
            return false;

        int offset = HeaderBytes;
        for (int i = 0; i < total; i++)
        {
            if (bytes.Length - offset < RecordHeaderBytes)
            {
                Debug.LogWarning($"GfxTweakParser: truncated header at record {i}/{total}.");
                break;
            }

            int id = BitConverter.ToInt32(bytes, offset);
            int typeCode = BitConverter.ToInt32(bytes, offset + 4);
            int count = BitConverter.ToInt32(bytes, offset + 8);
            offset += RecordHeaderBytes;

            if (count < 0 || count > 4096)
            {
                Debug.LogWarning($"GfxTweakParser: invalid count {count} at id={id}; aborting.");
                break;
            }

            int dataBytes = count * 4;
            if (bytes.Length - offset < dataBytes)
            {
                Debug.LogWarning($"GfxTweakParser: truncated data at id={id}.");
                break;
            }

            var fields = new float[count];
            for (int f = 0; f < count; f++)
                fields[f] = BitConverter.ToSingle(bytes, offset + f * 4);
            offset += dataBytes;

            records[id] = new GfxTweakRecord
            {
                Id = id,
                TypeCode = typeCode,
                Fields = fields,
            };
        }

        if (offset != bytes.Length)
        {
            Debug.LogWarning(
                $"GfxTweakParser: size mismatch consumed={offset} file={bytes.Length} records={records.Count}/{total}.");
        }
        else
        {
            Debug.Log($"GfxTweakParser: loaded {records.Count} records ({bytes.Length} bytes).");
        }

        return records.Count > 0;
    }
}

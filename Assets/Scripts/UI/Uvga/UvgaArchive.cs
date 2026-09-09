using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Load-only parser for Anarchy Online UVGI/UVGA GUI image archives.
/// UVGI is a text index; UVGA is concatenated PNG payloads.
/// </summary>
public sealed class UvgaArchive
{
    readonly Dictionary<string, Entry> _entries;
    readonly byte[] _uvgaBytes;

    UvgaArchive(Dictionary<string, Entry> entries, byte[] uvgaBytes)
    {
        _entries = entries;
        _uvgaBytes = uvgaBytes;
    }

    public int Count => _entries.Count;

    public IReadOnlyCollection<string> Names => _entries.Keys;

    public static UvgaArchive Load(string uvgiPath)
    {
        if (string.IsNullOrWhiteSpace(uvgiPath))
            throw new ArgumentException("UVGI path is required.", nameof(uvgiPath));

        string fullUvgi = Path.GetFullPath(uvgiPath);
        if (!File.Exists(fullUvgi))
            throw new FileNotFoundException("UVGI index not found.", fullUvgi);

        string uvgaPath = Path.ChangeExtension(fullUvgi, ".uvga");
        if (!File.Exists(uvgaPath))
            throw new FileNotFoundException("UVGA archive not found.", uvgaPath);

        byte[] uvgaBytes = File.ReadAllBytes(uvgaPath);
        string[] lines = File.ReadAllLines(fullUvgi);
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        int expectedCount = -1;
        int lineIndex = 0;
        for (; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex]?.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            if (!int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedCount))
            {
                Debug.LogWarning($"[UvgaArchive] Expected entry count on first non-blank line of '{fullUvgi}', got '{line}'.");
                return new UvgaArchive(entries, uvgaBytes);
            }

            lineIndex++;
            break;
        }

        for (; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex]?.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                Debug.LogWarning($"[UvgaArchive] Skipping malformed UVGI line {lineIndex + 1}: '{line}'");
                continue;
            }

            if (!int.TryParse(parts[parts.Length - 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset)
                || !int.TryParse(parts[parts.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int length))
            {
                Debug.LogWarning($"[UvgaArchive] Skipping UVGI line {lineIndex + 1} with bad offset/length: '{line}'");
                continue;
            }

            string name = parts.Length == 3
                ? parts[0]
                : string.Join(" ", parts, 0, parts.Length - 2);

            if (string.IsNullOrEmpty(name))
            {
                Debug.LogWarning($"[UvgaArchive] Skipping UVGI line {lineIndex + 1} with empty name: '{line}'");
                continue;
            }

            if (offset < 0 || length < 0 || (long)offset + length > uvgaBytes.Length)
            {
                Debug.LogWarning(
                    $"[UvgaArchive] Skipping '{name}': offset {offset} length {length} outside UVGA size {uvgaBytes.Length}.");
                continue;
            }

            if (entries.ContainsKey(name))
                Debug.LogWarning($"[UvgaArchive] Duplicate entry '{name}' in '{fullUvgi}'; overwriting.");

            entries[name] = new Entry(offset, length);
        }

        if (expectedCount >= 0 && entries.Count != expectedCount)
        {
            Debug.LogWarning(
                $"[UvgaArchive] UVGI declared {expectedCount} entries but loaded {entries.Count} from '{fullUvgi}'.");
        }

        return new UvgaArchive(entries, uvgaBytes);
    }

    public bool TryGetPngBytes(string name, out byte[] png)
    {
        png = null;
        if (string.IsNullOrEmpty(name) || !_entries.TryGetValue(name, out Entry entry))
            return false;

        png = new byte[entry.Length];
        Buffer.BlockCopy(_uvgaBytes, entry.Offset, png, 0, entry.Length);
        return true;
    }

    readonly struct Entry
    {
        public Entry(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }
        public int Length { get; }
    }
}

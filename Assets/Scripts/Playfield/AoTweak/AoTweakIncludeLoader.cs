using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Flattens AO <c>!include</c> trees under <c>cd_image/twk</c>.
/// </summary>
public static class AoTweakIncludeLoader
{
    static readonly Regex IncludePattern = new Regex(
        @"!include\s*\{\s*([^}]+?)\s*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string TwkDirectory(string aoBasePath)
        => Path.Combine(AoInstallPath.Normalize(aoBasePath), "cd_image", "twk");

    public static bool TryLoadPlayfieldFlattened(
        string aoBasePath,
        int playfieldId,
        out string flattened)
        => TryLoadPlayfieldFlattened(aoBasePath, playfieldId, out flattened, out _);

    public static bool TryLoadPlayfieldFlattened(
        string aoBasePath,
        int playfieldId,
        out string flattened,
        out HashSet<string> includedFiles)
        => TryLoadPlayfieldFlattened(aoBasePath, playfieldId, out flattened, out includedFiles, out _);

    public static bool TryLoadPlayfieldFlattened(
        string aoBasePath,
        int playfieldId,
        out string flattened,
        out HashSet<string> includedFiles,
        out List<string> includedInOrder)
    {
        flattened = null;
        includedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        includedInOrder = new List<string>();
        string twkDir = TwkDirectory(aoBasePath);
        string entry = $"Tweak_Playfield_{playfieldId}.txt";
        string entryPath = Path.Combine(twkDir, entry);
        if (!File.Exists(entryPath))
        {
            Debug.Log($"[AoTweak] No playfield tweak at {entryPath}");
            return false;
        }

        var sb = new StringBuilder(256 * 1024);
        if (!AppendFile(twkDir, entry, includedFiles, includedInOrder, sb, depth: 0))
            return false;

        flattened = sb.ToString();
        return true;
    }

    public static bool IncludesFile(HashSet<string> includedFiles, string fileName)
    {
        if (includedFiles == null || string.IsNullOrWhiteSpace(fileName))
            return false;

        if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            fileName += ".txt";

        return includedFiles.Contains(fileName);
    }

    static bool AppendFile(
        string twkDir,
        string fileName,
        HashSet<string> visited,
        List<string> includedInOrder,
        StringBuilder sb,
        int depth)
    {
        if (depth > 64)
        {
            Debug.LogWarning($"[AoTweak] Include depth exceeded at '{fileName}'.");
            return false;
        }

        if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            fileName += ".txt";

        if (!visited.Add(fileName))
            return true;

        includedInOrder.Add(fileName);

        string path = Path.Combine(twkDir, fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[AoTweak] Missing include '{fileName}'.");
            return true;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AoTweak] Failed reading '{path}': {ex.Message}");
            return true;
        }

        int last = 0;
        foreach (Match match in IncludePattern.Matches(text))
        {
            sb.Append(text, last, match.Index - last);
            string includeName = match.Groups[1].Value.Trim();
            AppendFile(twkDir, includeName, visited, includedInOrder, sb, depth + 1);
            last = match.Index + match.Length;
        }

        sb.Append(text, last, text.Length - last);
        sb.AppendLine();
        return true;
    }
}

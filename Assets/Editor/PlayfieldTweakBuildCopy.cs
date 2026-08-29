#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class PlayfieldTweakBuildCopy
{
    const string FolderName = "twk";

    [PostProcessBuild(1)]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FolderName));
        if (!Directory.Exists(source))
        {
            Debug.LogWarning($"[PlayfieldTweak] Build copy skipped — missing '{source}'.");
            return;
        }

        string buildDir = Path.GetDirectoryName(pathToBuiltProject);
        string dest = Path.Combine(buildDir, FolderName);
        CopyDirectory(source, dest);
        Debug.Log($"[PlayfieldTweak] Copied {FolderName}/ → {dest}");
    }

    static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destDir, name), overwrite: true);
        }

        foreach (string directory in Directory.GetDirectories(sourceDir))
        {
            string name = Path.GetFileName(directory);
            CopyDirectory(directory, Path.Combine(destDir, name));
        }
    }
}
#endif

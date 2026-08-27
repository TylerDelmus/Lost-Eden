#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class DimensionConfigBuildCopy
{
    const string FileName = "dimensions.json";

    [PostProcessBuild(1)]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));
        if (!File.Exists(source))
        {
            Debug.LogError($"[Network] Build copy failed — missing '{source}'.");
            return;
        }

        string buildDir = Path.GetDirectoryName(pathToBuiltProject);
        string dest = Path.Combine(buildDir, FileName);
        File.Copy(source, dest, overwrite: true);
        Debug.Log($"[Network] Copied {FileName} → {dest}");
    }
}
#endif

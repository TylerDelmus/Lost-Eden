#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class AnimCalibrationBuildCopy
{
    const string FileName = "anim_calibration.json";

    [PostProcessBuild(1)]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));
        if (!File.Exists(source))
        {
            Debug.LogWarning($"[AnimCalibration] Build copy skipped — missing '{source}'.");
            return;
        }

        string buildDir = Path.GetDirectoryName(pathToBuiltProject);
        string dest = Path.Combine(buildDir, FileName);
        File.Copy(source, dest, overwrite: true);
        Debug.Log($"[AnimCalibration] Copied {FileName} → {dest}");
    }
}
#endif

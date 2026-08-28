using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Ensures a Resources HDRP Lit material with alpha clipping exists so player
/// builds keep the _ALPHATEST_ON shader variant for runtime materials.
/// </summary>
[InitializeOnLoad]
public sealed class HdrpLitVariantEnsure : IPreprocessBuildWithReport
{
    const string AssetPath = "Assets/Resources/Materials/HdrpLitAlphaClip.mat";

    public int callbackOrder => 0;

    static HdrpLitVariantEnsure()
    {
        EditorApplication.delayCall += EnsureAsset;
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        EnsureAsset();
    }

    [MenuItem("Lost Eden/Rendering/Ensure HDRP Lit Alpha Clip Variant")]
    public static void EnsureAsset()
    {
        string folder = Path.GetDirectoryName(AssetPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Materials"))
                AssetDatabase.CreateFolder("Assets/Resources", "Materials");
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetPath);
        if (material == null)
        {
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Debug.LogError("[HdrpLitVariantEnsure] HDRP/Lit shader not found.");
                return;
            }

            material = new Material(shader) { name = "HdrpLitAlphaClip" };
            AssetDatabase.CreateAsset(material, AssetPath);
        }

        HDMaterial.SetAlphaClipping(material, true);
        HDMaterial.SetAlphaCutoff(material, 0.5f);
        HDMaterial.ValidateMaterial(material);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
    }
}

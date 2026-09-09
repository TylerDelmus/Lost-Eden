using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stitches six cubemap face textures into one horizontal-cross image and
/// imports it as a modern Texture Shape = Cube asset.
/// </summary>
public static class BuildCubemapFromFaces
{
    const string SkyBoxFolder = "Assets/Resources/SkyBox";
    const string DefaultOutputPath = SkyBoxFolder + "/StarBox_Cross.png";

    static readonly (string fileName, CubemapFace face, int cellX, int cellY)[] FaceLayout =
    {
        ("Stars_up.png", CubemapFace.PositiveY, 1, 2),
        ("Stars_left.png", CubemapFace.NegativeX, 0, 1),
        ("Stars_front.png", CubemapFace.PositiveZ, 1, 1),
        ("Stars_right.png", CubemapFace.PositiveX, 2, 1),
        ("Stars_back.png", CubemapFace.NegativeZ, 3, 1),
        ("Stars_down.png", CubemapFace.NegativeY, 1, 0),
    };

    [MenuItem("Lost Eden/SkyBox/Build StarBox Cross Cubemap")]
    public static void BuildStarBoxCross()
    {
        BuildCross(SkyBoxFolder, DefaultOutputPath);
    }

    [MenuItem("Lost Eden/SkyBox/Build Cubemap Cross From Selected Folder")]
    public static void BuildFromSelectedFolder()
    {
        string folder = GetSelectedFolder();
        if (string.IsNullOrEmpty(folder))
        {
            EditorUtility.DisplayDialog(
                "Build Cubemap",
                "Select a folder in the Project window that contains the six Stars_*.png faces.",
                "OK");
            return;
        }

        string outputPath = $"{folder}/Cubemap_Cross.png";
        BuildCross(folder, outputPath);
    }

    static void BuildCross(string folder, string outputPath)
    {
        Texture2D[] faces = new Texture2D[FaceLayout.Length];
        int faceSize = -1;

        try
        {
            for (int i = 0; i < FaceLayout.Length; i++)
            {
                string path = $"{folder}/{FaceLayout[i].fileName}";
                if (!File.Exists(path))
                {
                    EditorUtility.DisplayDialog(
                        "Build Cubemap",
                        $"Missing face texture:\n{path}\n\nIf this is a Git LFS pointer, run:\ngit lfs pull",
                        "OK");
                    return;
                }

                try
                {
                    faces[i] = LoadPng(path);
                }
                catch (InvalidDataException ex)
                {
                    EditorUtility.DisplayDialog("Build Cubemap", ex.Message, "OK");
                    return;
                }

                if (faces[i].width != faces[i].height)
                {
                    EditorUtility.DisplayDialog(
                        "Build Cubemap",
                        $"{FaceLayout[i].fileName} must be square (got {faces[i].width}x{faces[i].height}).",
                        "OK");
                    return;
                }

                if (faceSize < 0)
                    faceSize = faces[i].width;
                else if (faces[i].width != faceSize)
                {
                    EditorUtility.DisplayDialog(
                        "Build Cubemap",
                        $"All faces must share one size. {FaceLayout[i].fileName} is {faces[i].width}px, expected {faceSize}px.",
                        "OK");
                    return;
                }
            }

            int width = faceSize * 4;
            int height = faceSize * 3;
            var atlas = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = Path.GetFileNameWithoutExtension(outputPath),
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            Color32[] clear = new Color32[width * height];
            for (int i = 0; i < clear.Length; i++)
                clear[i] = new Color32(0, 0, 0, 255);
            atlas.SetPixels32(clear);

            for (int i = 0; i < FaceLayout.Length; i++)
            {
                int x = FaceLayout[i].cellX * faceSize;
                int y = FaceLayout[i].cellY * faceSize;
                atlas.SetPixels(x, y, faceSize, faceSize, faces[i].GetPixels());
            }

            atlas.Apply(false, false);

            string absolutePath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllBytes(absolutePath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);

            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            ConfigureAsCube(outputPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Object asset = AssetDatabase.LoadAssetAtPath<Object>(outputPath);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);

            Debug.Log($"Built cubemap cross ({faceSize}px faces) at {outputPath}. Texture Shape set to Cube.");
        }
        finally
        {
            for (int i = 0; i < faces.Length; i++)
            {
                if (faces[i] != null)
                    Object.DestroyImmediate(faces[i]);
            }
        }
    }

    static Texture2D LoadPng(string assetPath)
    {
        byte[] bytes = File.ReadAllBytes(assetPath);
        if (bytes.Length < 8 || bytes[0] != 0x89)
        {
            throw new InvalidDataException(
                $"{assetPath} does not look like a PNG. Current size: {bytes.Length} bytes. " +
                "Pull Git LFS objects if this is still a pointer file.");
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes, markNonReadable: false))
            throw new InvalidDataException($"Failed to decode PNG: {assetPath}");

        return texture;
    }

    static void ConfigureAsCube(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.TextureCube;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.streamingMipmaps = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = 1;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.textureShape = TextureImporterShape.TextureCube;
        settings.cubemapConvolution = TextureImporterCubemapConvolution.None;
        settings.seamlessCubemap = true;
        importer.SetTextureSettings(settings);

        TextureImporterPlatformSettings platform = importer.GetDefaultPlatformTextureSettings();
        platform.maxTextureSize = 2048;
        platform.format = TextureImporterFormat.Automatic;
        platform.textureCompression = TextureImporterCompression.Compressed;
        importer.SetPlatformTextureSettings(platform);

        importer.SaveAndReimport();
    }

    static string GetSelectedFolder()
    {
        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
                continue;

            if (AssetDatabase.IsValidFolder(path))
                return path;

            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
                return folder;
        }

        return null;
    }
}

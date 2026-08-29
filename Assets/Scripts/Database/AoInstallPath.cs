using System.IO;

public static class AoInstallPath
{
    const string ResourceDatabaseIndexRelativePath = @"cd_image\data\db\ResourceDatabase.idx";

    public static bool IsValid(string aoBasePath)
    {
        if (string.IsNullOrWhiteSpace(aoBasePath))
            return false;

        if (!Directory.Exists(aoBasePath))
            return false;

        return File.Exists(Path.Combine(aoBasePath, ResourceDatabaseIndexRelativePath));
    }

    public static string Normalize(string aoBasePath)
    {
        if (string.IsNullOrWhiteSpace(aoBasePath))
            return string.Empty;

        return Path.GetFullPath(aoBasePath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}

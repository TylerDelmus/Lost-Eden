using System;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Player builds do not include Windows code pages by default. AODB uses
/// Encoding 1252 when reading RDB records, so register the provider early.
/// </summary>
public static class CodePagesBootstrap
{
    const string ProviderTypeName = "System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        // Resolve via reflection: Unity Code Coverage's ReportGeneratorMerged.dll embeds a
        // private CodePagesEncodingProvider (CS0122) when the public NuGet assembly is not
        // in the compile references.
        var providerType = Type.GetType(ProviderTypeName, throwOnError: false)
            ?? FindLoadedProviderType();

        if (providerType == null)
        {
            Debug.LogError("[Bootstrap] System.Text.Encoding.CodePages is not loaded; Encoding 1252 unavailable.");
            return;
        }

        var instance = (EncodingProvider)providerType
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
            .GetValue(null);

        Encoding.RegisterProvider(instance);
        Debug.Log("[Bootstrap] Registered System.Text.Encoding.CodePages provider.");
    }

    static Type FindLoadedProviderType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name != "System.Text.Encoding.CodePages")
                continue;

            return assembly.GetType("System.Text.CodePagesEncodingProvider");
        }

        return null;
    }
}

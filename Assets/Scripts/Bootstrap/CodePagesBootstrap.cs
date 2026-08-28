using System.Text;
using UnityEngine;

/// <summary>
/// Player builds do not include Windows code pages by default. AODB uses
/// Encoding 1252 when reading RDB records, so register the provider early.
/// </summary>
public static class CodePagesBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Debug.Log("[Bootstrap] Registered System.Text.Encoding.CodePages provider.");
    }
}

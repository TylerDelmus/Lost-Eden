using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

public static class NativeFolderDialog
{
    public static bool TryPickFolder(string title, string initialDirectory, out string selectedPath)
    {
        selectedPath = null;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return TryPickFolderWindows(title, initialDirectory, out selectedPath);
#else
        Debug.LogWarning("[NativeFolderDialog] Folder picking is only supported on Windows.");
        return false;
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    const uint BifReturnOnlyFsDirs = 0x00000001;
    const uint BifNewDialogStyle = 0x00000040;

    static bool TryPickFolderWindows(string title, string initialDirectory, out string selectedPath)
    {
        selectedPath = null;

        IntPtr pidl = IntPtr.Zero;
        IntPtr displayName = Marshal.AllocHGlobal(260 * sizeof(char));
        try
        {
            var bi = new BrowseInfo
            {
                hwndOwner = GetActiveWindow(),
                pidlRoot = IntPtr.Zero,
                pszDisplayName = displayName,
                lpszTitle = string.IsNullOrWhiteSpace(title) ? "Select Folder" : title,
                ulFlags = BifReturnOnlyFsDirs | BifNewDialogStyle,
                lpfn = IntPtr.Zero,
                lParam = IntPtr.Zero,
                iImage = 0
            };

            // Optional: start in a directory via callback. Keep simple — null callback is fine.
            if (!string.IsNullOrWhiteSpace(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
            {
                IntPtr callback = Marshal.GetFunctionPointerForDelegate(_browseCallback);
                bi.lpfn = callback;
                bi.lParam = Marshal.StringToHGlobalUni(initialDirectory);
            }

            pidl = SHBrowseForFolder(ref bi);
            if (bi.lParam != IntPtr.Zero)
                Marshal.FreeHGlobal(bi.lParam);

            if (pidl == IntPtr.Zero)
                return false;

            var path = new StringBuilder(260);
            if (!SHGetPathFromIDList(pidl, path))
                return false;

            selectedPath = path.ToString();
            return !string.IsNullOrEmpty(selectedPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NativeFolderDialog] Failed to open folder picker: {ex.Message}");
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(displayName);
            if (pidl != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pidl);
        }
    }

    const uint BffmInitialized = 1;
    const uint BffmSetSelectionW = 1024 + 103;

    static readonly BrowseCallbackProc _browseCallback = OnBrowseCallback;

    static int OnBrowseCallback(IntPtr hwnd, uint msg, IntPtr lParam, IntPtr lpData)
    {
        if (msg == BffmInitialized && lpData != IntPtr.Zero)
            SendMessage(hwnd, BffmSetSelectionW, (IntPtr)1, lpData);

        return 0;
    }

    delegate int BrowseCallbackProc(IntPtr hwnd, uint msg, IntPtr lParam, IntPtr lpData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct BrowseInfo
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHBrowseForFolder(ref BrowseInfo lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);
#endif
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Native Windows folder picker for session/workspace folder fields, so the
/// operator can click "Bläddra..." instead of typing a path by hand. Raw COM
/// interop (IFileOpenDialog) rather than System.Windows.Forms.FolderBrowserDialog
/// on purpose - AiLocal.Node targets plain net10.0 (Microsoft.NET.Sdk.Web),
/// not net10.0-windows, because Workers already run on Linux/macOS (see
/// AutoStartManager's same OperatingSystem.IsWindows() gating pattern for the
/// precedent). A WinForms dependency would force a Windows-only TFM on the
/// whole project; COM interop attributes compile fine on any TFM and simply
/// never execute off Windows.
/// </summary>
public static class NativeDialogs
{
    [SupportedOSPlatformGuard("windows")]
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Shows the modern (Vista+) folder picker and returns the chosen path,
    /// or null if the operator cancelled. Modal native dialogs need an STA
    /// COM apartment; ASP.NET Core's request threads are ordinary thread-pool
    /// (MTA) threads, so the actual call is marshaled onto a dedicated,
    /// short-lived STA thread - a separate apartment from AiLocal.App's own
    /// WebView2 UI thread (also STA, via its [STAThread] Main), so there's no
    /// interference between the two even when running under that shell.
    /// </summary>
    public static Task<string?> PickFolderAsync(string? initialDirectory)
    {
        if (!IsSupported)
            return Task.FromResult<string?>(null);

        var tcs = new TaskCompletionSource<string?>();
        var thread = new Thread(() =>
        {
            try
            {
                // Redundant with the IsSupported check above (this thread is
                // never spawned otherwise) - repeated here so the platform-
                // compatibility analyzer can see the guard within this
                // closure's own flow, since it doesn't track guards across
                // the Thread boundary.
                tcs.SetResult(OperatingSystem.IsWindows() ? PickFolderCore(initialDirectory) : null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    [SupportedOSPlatform("windows")]
    private static string? PickFolderCore(string? initialDirectory)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogRcw();
        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FOS.PICKFOLDERS | FOS.FORCEFILESYSTEM | FOS.PATHMUSTEXIST | FOS.NOCHANGEDIR);
            dialog.SetTitle("Välj en mapp");

            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                var guid = typeof(IShellItem).GUID;
                if (SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref guid, out var folderItem) == 0)
                    dialog.SetFolder(folderItem);
            }

            // hr is HRESULT_CANCELLED (0x800704C7, the operator closed the
            // dialog without picking anything) on the overwhelmingly common
            // non-zero path, but any other failure gets the same treatment -
            // no folder chosen is no folder chosen either way.
            var hr = dialog.Show(IntPtr.Zero);
            if (hr != 0)
                return null;

            dialog.GetResult(out var resultItem);
            resultItem.GetDisplayName(SIGDN.FILESYSPATH, out var pathPtr);
            try
            {
                return Marshal.PtrToStringUni(pathPtr);
            }
            finally
            {
                if (pathPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path, IntPtr bindContext, ref Guid riid, out IShellItem item);

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRcw { }

    [Flags]
    private enum FOS : uint
    {
        PICKFOLDERS = 0x20,
        FORCEFILESYSTEM = 0x40,
        NOCHANGEDIR = 0x8,
        PATHMUSTEXIST = 0x800
    }

    private enum SIGDN : uint
    {
        FILESYSPATH = 0x80058000
    }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FOS fos);
        void GetOptions(out FOS pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog : IFileDialog
    {
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}

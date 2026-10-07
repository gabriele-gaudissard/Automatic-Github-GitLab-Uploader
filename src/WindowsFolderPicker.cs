using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// The standard Explorer folder dialog provides its own address bar and search box.
public static class WindowsFolderPicker
{
    const int Cancelled = unchecked((int)0x800704C7);

    public static string Show(IWin32Window owner, string initialFolder)
    {
        IFileOpenDialog dialog = CreateDialog(initialFolder);
        try
        {
            int result = dialog.Show(owner.Handle);
            if (result == Cancelled) return null;
            Marshal.ThrowExceptionForHR(result);
            IShellItem item;
            dialog.GetResult(out item);
            try { return GetPath(item); }
            finally { Marshal.ReleaseComObject(item); }
        }
        finally { Marshal.ReleaseComObject(dialog); }
    }

    internal static IFileOpenDialog CreateDialog(string initialFolder)
    {
        var dialog = (IFileOpenDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")));
        try
        {
            uint options;
            dialog.GetOptions(out options);
            // Pick folders, restrict results to filesystem paths, and preserve cwd.
            dialog.SetOptions(options | 0x20 | 0x40 | 0x800 | 0x8);
            dialog.SetTitle(UiText.Get("Select project folder", "Seleziona cartella progetto"));
            dialog.SetOkButtonLabel(UiText.Get("Select folder", "Seleziona cartella"));
            if (Directory.Exists(initialFolder))
            {
                Guid iid = typeof(IShellItem).GUID;
                IShellItem item;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(Path.GetFullPath(initialFolder), IntPtr.Zero, ref iid, out item));
                try { dialog.SetFolder(item); }
                finally { Marshal.ReleaseComObject(item); }
            }
            return dialog;
        }
        catch { Marshal.ReleaseComObject(dialog); throw; }
    }

    internal static string GetPath(IShellItem item)
    {
        IntPtr path;
        item.GetDisplayName(0x80058000, out path); // SIGDN_FILESYSPATH
        try { return Marshal.PtrToStringUni(path); }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IShellItem item);

    // Keep the complete native method order: IModalWindow, IFileDialog, IFileOpenDialog.
    [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out IntPtr name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem item, uint location);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int result);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
        void GetResults(out IntPtr items);
        void GetSelectedItems(out IntPtr items);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint type, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}

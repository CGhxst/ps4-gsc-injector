using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PS4GSCInjector
{
    internal static class FolderPicker
    {
        public static string Show(Window owner)
        {
            IFileOpenDialog dialog = null;
            IShellItem resultItem = null;

            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialogCoClass();
                dialog.SetOptions(Fos.PickFolders | Fos.ForceFileSystem | Fos.PathMustExist);

                IntPtr handle = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).EnsureHandle();
                int result = dialog.Show(handle);
                if (result == unchecked((int)0x800704C7)) return null;
                Marshal.ThrowExceptionForHR(result);

                dialog.GetResult(out resultItem);
                resultItem.GetDisplayName(SigDn.FileSystemPath, out string path);
                return path;
            }
            catch (COMException)
            {
                return null;
            }
            finally
            {
                if (resultItem != null)
                {
                    Marshal.ReleaseComObject(resultItem);
                }

                if (dialog != null)
                {
                    Marshal.ReleaseComObject(dialog);
                }
            }
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialogCoClass
        {
        }

        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr hwndOwner);
            void SetFileTypes();
            void SetFileTypeIndex();
            void GetFileTypeIndex();
            void Advise();
            void Unadvise();
            void SetOptions(Fos fos);
            void GetOptions();
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder();
            void GetCurrentSelection();
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName();
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace();
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid();
            void ClearClientData();
            void SetFilter();
            void GetResults();
            void GetSelectedItems();
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler();
            void GetParent();
            void GetDisplayName(SigDn sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes();
            void Compare();
        }

        [Flags]
        private enum Fos : uint
        {
            ForceFileSystem = 0x40,
            PickFolders = 0x20,
            PathMustExist = 0x800,
        }

        private enum SigDn : uint
        {
            FileSystemPath = 0x80058000,
        }
    }
}

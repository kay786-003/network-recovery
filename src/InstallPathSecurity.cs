using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace NetworkRecovery
{
    internal static class InstallPathSecurity
    {
        // No DELETE, data read/write, or directory enumeration rights are needed.
        // MAXIMUM_ALLOWED can include DELETE and conflict with an existing watcher
        // or process working-directory handle that does not share delete access.
        private const uint SecurityAccess = 0x000E0080; // READ_CONTROL | WRITE_DAC | WRITE_OWNER | FILE_READ_ATTRIBUTES
        private const uint OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;

        internal static void Apply(string path, bool directory, FileSystemSecurity security)
        {
            if (security == null || !security.AreAccessRulesProtected)
                throw new ArgumentException("A protected security descriptor is required.", "security");
            byte[] bytes = security.GetSecurityDescriptorBinaryForm();
            RawSecurityDescriptor descriptor = new RawSecurityDescriptor(bytes, 0);
            if (descriptor.Owner == null || descriptor.DiscretionaryAcl == null ||
                (descriptor.ControlFlags & ControlFlags.DiscretionaryAclPresent) == 0)
                throw new ArgumentException("An owner and non-null DACL are required.", "security");
            // Deny delete sharing so the checked object cannot be renamed/replaced
            // between validation and the security write. Never follow a reparse point.
            using (SafeFileHandle handle = CreateFile(path, SecurityAccess, 3, IntPtr.Zero,
                3, OpenReparsePoint | BackupSemantics, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw Error(Marshal.GetLastWin32Error(), "無法保護安裝路徑", path);
                FileHandleInfo info;
                if (!GetFileInformationByHandle(handle, out info))
                    throw Error(Marshal.GetLastWin32Error(), "無法檢查安裝路徑", path);
                if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                    ((info.Attributes & (uint)FileAttributes.Directory) != 0) != directory ||
                    (!directory && info.Links > 1))
                    throw new IOException("拒絕使用連結或類型不符的安裝路徑：" + path);

                // Set only this opened object. The native security operation does not
                // walk existing children, including private backups or untrusted links.
                // New children still inherit the ACEs in the resulting descriptor.
                int status = NtSetSecurityObject(handle, 0x80000005, bytes);
                if (status < 0)
                    throw Error(unchecked((int)RtlNtStatusToDosError(status)), "無法設定管理員專用的寫入權限", path);
            }
        }

        private static Win32Exception Error(int code, string operation, string path)
        {
            return new Win32Exception(code, operation + "（Windows 錯誤 " + code +
                "：" + new Win32Exception(code).Message + "）：" + path);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileHandleInfo
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileHandleInfo info);
        [DllImport("ntdll.dll")]
        private static extern int NtSetSecurityObject(SafeFileHandle handle, uint information, byte[] descriptor);
        [DllImport("ntdll.dll")]
        private static extern uint RtlNtStatusToDosError(int status);
    }
}

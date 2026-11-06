using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using NetworkRecovery;

internal static class InstallPathSecurityChecks
{
    private static int checks;
    private static readonly SecurityIdentifier User = WindowsIdentity.GetCurrent().User;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string name, string existing, IntPtr security);

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    private static FileSystemSecurity Descriptor(bool directory)
    {
        FileSystemSecurity security = directory ? (FileSystemSecurity)new DirectorySecurity() : new FileSecurity();
        security.SetOwner(User);
        security.SetAccessRuleProtection(true, false);
        InheritanceFlags inherit = directory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
        security.AddAccessRule(new FileSystemAccessRule(User, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }
    private static string Sddl(string path, bool directory)
    {
        FileSystemSecurity security = directory ? (FileSystemSecurity)Directory.GetAccessControl(path) : File.GetAccessControl(path);
        return security.GetSecurityDescriptorSddlForm(AccessControlSections.Access | AccessControlSections.Owner);
    }
    private static void Rejects(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (IOException) { rejected = true; }
        Check(rejected, message);
    }
    public static void Main()
    {
        // Only newly created test objects. Never use Program Files, ProgramData,
        // the application's installer, scheduler, WLAN, or network repair APIs.
        string root = Path.Combine(Path.GetTempPath(), "NetworkRecovery-acl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string target = Path.Combine(root, "installation");
        string child = Path.Combine(target, "existing.txt");
        string privateFolder = Path.Combine(target, "private-backup");
        string newFile = Path.Combine(target, "new.txt");
        string link = Path.Combine(root, "hard-link.txt");
        string junction = Path.Combine(root, "junction");
        try
        {
            Directory.CreateDirectory(target);
            // Explicitly grant this test account full access so MAXIMUM_ALLOWED
            // includes DELETE even when tests run without elevation.
            DirectorySecurity initial = Directory.GetAccessControl(target);
            initial.AddAccessRule(new FileSystemAccessRule(User, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(target, initial);
            File.WriteAllText(child, "demo content");
            Directory.CreateDirectory(privateFolder);
            DirectorySecurity privateSecurity = new DirectorySecurity();
            privateSecurity.SetAccessRuleProtection(true, false);
            privateSecurity.AddAccessRule(new FileSystemAccessRule(User, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(privateFolder, privateSecurity);
            string childBefore = Sddl(child, false), privateBefore = Sddl(privateFolder, true);
            using (SafeFileHandle watcher = CreateFile(target, 1, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
            {
                Check(!watcher.IsInvalid, "test directory watcher opened");
                using (SafeFileHandle old = CreateFile(target, 0x02000000, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
                    Check(old.IsInvalid && Marshal.GetLastWin32Error() == 32, "old MAXIMUM_ALLOWED path reproduces sharing violation");
                InstallPathSecurity.Apply(target, true, Descriptor(true));
                Check(Directory.GetAccessControl(target).AreAccessRulesProtected, "new path writes a protected DACL under watcher");
                Check(Directory.GetAccessControl(target).GetOwner(typeof(SecurityIdentifier)).Equals(User), "owner written to opened object");
            }
            Check(Sddl(child, false) == childBefore, "existing inheriting child ACL is untouched");
            Check(Sddl(privateFolder, true) == privateBefore, "private backup ACL is untouched");
            File.WriteAllText(newFile, "new demo");
            bool inheritsRead = false;
            foreach (FileSystemAccessRule rule in File.GetAccessControl(newFile).GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)) && rule.IsInherited)
                    inheritsRead = (rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute &&
                        (rule.FileSystemRights & (FileSystemRights.WriteData | FileSystemRights.ChangePermissions)) == 0;
            Check(inheritsRead, "new files inherit read-only standard-user permissions");
            using (FileStream reader = new FileStream(child, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                InstallPathSecurity.Apply(child, false, Descriptor(false));
                Check(File.GetAccessControl(child).AreAccessRulesProtected, "file security update coexists with an existing reader");
            }
            Check(File.ReadAllText(child) == "demo content", "security update never writes file contents");
            Rejects(delegate { InstallPathSecurity.Apply(child, true, Descriptor(true)); }, "file masquerading as directory rejected");
            Check(CreateHardLink(link, child, IntPtr.Zero), "isolated hard link created");
            string fileBefore = Sddl(child, false);
            Rejects(delegate { InstallPathSecurity.Apply(link, false, Descriptor(false)); }, "hard links rejected");
            Check(Sddl(child, false) == fileBefore, "hard-link rejection leaves target security unchanged");

            using (Process process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                "/d /c mklink /J \"" + junction + "\" \"" + privateFolder + "\"") {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd(); process.WaitForExit();
                Check(process.ExitCode == 0, "isolated directory junction created");
            }
            Rejects(delegate { InstallPathSecurity.Apply(junction, true, Descriptor(true)); }, "directory reparse points rejected");
            Check(Sddl(privateFolder, true) == privateBefore, "junction rejection leaves private target unchanged");
            bool denied = false;
            try { InstallPathSecurity.Apply(Path.Combine(root, "missing"), true, Descriptor(true)); }
            catch (Win32Exception ex) { denied = ex.NativeErrorCode == 2 && ex.Message.Contains("Windows 錯誤 2"); }
            Check(denied, "open failure reports its original Windows error code");
            bool unprotectedRejected = false;
            try { InstallPathSecurity.Apply(target, true, new DirectorySecurity()); }
            catch (ArgumentException) { unprotectedRejected = true; }
            Check(unprotectedRejected, "unprotected descriptor rejected");
        }
        finally
        {
            // Explicit fixed paths under this generated test root, no recursive deletion.
            if (Directory.Exists(junction)) Directory.Delete(junction);
            if (File.Exists(link)) File.Delete(link);
            if (File.Exists(newFile)) File.Delete(newFile);
            if (File.Exists(child)) File.Delete(child);
            if (Directory.Exists(privateFolder)) Directory.Delete(privateFolder);
            if (Directory.Exists(target)) Directory.Delete(target);
            Directory.Delete(root);
        }
        Console.WriteLine("PASS: " + checks + " isolated install-path security checks; no installed files or tasks changed.");
    }
}

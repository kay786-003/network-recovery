using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NetworkRecovery;

internal static class IconResourceChecks
{
    private static int checks;
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    private delegate bool ResourceNameCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, ResourceNameCallback callback, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("user32.dll")] private static extern IntPtr CreateIconFromResourceEx(byte[] bytes, uint size,
        [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    [STAThread] private static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); Environment.ExitCode = 1; }
    }
    private static void Run(string[] args)
    {
        Assembly app = typeof(MainForm).Assembly;
        string[] names = { "App", "Enabled", "Disabled" };
        List<byte[]> icons = new List<byte[]>();
        foreach (string name in names)
        {
            using (Stream resource = app.GetManifestResourceStream("NetworkRecovery.Icons." + name + ".ico"))
            {
                Check(resource != null, "icon resource embedded in single EXE");
                byte[] bytes = new byte[(int)resource.Length];
                int read = 0;
                while (read < bytes.Length) { int count = resource.Read(bytes, read, bytes.Length - read); if (count == 0) break; read += count; }
                Check(read == bytes.Length && BitConverter.ToUInt16(bytes, 0) == 0 && BitConverter.ToUInt16(bytes, 2) == 1, "valid ICO container");
                Check(BitConverter.ToUInt16(bytes, 4) == Sizes.Length, "all nine Windows icon frames are present");
                for (int i = 0; i < Sizes.Length; i++)
                {
                    int width = bytes[6 + 16 * i] == 0 ? 256 : bytes[6 + 16 * i];
                    Check(width == Sizes[i], "ICO frame dimensions match advertised sizes");
                    if (Sizes[i] == 256)
                    {
                        // Framework Icon(Stream,256,256) may choose the 128 frame.
                        // Validate the shell's 256 frame directly with the Windows API.
                        uint length = BitConverter.ToUInt32(bytes, 6 + 16 * i + 8);
                        uint offset = BitConverter.ToUInt32(bytes, 6 + 16 * i + 12);
                        byte[] frame = new byte[length]; Array.Copy(bytes, offset, frame, 0, length);
                        IntPtr handle = CreateIconFromResourceEx(frame, length, true, 0x00030000, 256, 256, 0);
                        Check(handle != IntPtr.Zero, "Windows decodes shell's 256-pixel frame");
                        try
                        {
                            using (Icon large = Icon.FromHandle(handle))
                            using (Bitmap bitmap = large.ToBitmap())
                            {
                                Check(large.Width == 256 && large.Height == 256, "shell icon retains full 256-pixel dimensions");
                                Check(bitmap.GetPixel(0, 0).A == 0, "large shell icon preserves transparent margin");
                            }
                        }
                        finally { DestroyIcon(handle); }
                        continue;
                    }
                    using (MemoryStream memory = new MemoryStream(bytes))
                    using (Icon icon = new Icon(memory, Sizes[i], Sizes[i]))
                    using (Bitmap bitmap = icon.ToBitmap())
                    {
                        Check(icon.Width == Sizes[i] && icon.Height == Sizes[i], "Windows decodes requested icon size: " + name + " requested=" + Sizes[i] + " actual=" + icon.Size);
                        Check(bitmap.GetPixel(0, 0).A == 0, "ICO preserves transparent margin: " + name + " " + Sizes[i] + " alpha=" + bitmap.GetPixel(0, 0).A);
                    }
                }
                icons.Add(bytes);
            }
        }
        Check(Convert.ToBase64String(icons[0]) != Convert.ToBase64String(icons[1]) &&
            Convert.ToBase64String(icons[1]) != Convert.ToBase64String(icons[2]), "application/enabled/disabled resources differ");

        Type provider = app.GetType("NetworkRecovery.AppIcons", true);
        Icon enabled = (Icon)provider.GetField("Enabled", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Icon disabled = (Icon)provider.GetField("Disabled", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Icon application = (Icon)provider.GetField("Application", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        MethodInfo statusIcon = provider.GetMethod("ForStatus", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (string status in new[] { "正常", "修復中", "修復後觀察中", "無外網，等待修復", "等待目標 Wi-Fi（持續掃描）", "監控已啟用（等待執行）" })
            Check(Object.ReferenceEquals(statusIcon.Invoke(null, new object[] { status }), enabled), "enabled monitoring stays green during an outage or repair");
        foreach (string status in new[] { "已暫停（剩餘 60 分鐘）", "監控已停止", "尚未安裝" })
            Check(Object.ReferenceEquals(statusIcon.Invoke(null, new object[] { status }), disabled), "paused/stopped monitoring uses disabled icon");
        Check(Object.ReferenceEquals(statusIcon.Invoke(null, new object[] { "無法讀取排程狀態" }), application), "unknown task state uses neutral app icon");
        using (MainForm form = new MainForm(true)) Check(Object.ReferenceEquals(form.Icon, application), "main window uses application icon");
        using (MonitorToolsForm form = new MonitorToolsForm(true)) Check(Object.ReferenceEquals(form.Icon, application), "tools window uses application icon");

        IntPtr module = LoadLibraryEx(app.Location, IntPtr.Zero, 2);
        Check(module != IntPtr.Zero, "release EXE can be inspected as data without executing it");
        try
        {
            bool found = false;
            ResourceNameCallback callback = delegate(IntPtr image, IntPtr type, IntPtr name, IntPtr parameter) {
                IntPtr resource = FindResource(image, name, type);
                uint size = SizeofResource(image, resource);
                if (size >= 6)
                {
                    byte[] header = new byte[6];
                    Marshal.Copy(LockResource(LoadResource(image, resource)), header, 0, 6);
                    found |= BitConverter.ToUInt16(header, 2) == 1 && BitConverter.ToUInt16(header, 4) == Sizes.Length;
                }
                return true;
            };
            Check(EnumResourceNames(module, new IntPtr(14), callback, IntPtr.Zero) && found, "EXE shell icon embeds all generated frames");
            GC.KeepAlive(callback);
        }
        finally { FreeLibrary(module); }

        string preview = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "NetworkRecovery-icons-preview.png");
        using (Bitmap canvas = new Bitmap(720, 340))
        using (Graphics graphics = Graphics.FromImage(canvas))
        using (Font font = new Font("Segoe UI", 11))
        {
            graphics.Clear(Color.FromArgb(245, 247, 251));
            graphics.FillRectangle(Brushes.Black, 0, 170, 720, 170);
            for (int i = 0; i < icons.Count; i++)
            {
                graphics.DrawString(names[i], font, Brushes.Black, 20 + i * 240, 8);
                using (MemoryStream memory = new MemoryStream(icons[i]))
                using (Icon icon = new Icon(memory, 128, 128))
                { graphics.DrawIcon(icon, 20 + i * 240, 35); graphics.DrawIcon(icon, 20 + i * 240, 185); }
                int y = 45;
                foreach (int size in new[] { 16, 32, 48 })
                    using (MemoryStream memory = new MemoryStream(icons[i]))
                    using (Icon icon = new Icon(memory, size, size))
                    { graphics.DrawIcon(icon, 175 + i * 240, y); y += size + 9; }
            }
            canvas.Save(preview, System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine("Demo icon preview: " + preview);
        Console.WriteLine("PASS: " + checks + " embedded ICO, shell icon, transparency and status checks; no network or scheduler changes.");
    }
}

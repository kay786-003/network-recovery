using System;
using System.Drawing;
using System.IO;

namespace NetworkRecovery
{
    internal static class AppIcons
    {
        internal static readonly Icon Application = Load("App");
        internal static readonly Icon Enabled = Load("Enabled");
        internal static readonly Icon Disabled = Load("Disabled");

        private static Icon Load(string name)
        {
            using (Stream stream = typeof(AppIcons).Assembly.GetManifestResourceStream("NetworkRecovery.Icons." + name + ".ico"))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded application icon.");
                using (Icon icon = new Icon(stream, 32, 32)) return (Icon)icon.Clone();
            }
        }

        internal static Icon ForStatus(string status)
        {
            if (String.IsNullOrEmpty(status)) return Application;
            if (status.StartsWith("已暫停", StringComparison.Ordinal) || status == "監控已停止" || status == "尚未安裝")
                return Disabled;
            if (status.StartsWith("監控已啟用", StringComparison.Ordinal) || status == "正常" || status == "修復中" ||
                status == "修復後觀察中" || status == "無外網，等待修復" || status == "等待目標 Wi-Fi（持續掃描）" ||
                status == "等待操作完成或檢查控制狀態") return Enabled;
            return Application;
        }
    }
}

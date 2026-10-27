using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NetworkRecovery;
class UiLayoutChecks
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static int passed;
    static Control Field(MainForm form, string name)
    { return (Control)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form); }
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); passed++; }
    static void CheckFooter(MainForm form)
    {
        Control start = Field(form, "start"), stop = Field(form, "stop");
        foreach (Control button in new[] { start, stop })
        {
            Size text = TextRenderer.MeasureText(button.Text, button.Font);
            Check(button.Width >= text.Width && button.Height >= text.Height + 8, "Button text is clipped.");
            foreach (Control sibling in button.Parent.Controls)
                if (sibling != button && sibling.Visible)
                    Check(!button.Bounds.IntersectsWith(sibling.Bounds), "Footer control covers a button.");
            form.ScrollControlIntoView(button); Application.DoEvents();
            Rectangle visible = button.RectangleToScreen(button.ClientRectangle);
            for (Control parent = button.Parent; parent != null; parent = parent.Parent)
                Check(parent.RectangleToScreen(parent.ClientRectangle).Contains(visible), "Button is clipped by ancestor.");
        }
    }
    [STAThread] static void Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (MainForm form = new MainForm(true))
        {
            form.Show(); Application.DoEvents(); CheckFooter(form);
            Field(form, "status").Text = "●  尚未安裝";
            Field(form, "notice").Text = new string('測', 160);
            ((CheckBox)Field(form, "replacePassword")).Checked = true;
            Application.DoEvents(); CheckFooter(form);
            form.Size = new Size(1280, 960); Application.DoEvents(); CheckFooter(form);
            form.WindowState = FormWindowState.Maximized; Application.DoEvents(); CheckFooter(form);
            form.WindowState = FormWindowState.Normal; Application.DoEvents(); CheckFooter(form);
            form.Close();
        }
        Console.WriteLine("Passed " + passed + " UI layout checks using demo data; no network or scheduler changes.");
    }
}

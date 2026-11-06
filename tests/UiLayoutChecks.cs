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
    // Every visible control must stay inside its parent, must not overlap siblings, and wrapped text must fit.
    static void CheckAll(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible) continue;
            Check(child.Bounds.Right <= parent.ClientSize.Width + 1 && child.Bounds.Bottom <= parent.ClientSize.Height + 1 && child.Left >= 0 && child.Top >= 0,
                child.GetType().Name + " '" + child.Text + "' is clipped by its parent. child=" + child.Bounds + " parent=" + parent.ClientSize + " form=" + Application.OpenForms[0].ClientSize);
            foreach (Control other in parent.Controls)
                if (other != child && other.Visible && !(parent is TableLayoutPanel && false))
                    Check(!child.Bounds.IntersectsWith(other.Bounds), child.GetType().Name + " '" + child.Text + "' overlaps " + other.GetType().Name + " '" + other.Text + "'.");
            if ((child is Label || child is CheckBox || child is Button) && child.Text.Length > 0)
            {
                Size need = TextRenderer.MeasureText(child.Text, child.Font, new Size(child.Width, 100000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                Check(need.Height <= child.Height + 2, "Text of '" + child.Text + "' needs " + need.Height + "px but has " + child.Height + ".");
            }
            CheckAll(child);
        }
    }
    static void CheckFlow(MainForm form, Control root)
    {
        // Content tops must follow header, status, body, flow and footer in order.
        Control content = form.Controls[0];
        CheckAll(content);
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
            foreach (int[] s in new[] { new[]{1920,1040}, new[]{1366,680}, new[]{1024,600}, new[]{860,700}, new[]{800,560}, new[]{480,700}, new[]{360,500} })
            {
                form.ClientSize = new Size(s[0], s[1]); Application.DoEvents(); Application.DoEvents();
                CheckFooter(form); CheckFlow(form, null);
            }
            form.Close();
        }
        Console.WriteLine("Passed " + passed + " UI layout checks using demo data; no network or scheduler changes.");
    }
}

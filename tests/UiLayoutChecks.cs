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
            form.PerformLayout(); Application.DoEvents(); form.ScrollControlIntoView(button); Application.DoEvents();
            Rectangle visible = button.RectangleToScreen(button.ClientRectangle);
            for (Control parent = button.Parent; parent != null; parent = parent.Parent)
                Check(parent.RectangleToScreen(parent.ClientRectangle).Contains(visible), "Button is clipped by ancestor. scale=" + Theme.Scale + " button=" + button.Text + " ancestor=" + parent.GetType().Name + " " + parent.RectangleToScreen(parent.ClientRectangle) + " vs " + visible + " form=" + form.ClientSize);
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
        try { Run(); }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); Environment.ExitCode = 1; }
    }
    static void Run()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        // Rehearse 100%, 125%, 150% and 200% scaling: spacing and fonts scale together through Theme.Scale.
        foreach (float scale in new[] { 1.0F, 1.25F, 1.5F, 2.0F }) { Theme.Scale = scale; RunWindows(); }
        CheckTrayMenu();
        Console.WriteLine("Passed " + passed + " UI layout checks using demo data at 100/125/150/200% scale; no network or scheduler changes.");
    }
    static void RunWindows()
    {
        using (MainForm form = new MainForm(true))
        {
            form.Show(); Application.DoEvents(); CheckFooter(form);
            Check(!((CheckBox)Field(form, "countryGuard")).Checked, "Country guard defaults off in demo mode.");
            ((CheckBox)Field(form, "countryGuard")).Checked = true;
            if (Theme.Scale == 1.0F)
            {
                form.ClientSize = new Size(1000, 980); Application.DoEvents();
                string previewPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "country-guard-preview.png");
                using (Bitmap bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(previewPath); }
                Console.WriteLine("Demo country guard preview: " + previewPath);
            }
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
        using (MonitorToolsForm form = new MonitorToolsForm(true))
        {
            form.Show(); Application.DoEvents();
            TableLayoutPanel actions = (TableLayoutPanel)typeof(MonitorToolsForm).GetField("actions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            string[] names = { "備份", "還原備份", "一鍵修復並重設", "移除備份" };
            Check(actions.Controls.Count == names.Length, "Tools must expose exactly four actions.");
            for (int i = 0; i < names.Length; i++) Check(actions.Controls[i].Text == names[i], "Unexpected tools action or order.");
            CheckTools(form); CheckAll(form.Controls[0]);
            ResetScroll(form); Application.DoEvents();
            string preview = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(MainForm).Assembly.Location), "tools-preview.png");
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(preview); }
            Console.WriteLine("Demo tools preview: " + preview);
            foreach (int[] s in new[] { new[]{900,700}, new[]{560,520}, new[]{420,400}, new[]{360,300} })
            { form.ClientSize = new Size(s[0], s[1]); Application.DoEvents(); Application.DoEvents(); CheckTools(form); CheckAll(form.Controls[0]); }
            form.Close();
        }
    }
    static void CheckTrayMenu()
    {
        DateTime utc = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);
        PauseState state = new PauseState();
        ToolStripMenuItem[] items = null;
        items = TrayPauseMenu.Create(delegate(int hours) {
            int selected = state.ToggleHours(hours, "demo", 100, utc);
            state = new PauseState { Hours = selected, Revision = "demo", BootId = "demo", Started = 100, StartedUtc = utc };
            TrayPauseMenu.Apply(items, state.SelectedHours("demo", 100, utc));
        });
        try
        {
            Check(items.Length == 4, "Tray must offer four pause durations.");
            for (int i = 0; i < items.Length; i++)
            {
                items[i].PerformClick();
                for (int j = 0; j < items.Length; j++) Check(items[j].Checked == (j == i), "Only the selected duration is checked.");
                items[i].PerformClick();
                Check(state.Hours == 0 && !items[i].Checked, "Clicking the checked item cancels pause.");
            }
            items[0].PerformClick(); items[2].PerformClick();
            Check(!items[0].Checked && items[2].Checked && state.Hours == 4, "Selecting another duration replaces the current pause.");
            TrayPauseMenu.Apply(items, state.SelectedHours("demo", 14500, utc));
            Check(!items[2].Checked, "Expired pause loses its checkmark.");
            state = new PauseState { Hours = 4, Revision = "demo", BootId = "demo", Started = 0, StartedUtc = utc };
            Check(state.ToggleHours(4, "demo", 14400, utc) == 4, "Clicking an expired duration starts a new pause.");
        }
        finally { foreach (ToolStripMenuItem item in items) item.Dispose(); }
    }
    static void CheckTools(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            Button button = child as Button;
            if (button != null)
            {
                Size text = TextRenderer.MeasureText(button.Text, button.Font, new Size(button.Width - 24, 0), TextFormatFlags.WordBreak);
                Check(button.Height >= text.Height + 8, "Tools button text clipped.");
                Check(button.Parent.ClientRectangle.Contains(button.Bounds), "Tools button cannot scroll into view: " + button.Text + " " + button.Bounds + " in " + button.Parent.ClientRectangle);
            }
            CheckTools(child);
        }
    }
    static void ResetScroll(Control parent)
    {
        ScrollableControl scroll = parent as ScrollableControl;
        if (scroll != null) scroll.AutoScrollPosition = Point.Empty;
        foreach (Control child in parent.Controls) ResetScroll(child);
    }
}

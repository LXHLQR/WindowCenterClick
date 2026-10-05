using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new CenterForm());
    }
}

internal sealed class CenterForm : Form
{
    private readonly Button select = new Button();
    private readonly CheckBox fit = new CheckBox();
    private readonly CheckBox adjust = new CheckBox();
    private readonly ComboBox ratio = new ComboBox();
    private readonly ComboBox scale = new ComboBox();
    private readonly NumericUpDown ratioWidth = new NumericUpDown();
    private readonly NumericUpDown ratioHeight = new NumericUpDown();
    private readonly NumericUpDown percent = new NumericUpDown();
    private readonly Label status = new Label();
    private readonly Timer timer = new Timer();
    private readonly Native.HookProc mouseProc;
    private IntPtr hook;
    private IntPtr picked;
    private int heldButton;
    private bool selecting;
    private bool escapeRegistered;
    private DateTime selectionStarted;
    private const int EscapeHotkey = 731;

    public CenterForm()
    {
        Text = "窗口居中 · 比例与大小";
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(680, 640);
        MinimumSize = new Size(680, 590);
        MaximizeBox = false;
        Icon = SystemIcons.Application;
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1,
            RowCount = 9, AutoScroll = true
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label {
            Text = "选择比例、大小，再点一下窗口", AutoSize = true,
            Font = new Font(Font.FontFamily, 17F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 14)
        });
        layout.Controls.Add(new Label {
            Text = "点击下方按钮后，本工具会暂时隐藏。\r\n用鼠标左键点击已打开的应用窗口；右键或 Esc 取消。",
            AutoSize = true, Margin = new Padding(0, 0, 0, 16)
        });
        adjust.Text = "调整长宽比与大小（取消勾选则仅居中）";
        adjust.Checked = true;
        adjust.AutoSize = true;
        adjust.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(adjust);

        ratio.DropDownStyle = ComboBoxStyle.DropDownList;
        ratio.Width = 175;
        ratio.Items.AddRange(new object[] { "显示器长宽比", "16:9", "4:3", "1:1", "自定义长宽比" });
        ratio.SelectedIndex = 0;
        ConfigureNumber(ratioWidth, 0.01M, 10000M, 16M, 2);
        ConfigureNumber(ratioHeight, 0.01M, 10000M, 9M, 2);
        layout.Controls.Add(Row(new Label { Text = "长宽比", AutoSize = true, Padding = new Padding(0, 6, 12, 0) },
            ratio, ratioWidth, new Label { Text = ":", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, ratioHeight));

        scale.DropDownStyle = ComboBoxStyle.DropDownList;
        scale.Width = 175;
        scale.Items.AddRange(new object[] { "75%", "50%", "25%", "100%", "自定义百分比" });
        scale.SelectedIndex = 0;
        ConfigureNumber(percent, 1M, 100M, 75M, 2);
        percent.Increment = 1M;
        layout.Controls.Add(Row(new Label { Text = "大　小", AutoSize = true, Padding = new Padding(0, 6, 12, 0) },
            scale, percent, new Label { Text = "%", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }));
        layout.Controls.Add(new Label {
            Text = "100% = 所选比例在可用区域内能容纳的最大尺寸。\r\n50% = 该尺寸的宽、高各取一半（面积为 25%）。\r\n比例按整个可见窗口计算，包含标题栏；不含阴影和隐藏边框。",
            AutoSize = true, Margin = new Padding(0, 0, 0, 14), ForeColor = SystemColors.GrayText
        });

        select.Text = "选择窗口并应用";
        select.AutoSize = true;
        select.Padding = new Padding(20, 8, 20, 8);
        select.Margin = new Padding(0, 0, 0, 12);
        select.Click += delegate { StartSelection(); };
        layout.Controls.Add(select);
        fit.Text = "仅居中时：窗口过大则尝试缩小到可用区域";
        fit.AutoSize = true;
        fit.Checked = true;
        fit.Margin = new Padding(0, 0, 0, 14);
        layout.Controls.Add(fit);
        adjust.CheckedChanged += delegate { UpdateOptions(); };
        ratio.SelectedIndexChanged += delegate { UpdateOptions(); };
        scale.SelectedIndexChanged += delegate { UpdateOptions(); };
        UpdateOptions();
        status.Text = "就绪。选择设置后点击按钮，再点击目标窗口。";
        status.Dock = DockStyle.Fill;
        status.MinimumSize = new Size(0, 120);
        status.AutoEllipsis = true;
        layout.Controls.Add(status);
        Controls.Add(layout);
        mouseProc = MouseHook;
        timer.Interval = 100;
        timer.Tick += delegate {
            if (selecting && (DateTime.UtcNow - selectionStarted).TotalSeconds > 30)
                FinishSelection("选择已超时，请重试。", false);
        };
    }

    private static void ConfigureNumber(NumericUpDown control, decimal min, decimal max, decimal value, int decimals)
    {
        control.Minimum = min;
        control.Maximum = max;
        control.DecimalPlaces = decimals;
        control.Value = value;
        control.Width = 90;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true,
            Margin = new Padding(0, 0, 0, 12) };
        row.Controls.AddRange(controls);
        return row;
    }

    private void UpdateOptions()
    {
        ratio.Enabled = scale.Enabled = adjust.Checked;
        ratioWidth.Enabled = ratioHeight.Enabled = adjust.Checked && ratio.SelectedIndex == 4;
        percent.Enabled = adjust.Checked && scale.SelectedIndex == 4;
        fit.Enabled = !adjust.Checked;
    }

    private SizeOptions ReadOptions()
    {
        double selectedRatio = 0; // 0 = selected monitor's full display ratio, not its work area ratio
        if (ratio.SelectedIndex == 1) selectedRatio = 16.0 / 9.0;
        else if (ratio.SelectedIndex == 2) selectedRatio = 4.0 / 3.0;
        else if (ratio.SelectedIndex == 3) selectedRatio = 1;
        else if (ratio.SelectedIndex == 4) selectedRatio = (double)ratioWidth.Value / (double)ratioHeight.Value;
        double[] presets = { 75, 50, 25, 100 };
        double selectedPercent = scale.SelectedIndex == 4 ? (double)percent.Value : presets[scale.SelectedIndex];
        return new SizeOptions { Adjust = adjust.Checked, Ratio = selectedRatio, Scale = selectedPercent / 100.0 };
    }

    private void StartSelection()
    {
        if (selecting) return;
        picked = IntPtr.Zero;
        heldButton = 0;
        hook = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) {
            status.Text = "无法开始鼠标选择：" + new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return;
        }
        escapeRegistered = Native.RegisterHotKey(Handle, EscapeHotkey, 0, 0x1B);
        selecting = true;
        selectionStarted = DateTime.UtcNow;
        select.Enabled = false;
        Hide();
        timer.Start();
    }

    // Only capture the selection gesture. All window manipulation is deferred to the UI loop.
    private IntPtr MouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code < 0 || !selecting)
            return Native.CallNextHookEx(hook, code, message, data);
        int msg = message.ToInt32();
        if (msg == 0x201 || msg == 0x204) { // left/right down
            if (heldButton != 0) return Native.CallNextHookEx(hook, code, message, data);
            heldButton = msg == 0x201 ? 1 : 2;
            if (heldButton == 1) {
                Native.MouseData input = (Native.MouseData)Marshal.PtrToStructure(data, typeof(Native.MouseData));
                picked = Native.GetAncestor(Native.WindowFromPoint(input.Point), 2); // GA_ROOT
            }
            return new IntPtr(1);
        }
        if ((msg == 0x202 && heldButton == 1) || (msg == 0x205 && heldButton == 2)) {
            bool cancel = heldButton == 2;
            selecting = false;
            BeginInvoke(new Action(delegate {
                if (cancel) FinishSelection("已取消选择。", false);
                else MovePickedWindow();
            }));
            return new IntPtr(1);
        }
        return Native.CallNextHookEx(hook, code, message, data);
    }

    private void StopSelection()
    {
        selecting = false;
        timer.Stop();
        if (hook != IntPtr.Zero) {
            Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
        if (escapeRegistered) Native.UnregisterHotKey(Handle, EscapeHotkey);
        escapeRegistered = false;
        heldButton = 0;
    }

    private void FinishSelection(string message, bool success)
    {
        StopSelection();
        if (IsDisposed || Disposing) return;
        status.Text = message;
        status.ForeColor = success ? Color.DarkGreen : SystemColors.ControlText;
        select.Enabled = true;
        Show();
        Activate();
    }

    private async void MovePickedWindow()
    {
        StopSelection();
        try {
            MoveResult result = await WindowMover.CenterAsync(picked, fit.Checked, ReadOptions());
            FinishSelection(result.Message, result.Exact);
        }
        catch (Exception ex) {
            FinishSelection(ex.Message, false);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == EscapeHotkey && selecting) {
            FinishSelection("已取消选择。", false);
            return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        StopSelection();
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class SizeOptions
{
    public bool Adjust;
    public double Ratio;
    public double Scale;

    internal Size Calculate(Native.Rect monitor, Native.Rect work)
    {
        double aspect = Ratio == 0 ? (double)monitor.Width / monitor.Height : Ratio;
        if (work.Width <= 0 || work.Height <= 0 || monitor.Width <= 0 || monitor.Height <= 0
            || double.IsNaN(aspect) || double.IsInfinity(aspect) || aspect <= 0 || Scale <= 0 || Scale > 1)
            throw new InvalidOperationException("显示器尺寸、长宽比或百分比无效。");
        double maxHeight = Math.Min(work.Height, work.Width / aspect);
        double rawWidth = maxHeight * aspect * Scale;
        double rawHeight = maxHeight * Scale;
        if (rawWidth < 1 || rawHeight < 1)
            throw new InvalidOperationException("该比例和大小产生了不足 1 像素的尺寸，请增大百分比或调整长宽比。");
        return new Size(Math.Min(work.Width, (int)Math.Round(rawWidth, MidpointRounding.AwayFromZero)),
            Math.Min(work.Height, (int)Math.Round(rawHeight, MidpointRounding.AwayFromZero)));
    }
}

internal sealed class MoveResult
{
    public string Message;
    public bool Exact;
}

internal static class WindowMover
{
    private static Native.Rect VisibleRect(IntPtr hwnd, Native.Rect outer)
    {
        Native.Rect visible;
        if (Native.DwmGetWindowAttribute(hwnd, 9, out visible, Marshal.SizeOf(typeof(Native.Rect))) == 0
            && visible.Width > 0 && visible.Height > 0) return visible;
        return outer;
    }

    private static void EnsureWindow(IntPtr hwnd, uint expectedPid)
    {
        uint pid;
        Native.GetWindowThreadProcessId(hwnd, out pid);
        if (!Native.IsWindow(hwnd) || pid != expectedPid || !Native.IsWindowVisible(hwnd))
            throw new InvalidOperationException("目标窗口已关闭或不可见，请重新选择。");
    }

    internal static async Task<MoveResult> CenterAsync(IntPtr hwnd, bool fit, SizeOptions options)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd))
            throw new InvalidOperationException("未找到应用窗口，请点击已打开窗口的标题栏或内容区域。");
        uint pid;
        Native.GetWindowThreadProcessId(hwnd, out pid);
        if (pid == (uint)Process.GetCurrentProcess().Id)
            throw new InvalidOperationException("请选择其他应用窗口。");
        var className = new StringBuilder(256);
        Native.GetClassName(hwnd, className, className.Capacity);
        string cls = className.ToString();
        if (hwnd == Native.GetDesktopWindow() || hwnd == Native.GetShellWindow()
            || cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd"
            || cls == "Shell_SecondaryTrayWnd" || cls == "#32768")
            throw new InvalidOperationException("桌面、任务栏和菜单不能居中，请点击应用窗口。");
        EnsureWindow(hwnd, pid);
        IntPtr monitor = Native.MonitorFromWindow(hwnd, 2);
        var info = new Native.MonitorInfo();
        info.Size = Marshal.SizeOf(typeof(Native.MonitorInfo));
        if (monitor == IntPtr.Zero || !Native.GetMonitorInfo(monitor, ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取显示器可用区域。");
        Native.Rect before;
        if (!Native.GetWindowRect(hwnd, out before))
            throw new InvalidOperationException("无法读取目标窗口位置。");
        long style = Native.GetStyle(hwnd);
        if (options.Adjust && (style & 0x00040000L) == 0)
            throw new InvalidOperationException("该窗口未声明支持调整大小。请取消勾选“调整长宽比与大小”，仅将其居中。");
        if ((style & 0x00C00000L) == 0 && before.Left <= info.Monitor.Left
            && before.Top <= info.Monitor.Top && before.Right >= info.Monitor.Right
            && before.Bottom >= info.Monitor.Bottom)
            throw new InvalidOperationException("检测到无边框全屏窗口，请先退出全屏后再居中。");

        bool restored = Native.IsZoomed(hwnd) || Native.IsIconic(hwnd);
        if (restored) {
            if (!Native.ShowWindowAsync(hwnd, 9)) // SW_RESTORE
                throw new InvalidOperationException("无法还原窗口。若目标以管理员身份运行，请以管理员身份运行本工具。");
            bool ready = false;
            for (int i = 0; i < 25; i++) {
                await Task.Delay(60);
                EnsureWindow(hwnd, pid);
                if (!Native.IsZoomed(hwnd) && !Native.IsIconic(hwnd)) { ready = true; break; }
            }
            if (!ready) throw new InvalidOperationException("窗口未能及时还原，请重试。");
            await Task.Delay(180); // allow restore animation to settle
        }

        // Keep the originally selected monitor even if restoring changes the window's location.
        Native.Rect work = info.Work;
        Size desired = options.Adjust ? options.Calculate(info.Monitor, work) : Size.Empty;
        bool resized = false;
        bool success = false;
        bool exceeds = false;
        bool sizeMatched = !options.Adjust;
        Native.Rect actual = before;
        for (int attempt = 0; attempt < 5; attempt++) {
            EnsureWindow(hwnd, pid);
            Native.Rect outer;
            if (!Native.GetWindowRect(hwnd, out outer))
                throw new InvalidOperationException("无法读取目标窗口位置。");
            Native.Rect visible = VisibleRect(hwnd, outer);
            int width = outer.Width;
            int height = outer.Height;
            int visibleWidth = visible.Width;
            int visibleHeight = visible.Height;
            bool resizeNow = !options.Adjust && attempt < 2 && fit && (style & 0x00040000L) != 0
                && (visibleWidth > work.Width || visibleHeight > work.Height);
            if (options.Adjust && attempt < 3) {
                resizeNow = true;
                // Convert requested visible dimensions into the outer dimensions expected by Win32.
                width = desired.Width + outer.Width - visible.Width;
                height = desired.Height + outer.Height - visible.Height;
                visibleWidth = desired.Width;
                visibleHeight = desired.Height;
                resized = true;
            }
            else if (resizeNow) {
                width -= Math.Max(0, visibleWidth - work.Width);
                height -= Math.Max(0, visibleHeight - work.Height);
                visibleWidth = Math.Min(visibleWidth, work.Width);
                visibleHeight = Math.Min(visibleHeight, work.Height);
                resized = true;
            }
            // DWM excludes invisible resize borders; SetWindowPos positions the outer bounds.
            int x = work.Left + (work.Width - visibleWidth) / 2 - (visible.Left - outer.Left);
            int y = work.Top + (work.Height - visibleHeight) / 2 - (visible.Top - outer.Top);
            uint flags = 0x0004 | 0x0010 | 0x0200 | 0x4000; // NOZORDER, NOACTIVATE, NOOWNERZORDER, ASYNC
            if (!resizeNow) flags |= 0x0001; // NOSIZE
            if (!Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, Math.Max(1, width), Math.Max(1, height), flags))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "窗口移动失败。若目标以管理员身份运行，请以管理员身份运行本工具。");
            await Task.Delay(160);
            EnsureWindow(hwnd, pid);
            if (!Native.GetWindowRect(hwnd, out outer)) continue;
            visible = VisibleRect(hwnd, outer);
            actual = visible;
            sizeMatched = !options.Adjust || (Math.Abs(visible.Width - desired.Width) <= 1
                && Math.Abs(visible.Height - desired.Height) <= 1);
            exceeds = visible.Width > work.Width + 2 || visible.Height > work.Height + 2;
            bool centered = Math.Abs((long)visible.Left + visible.Right - work.Left - work.Right) <= 4
                && Math.Abs((long)visible.Top + visible.Bottom - work.Top - work.Bottom) <= 4;
            // Last two passes center the actual accepted size if the app enforces a minimum size.
            if (centered && (sizeMatched || attempt >= 3)) { success = true; break; }
        }
        if (!success)
            throw new InvalidOperationException("已尝试移动，但未确认窗口到达中央。目标可能限制移动、处于全屏、未响应或权限不足。");
        var title = new StringBuilder(512);
        Native.GetWindowText(hwnd, title, title.Capacity);
        string process = "PID " + pid;
        try { using (Process p = Process.GetProcessById((int)pid)) process = p.ProcessName; }
        catch (Exception) { }
        string dimensions = options.Adjust ? "\r\n目标 " + desired.Width + " × " + desired.Height
            + " px；实际 " + actual.Width + " × " + actual.Height + " px。" : "";
        string message = (sizeMatched ? "已居中：" : "已居中，但尺寸未完全应用：")
            + process + (title.Length > 0 ? " · " + title : "") + dimensions
            + (!sizeMatched ? "\r\n应用可能限制最小尺寸或长宽比；已按实际尺寸居中。" : "")
            + (restored ? "\r\n已从最大化状态还原。" : "")
            + (exceeds ? "\r\n窗口仍大于可用区域，部分内容可能超出屏幕或覆盖任务栏。"
                : resized && !options.Adjust ? "\r\n已尝试缩小窗口以适应工作区。" : "");
        return new MoveResult { Message = message, Exact = sizeMatched && !exceeds };
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseData { public Point Point; public uint Mouse, Flags, Time; public UIntPtr ExtraInfo; }
    internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLong64(IntPtr hwnd, int index);
    internal static long GetStyle(IntPtr hwnd) {
        return IntPtr.Size == 8 ? GetWindowLong64(hwnd, -16).ToInt64() : GetWindowLong32(hwnd, -16);
    }
}

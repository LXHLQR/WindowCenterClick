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
        Text = "窗口居中 · 鼠标选择";
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(580, 335);
        MinimumSize = new Size(500, 360);
        MaximizeBox = false;
        Icon = SystemIcons.Application;
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1,
            RowCount = 5
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label {
            Text = "点一下窗口，让它回到中央", AutoSize = true,
            Font = new Font(Font.FontFamily, 17F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 14)
        });
        layout.Controls.Add(new Label {
            Text = "点击下方按钮后，本工具会暂时隐藏。\r\n用鼠标左键点击已打开的应用窗口；右键或 Esc 取消。",
            AutoSize = true, Margin = new Padding(0, 0, 0, 16)
        });
        select.Text = "选择窗口并居中";
        select.AutoSize = true;
        select.Padding = new Padding(20, 8, 20, 8);
        select.Margin = new Padding(0, 0, 0, 12);
        select.Click += delegate { StartSelection(); };
        layout.Controls.Add(select);
        fit.Text = "窗口过大时，尝试缩小到可用区域";
        fit.AutoSize = true;
        fit.Checked = true;
        fit.Margin = new Padding(0, 0, 0, 14);
        layout.Controls.Add(fit);
        status.Text = "就绪。按目标窗口所在显示器居中，避开任务栏。";
        status.Dock = DockStyle.Fill;
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
            string result = await WindowMover.CenterAsync(picked, fit.Checked);
            FinishSelection(result, true);
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

    internal static async Task<string> CenterAsync(IntPtr hwnd, bool fit)
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
        bool resized = false;
        bool success = false;
        bool exceeds = false;
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
            bool resizeNow = attempt < 2 && fit && (style & 0x00040000L) != 0
                && (visibleWidth > work.Width || visibleHeight > work.Height);
            if (resizeNow) {
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
            exceeds = visible.Width > work.Width + 2 || visible.Height > work.Height + 2;
            bool centered = Math.Abs((long)visible.Left + visible.Right - work.Left - work.Right) <= 4
                && Math.Abs((long)visible.Top + visible.Bottom - work.Top - work.Bottom) <= 4;
            if (centered) { success = true; break; }
        }
        if (!success)
            throw new InvalidOperationException("已尝试移动，但未确认窗口到达中央。目标可能限制移动、处于全屏、未响应或权限不足。");
        var title = new StringBuilder(512);
        Native.GetWindowText(hwnd, title, title.Capacity);
        string process = "PID " + pid;
        try { using (Process p = Process.GetProcessById((int)pid)) process = p.ProcessName; }
        catch (Exception) { }
        return "已居中：" + process + (title.Length > 0 ? " · " + title : "")
            + (restored ? "\r\n已从最大化状态还原。" : "")
            + (exceeds ? "\r\n窗口仍大于可用区域，部分内容可能超出屏幕或覆盖任务栏。"
                : resized ? "\r\n已尝试缩小窗口以适应工作区。" : "");
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

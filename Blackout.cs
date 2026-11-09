using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// Displays a black, mouse-blocking overlay only while Twinkle Tray reports 0%.
// Ctrl+Alt+Shift+F12 exits the helper. A lost or malformed pipe response hides it.
internal sealed class BlackoutWindow : Form
{
    private const int WM_HOTKEY = 0x0312, WM_SETCURSOR = 0x0020, WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    private const int LWA_ALPHA = 2;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    private readonly System.Windows.Forms.Timer timer;
    private readonly string monitorKey;
    private bool polling, isShown;
    private DateTime lastGood = DateTime.MinValue;
    private DateTime lastPoll = DateTime.MinValue;
    private volatile bool exiting;

    internal BlackoutWindow(string monitorKey)
    {
        this.monitorKey = monitorKey;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen.Bounds;
        // Create handle without showing or activating the window.
        var unused = Handle;
        SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);
        RegisterHotKey(Handle, 1, 0x0001 | 0x0002 | 0x0004, 0x7B); // Alt+Ctrl+Shift+F12
        timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += Tick;
        timer.Start();
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE; return cp; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == 1) { Application.Exit(); return; }
        if (m.Msg == WM_SETCURSOR && isShown) { SetCursor(IntPtr.Zero); m.Result = (IntPtr)1; return; }
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
        base.WndProc(ref m);
    }
    private void Tick(object sender, EventArgs e)
    {
        if ((DateTime.UtcNow - lastGood).TotalSeconds > 3) SetBlack(false);
        if (isShown) {
            // Other apps can raise a topmost window after us (e.g. the brightness
            // indicator). Raise the black overlay again without taking focus.
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            if (Bounds.Contains(Cursor.Position)) SetCursor(IntPtr.Zero);
        }
        if ((DateTime.UtcNow - lastPoll).TotalMilliseconds < 250) return;
        if (polling) return;
        lastPoll = DateTime.UtcNow;
        polling = true;
        Task.Run((Action)Poll);
    }
    private void Poll()
    {
        bool good = false, black = false;
        try
        {
            using (var pipe = new NamedPipeClientStream(".", @"twinkle-tray\cmds", PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(300);
                string escaped = monitorKey.Replace("\\", "\\\\").Replace("\"", "\\\"");
                byte[] command = Encoding.UTF8.GetBytes("{\"type\":\"get\",\"monitor\":\"" + escaped + "\",\"property\":\"brightness\"}");
                pipe.Write(command, 0, command.Length);
                byte[] response = new byte[128];
                var read = pipe.ReadAsync(response, 0, response.Length);
                if (read.Wait(500) && read.Result > 0)
                {
                    int value;
                    if (int.TryParse(Encoding.UTF8.GetString(response, 0, read.Result).Trim(), out value) && value >= 0 && value <= 100)
                    { good = true; black = value == 0; }
                }
            }
        }
        catch { /* Fail open on any error. */ }
        if (exiting) return;
        try { BeginInvoke((Action)(() => {
            polling = false;
            if (good) lastGood = DateTime.UtcNow;
            bool wasBlack = isShown;
            SetBlack(good && black);
            if (good && wasBlack && !black) Task.Run((Action)RequestOverlay);
        })); }
        catch { }
    }
    private static void RequestOverlay()
    {
        try {
            // Twinkle Tray's documented command asks its running instance to
            // show the normal safe overlay after our cover has been removed.
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "twinkle-tray", "Twinkle Tray.exe");
            if (!File.Exists(exe)) return;
            var start = new ProcessStartInfo(exe, "--Overlay") {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var process = Process.Start(start)) { }
        } catch { /* Losing the indicator must never prevent unblanking. */ }
    }
    private void SetBlack(bool black)
    {
        if (black == isShown) return;
        isShown = black;
        if (black) { Bounds = Screen.PrimaryScreen.Bounds; Show(); if (Bounds.Contains(Cursor.Position)) SetCursor(IntPtr.Zero); }
        else {
            Hide();
            // Restore a visible pointer immediately; the underlying app will
            // choose its own cursor the next time the pointer moves.
            SetCursor(LoadCursor(IntPtr.Zero, new IntPtr(32512)));
        }
    }
    protected override void Dispose(bool disposing)
    {
        exiting = true;
        if (disposing) { timer.Dispose(); if (isShown) SetBlack(false); }
        if (IsHandleCreated) UnregisterHotKey(Handle, 1);
        base.Dispose(disposing);
    }
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1 || String.IsNullOrWhiteSpace(args[0])) return;
        bool created;
        using (var mutex = new Mutex(true, @"Local\TwinkleZeroBlackout", out created))
        {
            if (!created) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var window = new BlackoutWindow(args[0])) Application.Run();
        }
    }
}


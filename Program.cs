/*
 * ====================================================================================
 * PROJECT:   Windows Hot Corners
 * AUTHOR:    Osman Onur Koç (@osmanonurkoc)
 * WEBSITE:   https://www.osmanonurkoc.com
 * LICENSE:   MIT License
 *
 * DESCRIPTION:
 * A lightweight utility that brings GNOME/macOS style "Hot Corners" to Windows.
 * It uses native Win32 APIs for zero-latency detection and supports Windows 10/11
 * Dark & Light themes automatically using a custom-drawn UI engine.
 * ====================================================================================
 */

using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.ComponentModel;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;

namespace HotCornersApp;

/// <summary>
/// Application configuration schema.
/// </summary>
public class Config
{
    public int TopLeft { get; set; } = 0;
    public int TopRight { get; set; } = 0;
    public int BottomLeft { get; set; } = 0;
    public int BottomRight { get; set; } = 0;
    public bool RunAtStartup { get; set; } = false;
    public int Sensitivity { get; set; } = 10;
    public int Cooldown { get; set; } = 500;
    public int TriggerDelay { get; set; } = 0;

    public string[] CustomAppPaths { get; set; } = new string[4] { "", "", "", "" };
    public string[] CustomHotkeys { get; set; } = new string[4] { "", "", "", "" };
}

/// <summary>
/// Manages system theme detection and UI color palette.
/// </summary>
public static class Theme
{
    public static Color BackColor { get; private set; }
    public static Color SurfaceColor { get; private set; }
    public static Color TextColor { get; private set; }
    public static Color SubTextColor { get; private set; }
    public static Color BorderColor { get; private set; }
    public static Color AccentColor { get; private set; }
    public static Color HoverColor { get; private set; }
    public static int Radius = 8;
    public static bool IsDarkMode { get; private set; }

    public static void ApplySystemTheme()
    {
        IsDarkMode = GetSystemTheme() == 0;

        if (IsDarkMode)
        {
            BackColor = Color.FromArgb(32, 32, 32);
            SurfaceColor = Color.FromArgb(45, 45, 48);
            TextColor = Color.FromArgb(243, 243, 243);
            SubTextColor = Color.FromArgb(170, 170, 170);
            BorderColor = Color.FromArgb(80, 80, 80);
            AccentColor = Color.FromArgb(0, 120, 215);
            HoverColor = Color.FromArgb(60, 60, 65);
        }
        else
        {
            BackColor = Color.FromArgb(243, 243, 243);
            SurfaceColor = Color.FromArgb(255, 255, 255);
            TextColor = Color.FromArgb(32, 32, 32);
            SubTextColor = Color.FromArgb(100, 100, 100);
            BorderColor = Color.FromArgb(210, 210, 210);
            AccentColor = Color.FromArgb(0, 120, 215);
            HoverColor = Color.FromArgb(230, 230, 230);
        }
    }

    private static int GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int val ? val : 0;
        }
        catch { return 0; }
    }
}

/// <summary>
/// GDI+ helper methods.
/// </summary>
public static class Gfx
{
    public static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
    {
        GraphicsPath path = new();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// --- CUSTOM CONTROLS ---

public class ModernNumericUpDown : Control
{
    private int _value;
    private int _min = 0;
    private int _max = 5000;
    private bool _isUpHovered;
    private bool _isDownHovered;
    private readonly int _buttonWidth = 24;
    private TextBox _txtInput;

    public event EventHandler? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum { get => _min; set => _min = value; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum { get => _max; set => _max = value; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var old = _value;
            _value = Math.Max(_min, Math.Min(_max, value));

            if (_txtInput.Text != _value.ToString())
                _txtInput.Text = _value.ToString();

            if (_value != old)
            {
                ValueChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    public ModernNumericUpDown()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(100, 30);
        Cursor = Cursors.Default;

        _txtInput = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Location = new Point(5, 6),
            Width = Width - _buttonWidth - 8,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = HorizontalAlignment.Center,
            Font = new Font("Segoe UI", 10)
        };

        _txtInput.KeyPress += (s, e) => {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        };

            _txtInput.TextChanged += (s, e) => {
                if (int.TryParse(_txtInput.Text, out int result))
                {
                    int clamped = Math.Max(_min, Math.Min(_max, result));
                    if (_value != clamped)
                    {
                        _value = clamped;
                        ValueChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            };

            _txtInput.Leave += (s, e) => _txtInput.Text = _value.ToString();

            // Bind mouse scroll events
            this.MouseWheel += OnScroll;
            _txtInput.MouseWheel += OnScroll;

            Controls.Add(_txtInput);
            UpdateTheme();
    }

    private void OnScroll(object? sender, MouseEventArgs e)
    {
        if (e.Delta > 0) Value++;
        else if (e.Delta < 0) Value--;

        if (e is HandledMouseEventArgs he) he.Handled = true;
    }

    public void UpdateTheme()
    {
        BackColor = Theme.SurfaceColor;
        _txtInput.BackColor = Theme.SurfaceColor;
        _txtInput.ForeColor = Theme.TextColor;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_txtInput.BackColor != Theme.SurfaceColor) UpdateTheme();

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Theme.BackColor);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var path = Gfx.GetRoundedPath(rect, Theme.Radius);

        using (var b = new SolidBrush(Theme.SurfaceColor)) e.Graphics.FillPath(b, path);
        using (var p = new Pen(Theme.BorderColor)) e.Graphics.DrawPath(p, path);

        int lineX = Width - _buttonWidth;
        using (var p = new Pen(Theme.BorderColor))
        {
            e.Graphics.DrawLine(p, lineX, 0, lineX, Height);
            e.Graphics.DrawLine(p, lineX, Height / 2, Width, Height / 2);
        }

        DrawArrow(e.Graphics, new Rectangle(lineX, 0, _buttonWidth, Height / 2), true, _isUpHovered);
        DrawArrow(e.Graphics, new Rectangle(lineX, Height / 2, _buttonWidth, Height / 2), false, _isDownHovered);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool up = e.X > Width - _buttonWidth && e.Y < Height / 2;
        bool down = e.X > Width - _buttonWidth && e.Y >= Height / 2;

        if (up != _isUpHovered || down != _isDownHovered)
        {
            _isUpHovered = up;
            _isDownHovered = down;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isUpHovered = false;
        _isDownHovered = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.X > Width - _buttonWidth)
        {
            if (e.Y < Height / 2) Value++;
            else Value--;
            _txtInput.Text = Value.ToString();
        }
    }

    private void DrawArrow(Graphics g, Rectangle bounds, bool isUp, bool isHovered)
    {
        if (isHovered)
        {
            using var b = new SolidBrush(Color.FromArgb(20, 120, 120, 120));
            g.FillRectangle(b, bounds);
        }

        int arrowSize = 4;
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        Point[] points;

        if (isUp) points = [new(center.X - arrowSize, center.Y + 2), new(center.X + arrowSize, center.Y + 2), new(center.X, center.Y - 2)];
        else points = [new(center.X - arrowSize, center.Y - 2), new(center.X + arrowSize, center.Y - 2), new(center.X, center.Y + 2)];

        using var brush = new SolidBrush(isHovered ? Theme.AccentColor : Theme.SubTextColor);
        g.FillPolygon(brush, points);
    }
}

public class ModernComboBox : ComboBox
{
    public ModernComboBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.DoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        ItemHeight = 22;
        BackColor = Theme.SurfaceColor;
        ForeColor = Theme.TextColor;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Theme.BackColor);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var path = Gfx.GetRoundedPath(rect, Theme.Radius);

        using (var b = new SolidBrush(Theme.SurfaceColor)) e.Graphics.FillPath(b, path);
        using (var p = new Pen(Theme.BorderColor)) e.Graphics.DrawPath(p, path);

        var text = SelectedItem?.ToString() ?? Text;
        var textRect = new Rectangle(8, 0, Width - 30, Height);
        TextRenderer.DrawText(e.Graphics, text, Font, textRect, Theme.TextColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var arrowX = Width - 20;
        var arrowY = Height / 2 - 2;
        Point[] points = [new(arrowX, arrowY), new(arrowX + 9, arrowY), new(arrowX + 4, arrowY + 5)];

        using var brush = new SolidBrush(Theme.SubTextColor);
        e.Graphics.FillPolygon(brush, points);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color selColor = isSelected ? Theme.AccentColor : Theme.SurfaceColor;
        Color txtColor = isSelected ? Color.White : Theme.TextColor;

        using (var b = new SolidBrush(selColor)) e.Graphics.FillRectangle(b, e.Bounds);

        TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString() ?? "", Font, new Point(e.Bounds.X + 5, e.Bounds.Y + 3), txtColor, TextFormatFlags.NoPrefix);
    }
}

public class ModernCheckBox : CheckBox
{
    public ModernCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        AutoSize = false;
        Size = new Size(350, 26);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Theme.BackColor);

        var boxRect = new Rectangle(0, 5, 16, 16);
        var path = Gfx.GetRoundedPath(boxRect, 4);

        if (Checked)
        {
            using var b = new SolidBrush(Theme.AccentColor);
            e.Graphics.FillPath(b, path);
            using var p = new Pen(Color.White, 2);
            e.Graphics.DrawLine(p, 3, 12, 6, 15);
            e.Graphics.DrawLine(p, 6, 15, 13, 8);
        }
        else
        {
            using var b = new SolidBrush(Theme.SurfaceColor);
            e.Graphics.FillPath(b, path);
            using var p = new Pen(Theme.BorderColor);
            e.Graphics.DrawPath(p, path);
        }

        var textRect = new Rectangle(24, 0, Width - 25, Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, Theme.TextColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }
}

public class ModernButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsPrimary { get; set; }
    private bool _isHovered;

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Size = new Size(100, 35);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _isHovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _isHovered = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Theme.BackColor);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var path = Gfx.GetRoundedPath(rect, Theme.Radius);

        Color bg;
        Color txt;

        if (IsPrimary)
        {
            bg = _isHovered ? ControlPaint.Light(Theme.AccentColor) : Theme.AccentColor;
            txt = Color.White;
        }
        else
        {
            bg = _isHovered ? Theme.HoverColor : Theme.SurfaceColor;
            txt = Theme.TextColor;
        }

        using (var b = new SolidBrush(bg)) e.Graphics.FillPath(b, path);

        if (!IsPrimary)
        {
            using var p = new Pen(Theme.BorderColor);
            e.Graphics.DrawPath(p, path);
        }

        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, txt, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

public class RoundedTextBox : Panel
{
    public TextBox InnerTextBox;
    public RoundedTextBox()
    {
        Padding = new Padding(10, 8, 10, 8);
        BackColor = Theme.SurfaceColor;
        Size = new Size(310, 36);
        InnerTextBox = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.SurfaceColor, ForeColor = Theme.TextColor, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10) };
        Controls.Add(InnerTextBox);
    }

    public void UpdateTheme()
    {
        BackColor = Theme.SurfaceColor;
        InnerTextBox.BackColor = Theme.SurfaceColor;
        InnerTextBox.ForeColor = Theme.TextColor;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Theme.BackColor);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var b = new SolidBrush(Theme.SurfaceColor);
        e.Graphics.FillPath(b, Gfx.GetRoundedPath(rect, Theme.Radius));
        using var p = new Pen(Theme.BorderColor);
        e.Graphics.DrawPath(p, Gfx.GetRoundedPath(rect, Theme.Radius));
    }
}

/// <summary>
/// A robust UI Control for capturing custom keyboard shortcuts.
/// </summary>
public class HotkeyInputBox : Panel
{
    public TextBox InnerTextBox;
    public ModernButton BtnClear;

    public List<byte> Hotkeys = new();
    private bool _isListening = false;

    public event EventHandler? HotkeyChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string KeysString { get; set; } = "";

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelKeyboardProc _proc;
    private IntPtr _hookID = IntPtr.Zero;
    private HashSet<byte> _currentPressed = new();

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    public HotkeyInputBox()
    {
        _proc = HookCallback;

        Padding = new Padding(10, 8, 10, 8);
        BackColor = Theme.SurfaceColor;

        BtnClear = new ModernButton { Text = "X", Size = new Size(24, 24), Cursor = Cursors.Hand };
        BtnClear.Click += (s, e) => {
            Hotkeys.Clear();
            KeysString = "";
            RenderText();
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
        };

        InnerTextBox = new TextBox {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.SurfaceColor,
            ForeColor = Theme.TextColor,
            Font = new Font("Segoe UI", 10),
            ReadOnly = true,
            Cursor = Cursors.Hand,
            ShortcutsEnabled = false
        };

        Size = new Size(310, 36);

        Controls.Add(BtnClear);
        Controls.Add(InnerTextBox);

        InnerTextBox.PreviewKeyDown += (s, e) => e.IsInputKey = true;
        InnerTextBox.Enter += (s, e) => StartListening();
        InnerTextBox.Leave += (s, e) => StopListening();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (BtnClear == null || InnerTextBox == null) return;

        BtnClear.Location = new Point(Width - BtnClear.Width - 8, (Height - BtnClear.Height) / 2);
        InnerTextBox.Location = new Point(10, 8);
        InnerTextBox.Width = BtnClear.Left - InnerTextBox.Left - 5;
    }

    public void StartListening()
    {
        if (_isListening) return;
        _isListening = true;
        _currentPressed.Clear();
        RenderText();
        SetHook();
        Invalidate();
    }

    public void StopListening()
    {
        if (!_isListening) return;
        _isListening = false;
        RemoveHook();
        RenderText();
        Invalidate();
    }

    private void SetHook()
    {
        if (_hookID != IntPtr.Zero) return;

        IntPtr hModule = GetModuleHandle(null);
        if (hModule == IntPtr.Zero)
            hModule = LoadLibrary("user32.dll");

        _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hModule, 0);
    }

    private void RemoveHook()
    {
        if (_hookID != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            int vkCode = Marshal.ReadInt32(lParam);
            byte code = (byte)vkCode;

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                if (_currentPressed.Count == 0) Hotkeys.Clear();

                _currentPressed.Add(code);
                UpdateHotkeysFromPressed();
                return (IntPtr)1;
            }
            else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
            {
                _currentPressed.Remove(code);
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private void UpdateHotkeysFromPressed()
    {
        Hotkeys.Clear();

        bool hasCtrl = _currentPressed.Contains(0xA2) || _currentPressed.Contains(0xA3) || _currentPressed.Contains(0x11);
        bool hasShift = _currentPressed.Contains(0xA0) || _currentPressed.Contains(0xA1) || _currentPressed.Contains(0x10);
        bool hasAlt = _currentPressed.Contains(0xA4) || _currentPressed.Contains(0xA5) || _currentPressed.Contains(0x12);
        bool hasWin = _currentPressed.Contains(0x5B) || _currentPressed.Contains(0x5C);

        if (hasCtrl) Hotkeys.Add(0x11);
        if (hasShift) Hotkeys.Add(0x10);
        if (hasAlt) Hotkeys.Add(0x12);
        if (hasWin) Hotkeys.Add(0x5B);

        foreach (var k in _currentPressed)
        {
            if (k != 0x11 && k != 0x10 && k != 0x12 && k != 0x5B && k != 0x5C &&
                k != 0xA0 && k != 0xA1 && k != 0xA2 && k != 0xA3 && k != 0xA4 && k != 0xA5)
            {
                Hotkeys.Add(k);
            }
        }

        KeysString = string.Join(",", Hotkeys);

        if (IsHandleCreated)
            InnerTextBox.BeginInvoke(new Action(() => {
                RenderText();
                HotkeyChanged?.Invoke(this, EventArgs.Empty);
            }));
    }

    public void LoadFromString(string val)
    {
        KeysString = val;
        Hotkeys.Clear();
        if (!string.IsNullOrEmpty(val))
        {
            foreach (var s in val.Split(','))
            {
                if (byte.TryParse(s, out byte b)) Hotkeys.Add(b);
            }
        }
        RenderText();
    }

    private void RenderText()
    {
        if (_isListening && Hotkeys.Count == 0)
        {
            InnerTextBox.Text = "Listening (Press keys)...";
            return;
        }

        if (Hotkeys.Count == 0) { InnerTextBox.Text = "None"; return; }

        var names = new List<string>();
        foreach (var k in Hotkeys)
        {
            if (k == 0x11) names.Add("Ctrl");
            else if (k == 0x10) names.Add("Shift");
            else if (k == 0x12) names.Add("Alt");
            else if (k == 0x5B) names.Add("Win");
            else if (k == 0xAD) names.Add("Mute");
            else if (k == 0xAE) names.Add("Vol Down");
            else if (k == 0xAF) names.Add("Vol Up");
            else if (k == 0xB0) names.Add("Next Track");
            else if (k == 0xB1) names.Add("Prev Track");
            else if (k == 0xB2) names.Add("Stop");
            else if (k == 0xB3) names.Add("Play/Pause");
            else names.Add(((Keys)k).ToString());
        }

        InnerTextBox.Text = string.Join(" + ", names);
    }

    public void UpdateTheme()
    {
        BackColor = Theme.SurfaceColor;
        InnerTextBox.BackColor = Theme.SurfaceColor;
        InnerTextBox.ForeColor = Theme.TextColor;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Theme.BackColor);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        using var b = new SolidBrush(Theme.SurfaceColor);
        e.Graphics.FillPath(b, Gfx.GetRoundedPath(rect, Theme.Radius));

        Color bColor = _isListening ? Theme.AccentColor : Theme.BorderColor;
        float bWidth = _isListening ? 2f : 1f;

        if (_isListening) rect = new Rectangle(1, 1, Width - 3, Height - 3);

        using var p = new Pen(bColor, bWidth);
        e.Graphics.DrawPath(p, Gfx.GetRoundedPath(rect, Theme.Radius));
    }

    protected override void Dispose(bool disposing)
    {
        RemoveHook();
        base.Dispose(disposing);
    }
}

// --- MAIN FORM ---

public class MainForm : Form
{
    private NotifyIcon? _trayIcon;
    private System.Windows.Forms.Timer? _pollTimer;
    private Config _config = new();
    private string _configPath = "";
    private string _appDataFolder = "";
    private DateTime _lastTriggerTime = DateTime.MinValue;
    private DateTime? _cornerEntryTime = null;

    private ModernComboBox? _cbTopLeft, _cbTopRight, _cbBottomLeft, _cbBottomRight, _cbCornerSelect;
    private ModernNumericUpDown? _numSensitivity, _numCooldown, _numTriggerDelay;
    private RoundedTextBox? _txtCustomPath;
    private HotkeyInputBox? _customHotkeyBox;
    private ModernCheckBox? _chkStartup;
    private Panel? _headerPanel;
    private bool _allowVisible = true;
    private bool _isTriggered = false;
    private readonly bool[] _cornerToggled = new bool[4];

    private int _currentEditingCorner = 0;

    private readonly string[] _actions = {
        "None",
        "Trigger VirtualSpace Grid",
        "Native Task View (Win+Tab)",
        "Launch Custom App",
        "Show Desktop",
        "Lock Screen",
        "Action Center",
        "File Explorer",
        "Minimize All Windows",
        "Start Menu",
        "Start Screen Saver",
        "Task Manager",
        "Custom Hotkey"
    };

    public MainForm(bool startHidden)
    {
        _allowVisible = !startHidden;
        InitPaths();

        Theme.ApplySystemTheme();

        Text = "Windows Hot Corners";
        Size = new Size(420, 755);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        ApplyFormTheme();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        InitializeUI();
        LoadConfig();
        InitializeTray();

        SwitchEditingCorner(0);

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _pollTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _pollTimer.Tick += CheckCorners;
        _pollTimer.Start();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            Theme.ApplySystemTheme();
            if (InvokeRequired) Invoke(new Action(ApplyFormTheme));
            else ApplyFormTheme();
        }
    }

    private void ApplyFormTheme()
    {
        BackColor = Theme.BackColor;
        ForeColor = Theme.TextColor;

        int useImmersiveDarkMode = Theme.IsDarkMode ? 1 : 0;
        DwmSetWindowAttribute(Handle, 20, ref useImmersiveDarkMode, sizeof(int));

        RefreshControls(this);
        _txtCustomPath?.UpdateTheme();
        _customHotkeyBox?.UpdateTheme();
        _headerPanel?.Invalidate();
    }

    private void RefreshControls(Control parent)
    {
        parent.BackColor = Theme.BackColor;
        parent.ForeColor = Theme.TextColor;
        parent.Invalidate();
        foreach (Control c in parent.Controls)
        {
            if (c is Label) c.ForeColor = Theme.SubTextColor;
            if (c is ModernButton || c is ModernComboBox || c is ModernCheckBox || c is ModernNumericUpDown)
            {
                c.Invalidate();
            }
            if (c.HasChildren) RefreshControls(c);
        }
    }

    private void InitPaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _appDataFolder = Path.Combine(localAppData, "HotCornersApp");
        _configPath = Path.Combine(_appDataFolder, "config.json");
        if (!Directory.Exists(_appDataFolder)) Directory.CreateDirectory(_appDataFolder);
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    protected override void SetVisibleCore(bool value)
    {
        if (!_allowVisible && value) { base.SetVisibleCore(false); return; }
        base.SetVisibleCore(value);
    }

    private void InitializeUI()
    {
        _headerPanel = new Panel { Size = new Size(420, 90), Dock = DockStyle.Top, Cursor = Cursors.Hand };
        _headerPanel.Paint += HeaderPanel_Paint;
        _headerPanel.Click += (s, e) => Process.Start(new ProcessStartInfo("https://www.osmanonurkoc.com") { UseShellExecute = true });
        Controls.Add(_headerPanel);

        int y = 110;
        int labelH = 22;
        int gap = 75;

        int colLeft = 25;
        int colRight = 215;

        CreateLabel("Top Left", colLeft, y);
        _cbTopLeft = CreateCombo(colLeft, y + labelH);
        _cbTopLeft.SelectedIndexChanged += (s, e) => _config.TopLeft = _cbTopLeft.SelectedIndex;

        CreateLabel("Top Right", colRight, y);
        _cbTopRight = CreateCombo(colRight, y + labelH);
        _cbTopRight.SelectedIndexChanged += (s, e) => _config.TopRight = _cbTopRight.SelectedIndex;

        y += gap;

        CreateLabel("Bottom Left", colLeft, y);
        _cbBottomLeft = CreateCombo(colLeft, y + labelH);
        _cbBottomLeft.SelectedIndexChanged += (s, e) => _config.BottomLeft = _cbBottomLeft.SelectedIndex;

        CreateLabel("Bottom Right", colRight, y);
        _cbBottomRight = CreateCombo(colRight, y + labelH);
        _cbBottomRight.SelectedIndexChanged += (s, e) => _config.BottomRight = _cbBottomRight.SelectedIndex;

        y += gap + 10;

        int x1 = 25;
        int x2 = 155;
        int x3 = 285;

        CreateLabel("Sensitivity (px)", x1, y);
        _numSensitivity = CreateNumeric(x1, y + labelH, 1, 100);
        _numSensitivity.ValueChanged += (s, e) => _config.Sensitivity = _numSensitivity.Value;

        CreateLabel("Cooldown (ms)", x2, y);
        _numCooldown = CreateNumeric(x2, y + labelH, 100, 5000);
        _numCooldown.ValueChanged += (s, e) => _config.Cooldown = _numCooldown.Value;

        CreateLabel("Trigger Delay (ms)", x3, y);
        _numTriggerDelay = CreateNumeric(x3, y + labelH, 0, 2000);
        _numTriggerDelay.ValueChanged += (s, e) => _config.TriggerDelay = _numTriggerDelay.Value;

        y += gap;

        CreateLabel("Editing Custom App/Hotkey Settings For:", colLeft, y);
        _cbCornerSelect = new ModernComboBox { Location = new Point(colLeft, y + labelH), Width = 360 };
        _cbCornerSelect.Items.AddRange(new[] { "Top Left", "Top Right", "Bottom Left", "Bottom Right" });
        _cbCornerSelect.SelectedIndex = 0;
        _cbCornerSelect.SelectedIndexChanged += (s, e) => SwitchEditingCorner(_cbCornerSelect.SelectedIndex);
        Controls.Add(_cbCornerSelect);

        y += gap;

        CreateLabel("Custom App Path", colLeft, y);
        _txtCustomPath = new RoundedTextBox { Location = new Point(colLeft, y + labelH), Size = new Size(310, 36) };
        _txtCustomPath.InnerTextBox.TextChanged += (s, e) => {
            _config.CustomAppPaths[_currentEditingCorner] = _txtCustomPath.InnerTextBox.Text;
        };
        Controls.Add(_txtCustomPath);

        var btnBrowse = new ModernButton { Text = "...", Location = new Point(345, y + labelH), Size = new Size(40, 36) };
        btnBrowse.Click += BrowseFile;
        Controls.Add(btnBrowse);

        y += 65;

        CreateLabel("Custom Hotkey", colLeft, y);
        _customHotkeyBox = new HotkeyInputBox { Location = new Point(colLeft, y + labelH) };
        _customHotkeyBox.HotkeyChanged += (s, e) => {
            _config.CustomHotkeys[_currentEditingCorner] = _customHotkeyBox.KeysString;
        };
        Controls.Add(_customHotkeyBox);

        var btnStopListen = new ModernButton { Text = "OK", Location = new Point(345, y + labelH), Size = new Size(40, 36), IsPrimary = true };
        btnStopListen.Click += (s, e) => {
            _customHotkeyBox.StopListening();
            this.Focus();
        };
        Controls.Add(btnStopListen);

        y += 65;

        _chkStartup = new ModernCheckBox { Text = "Run at Windows Startup", Location = new Point(colLeft, y) };
        Controls.Add(_chkStartup);

        var btnSave = new ModernButton { Text = "Save & Minimize", Location = new Point(colLeft, y + 40), Size = new Size(360, 45), IsPrimary = true };
        btnSave.Click += SaveAndHide;
        Controls.Add(btnSave);
    }

    private void SwitchEditingCorner(int index)
    {
        _currentEditingCorner = index;

        if (_txtCustomPath != null && _config.CustomAppPaths != null)
            _txtCustomPath.InnerTextBox.Text = _config.CustomAppPaths[index];

        if (_customHotkeyBox != null && _config.CustomHotkeys != null)
            _customHotkeyBox.LoadFromString(_config.CustomHotkeys[index]);
    }

    private void HeaderPanel_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        e.Graphics.Clear(Theme.BackColor);

        int iconSize = 40;
        int spacing = 15;
        string title = "Windows Hot Corners";
        string subtitle = "@osmanonurkoc";
        var titleFont = new Font("Segoe UI", 14, FontStyle.Bold);
        var subFont = new Font("Segoe UI", 9, FontStyle.Regular);

        float totalWidth = iconSize + spacing + Math.Max(e.Graphics.MeasureString(title, titleFont).Width, e.Graphics.MeasureString(subtitle, subFont).Width);
        float startX = ((_headerPanel?.Width ?? 420) - totalWidth) / 2;
        float startY = 25;

        var rect = new Rectangle((int)startX, (int)startY, iconSize, iconSize);
        using (var brush = new LinearGradientBrush(rect, Color.FromArgb(51, 119, 244), Color.FromArgb(81, 148, 245), 45f))
        {
            e.Graphics.FillPath(brush, Gfx.GetRoundedPath(rect, 10));
        }

        int pad = 10, innerSize = iconSize - (pad * 2);
        var inner = new Rectangle((int)startX + pad, (int)startY + pad, innerSize, innerSize);
        using (var p = new Pen(Color.White, 2.5f))
        {
            e.Graphics.DrawLine(p, inner.X, inner.Y + 6, inner.X, inner.Y); e.Graphics.DrawLine(p, inner.X, inner.Y, inner.X + 6, inner.Y);
            e.Graphics.DrawLine(p, inner.Right, inner.Y + 6, inner.Right, inner.Y); e.Graphics.DrawLine(p, inner.Right, inner.Y, inner.Right - 6, inner.Y);
            e.Graphics.DrawLine(p, inner.X, inner.Bottom - 6, inner.X, inner.Bottom); e.Graphics.DrawLine(p, inner.X, inner.Bottom, inner.X + 6, inner.Bottom);
            e.Graphics.DrawLine(p, inner.Right, inner.Bottom - 6, inner.Right, inner.Bottom); e.Graphics.DrawLine(p, inner.Right, inner.Bottom, inner.Right - 6, inner.Bottom);
        }

        using (var b = new SolidBrush(Theme.TextColor))
        e.Graphics.DrawString(title, titleFont, b, startX + iconSize + spacing, startY + 2);

        using (var sb = new SolidBrush(Theme.SubTextColor))
        e.Graphics.DrawString(subtitle, subFont, sb, startX + iconSize + spacing + 2, startY + 28);
    }

    private void CreateLabel(string text, int x, int y)
    {
        Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Theme.SubTextColor, Font = new Font("Segoe UI", 9) });
    }

    private ModernComboBox CreateCombo(int x, int y)
    {
        var cb = new ModernComboBox { Location = new Point(x, y), Width = 175 };
        cb.Items.AddRange(_actions); cb.SelectedIndex = 0;
        Controls.Add(cb); return cb;
    }

    private ModernNumericUpDown CreateNumeric(int x, int y, int min, int max)
    {
        var num = new ModernNumericUpDown { Location = new Point(x, y), Minimum = min, Maximum = max };
        Controls.Add(num); return num;
    }

    private void BrowseFile(object? sender, EventArgs e)
    {
        var fd = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe" };
        if (fd.ShowDialog() == DialogResult.OK && _txtCustomPath != null)
            _txtCustomPath.InnerTextBox.Text = fd.FileName;
    }

    private void SaveAndHide(object? sender, EventArgs e)
    {
        if (!Directory.Exists(_appDataFolder)) Directory.CreateDirectory(_appDataFolder);

        _config.TopLeft = _cbTopLeft?.SelectedIndex ?? 0;
        _config.TopRight = _cbTopRight?.SelectedIndex ?? 0;
        _config.BottomLeft = _cbBottomLeft?.SelectedIndex ?? 0;
        _config.BottomRight = _cbBottomRight?.SelectedIndex ?? 0;
        _config.RunAtStartup = _chkStartup?.Checked ?? false;
        _config.Sensitivity = _numSensitivity?.Value ?? 10;
        _config.Cooldown = _numCooldown?.Value ?? 500;
        _config.TriggerDelay = _numTriggerDelay?.Value ?? 0;

        try {
            string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            SetStartup(_chkStartup?.Checked ?? false);
            Hide();
        }
        catch (Exception ex) { MessageBox.Show("Error saving: " + ex.Message); }
    }

    private void InitializeTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (s, e) => { _allowVisible = true; Show(); WindowState = FormWindowState.Normal; });
        menu.Items.Add("Exit", null, (s, e) => Application.Exit());

        _trayIcon = new NotifyIcon { Text = "Windows Hot Corners", Icon = Icon, Visible = true, ContextMenuStrip = menu };
        _trayIcon.DoubleClick += (s, e) => { _allowVisible = true; Show(); WindowState = FormWindowState.Normal; };
    }

    private void CheckCorners(object? sender, EventArgs e)
    {
        var primaryScreen = Screen.PrimaryScreen;
        if (primaryScreen is null) return;

        if ((DateTime.Now - _lastTriggerTime).TotalMilliseconds < (_config.Cooldown)) return;

        if (GetAsyncKeyState(0x01) < 0 || GetAsyncKeyState(0x02) < 0)
        {
            _cornerEntryTime = null;
            return;
        }

        if (IsForegroundWindowFullScreen())
        {
            _cornerEntryTime = null;
            return;
        }

        Point cursor = Cursor.Position;
        Rectangle screen = primaryScreen.Bounds;
        int s = _config.Sensitivity;

        // Accurate math using >= for correct pixel mapping
        bool tl = (cursor.X < screen.Left + s && cursor.Y < screen.Top + s);
        bool tr = (cursor.X >= screen.Right - s && cursor.Y < screen.Top + s);
        bool bl = (cursor.X < screen.Left + s && cursor.Y >= screen.Bottom - s);
        bool br = (cursor.X >= screen.Right - s && cursor.Y >= screen.Bottom - s);

        if (tl || tr || bl || br)
        {
            if (_isTriggered) return;

            if (_cornerEntryTime == null)
            {
                _cornerEntryTime = DateTime.Now;
            }

            if ((DateTime.Now - _cornerEntryTime.Value).TotalMilliseconds >= _config.TriggerDelay)
            {
                _isTriggered = true;
                _lastTriggerTime = DateTime.Now;
                _cornerEntryTime = null;

                if (tl) ExecuteAction(_config.TopLeft, 0);
                else if (tr) ExecuteAction(_config.TopRight, 1);
                else if (bl) ExecuteAction(_config.BottomLeft, 2);
                else if (br) ExecuteAction(_config.BottomRight, 3);
            }
        }
        else
        {
            _isTriggered = false;
            _cornerEntryTime = null;
        }
    }

    private bool IsForegroundWindowFullScreen()
    {
        if (_cornerToggled[0] || _cornerToggled[1] || _cornerToggled[2] || _cornerToggled[3])
            return false;

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        if (hwnd == GetDesktopWindow() || hwnd == GetShellWindow()) return false;

        var classNameBuilder = new System.Text.StringBuilder(256);
        GetClassName(hwnd, classNameBuilder, classNameBuilder.Capacity);
        string className = classNameBuilder.ToString();

        if (className is "XamlExplorerHostIslandWindow"
            or "MultitaskingViewFrame"
            or "Windows.UI.Core.CoreWindow"
            or "WorkerW"
            or "Progman"
            or "Shell_TrayWnd"
            or "Shell_SecondaryTrayWnd"
            or "ForegroundStaging")
        {
            return false;
        }

        GetWindowRect(hwnd, out Rectangle rect);

        Screen? screen = Screen.FromHandle(hwnd);
        if (screen is null) return false;

        bool isSameSize = rect.Width == screen.Bounds.Width && rect.Height == screen.Bounds.Height;

        int style = GetWindowLong(hwnd, GWL_STYLE);
        bool hasNoBorder = (style & (int)WS_CAPTION) == 0;

        return isSameSize && hasNoBorder;
    }

    // --- Win32 Imports ---

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rectangle lpRect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);
    [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_SCREENSAVE = 0xF140;
    private const int GWL_STYLE = -16;
    private const uint WS_CAPTION = 0x00C00000;

    private void ExecuteAction(int actionIdx, int cornerIdx)
    {
        bool isAlreadyOpen = _cornerToggled[cornerIdx];

        switch (actionIdx)
        {
            case 1:
                SendKeysTriple(0x11, 0x10, 0x09); // Ctrl+Shift+Tab
                _cornerToggled[cornerIdx] = !isAlreadyOpen;
                break;
            case 2:
                SendKeys(0x5B, 0x09); // Win+Tab
                _cornerToggled[cornerIdx] = !isAlreadyOpen;
                break;
            case 3:
                string appPath = _config.CustomAppPaths[cornerIdx];
                if (!string.IsNullOrWhiteSpace(appPath)) {
                    try { Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = true }); } catch { }
                }
                break;
            case 4:
                SendKeys(0x5B, 0x44); // Win+D
                _cornerToggled[cornerIdx] = !isAlreadyOpen;
                break;
            case 5:
                _cornerToggled[cornerIdx] = false;
                LockWorkStation();
                break;
            case 6:
                SendKeys(0x5B, 0x41); // Win+A
                _cornerToggled[cornerIdx] = !isAlreadyOpen;
                break;
            case 7:
                SendKeys(0x5B, 0x45); // Win+E
                break;
            case 8:
                if (!isAlreadyOpen)
                    SendKeys(0x5B, 0x4D); // Win+M
                    else
                        SendKeysTriple(0x5B, 0x10, 0x4D);
            _cornerToggled[cornerIdx] = !isAlreadyOpen;
            break;
            case 9:
                SendSingleKey(0x5B); // Win
                _cornerToggled[cornerIdx] = !isAlreadyOpen;
                break;
            case 10:
                SendMessage(GetDesktopWindow(), WM_SYSCOMMAND, SC_SCREENSAVE, 0);
                break;
            case 11:
                SendKeysTriple(0x11, 0x10, 0x1B); // Ctrl+Shift+Esc
                break;
            case 12:
                string hotkeyStr = _config.CustomHotkeys[cornerIdx];
                if (!string.IsNullOrEmpty(hotkeyStr))
                {
                    List<byte> keysToPress = new List<byte>();
                    foreach (var s in hotkeyStr.Split(',')) {
                        if (byte.TryParse(s, out byte b)) keysToPress.Add(b);
                    }

                    if (keysToPress.Count > 0) {
                        foreach (var k in keysToPress) {
                            uint ext = (k >= 0x21 && k <= 0x2F) || (k >= 0xA6 && k <= 0xB7) ? 1u : 0u;
                            keybd_event(k, 0, ext, 0);
                        }
                        for (int i = keysToPress.Count - 1; i >= 0; i--) {
                            byte k = keysToPress[i];
                            uint ext = (k >= 0x21 && k <= 0x2F) || (k >= 0xA6 && k <= 0xB7) ? 1u : 0u;
                            keybd_event(k, 0, ext | 2u, 0);
                        }
                        _cornerToggled[cornerIdx] = !isAlreadyOpen;
                    }
                }
                break;
        }
    }

    private void SendSingleKey(byte k1) { keybd_event(k1, 0, 0, 0); keybd_event(k1, 0, 2, 0); }
    private void SendKeys(byte k1, byte k2) { keybd_event(k1, 0, 0, 0); keybd_event(k2, 0, 0, 0); keybd_event(k2, 0, 2, 0); keybd_event(k1, 0, 2, 0); }
    private void SendKeysTriple(byte k1, byte k2, byte k3) { keybd_event(k1, 0, 0, 0); keybd_event(k2, 0, 0, 0); keybd_event(k3, 0, 0, 0); keybd_event(k3, 0, 2, 0); keybd_event(k2, 0, 2, 0); keybd_event(k1, 0, 2, 0); }

    private void LoadConfig()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                string content = File.ReadAllText(_configPath);
                var cfg = JsonSerializer.Deserialize<Config>(content);
                if (cfg != null)
                {
                    _config = cfg;

                    if (_cbTopLeft != null) _cbTopLeft.SelectedIndex = _config.TopLeft;
                    if (_cbTopRight != null) _cbTopRight.SelectedIndex = _config.TopRight;
                    if (_cbBottomLeft != null) _cbBottomLeft.SelectedIndex = _config.BottomLeft;
                    if (_cbBottomRight != null) _cbBottomRight.SelectedIndex = _config.BottomRight;
                    if (_chkStartup != null) _chkStartup.Checked = _config.RunAtStartup;
                    if (_numSensitivity != null) _numSensitivity.Value = _config.Sensitivity;
                    if (_numCooldown != null) _numCooldown.Value = _config.Cooldown;
                    if (_numTriggerDelay != null) _numTriggerDelay.Value = _config.TriggerDelay;
                }
            }
            catch { }
        }
    }

    private void SetStartup(bool enable)
    {
        try {
            using var rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (enable) rk?.SetValue("HotCornersApp", $"\"{Application.ExecutablePath}\" --startup");
            else rk?.DeleteValue("HotCornersApp", false);
        } catch {}
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnFormClosing(e);
    }

    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Global\\HOT_CORNERS_APP_MUTEX_V2", out bool createdNew);
        if (!createdNew) { MessageBox.Show("Application is already running! Check the System Tray."); return; }

        bool hidden = args.Length > 0 && args[0] == "--startup";
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(hidden));
    }
}

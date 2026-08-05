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

namespace HotCornersApp;

/// <summary>
/// Application configuration schema.
/// </summary>
public class Config
{
    public int TopLeft { get; set; }
    public int TopRight { get; set; }
    public int BottomLeft { get; set; }
    public int BottomRight { get; set; }
    public bool RunAtStartup { get; set; }
    public int Sensitivity { get; set; } = 10;
    public int Cooldown { get; set; } = 500;
    public int TriggerDelay { get; set; } = 0;
    public string CustomAppPath { get; set; } = "";
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
        Size = new Size(200, 36);
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

    private ModernComboBox? _cbTopLeft, _cbTopRight, _cbBottomLeft, _cbBottomRight;
    private ModernNumericUpDown? _numSensitivity, _numCooldown, _numTriggerDelay;
    private RoundedTextBox? _txtCustomPath;
    private ModernCheckBox? _chkStartup;
    private Panel? _headerPanel;
    private bool _allowVisible = true;
    private bool _isTriggered = false;

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
        "Task Manager"
    };

    public MainForm(bool startHidden)
    {
        _allowVisible = !startHidden;
        InitPaths();

        Theme.ApplySystemTheme();

        Text = "Windows Hot Corners";
        Size = new Size(420, 680);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        ApplyFormTheme();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        InitializeUI();
        LoadConfig();
        InitializeTray();

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

        CreateLabel("Top Left", colLeft, y); _cbTopLeft = CreateCombo(colLeft, y + labelH);
        CreateLabel("Top Right", colRight, y); _cbTopRight = CreateCombo(colRight, y + labelH);

        y += gap;

        CreateLabel("Bottom Left", colLeft, y); _cbBottomLeft = CreateCombo(colLeft, y + labelH);
        CreateLabel("Bottom Right", colRight, y); _cbBottomRight = CreateCombo(colRight, y + labelH);

        y += gap + 10;

        int x1 = 25;
        int x2 = 155;
        int x3 = 285;

        CreateLabel("Sensitivity (px)", x1, y);
        _numSensitivity = CreateNumeric(x1, y + labelH, 1, 100);

        CreateLabel("Cooldown (ms)", x2, y);
        _numCooldown = CreateNumeric(x2, y + labelH, 100, 5000);

        CreateLabel("Trigger Delay (ms)", x3, y);
        _numTriggerDelay = CreateNumeric(x3, y + labelH, 0, 2000);

        y += gap;

        CreateLabel("Custom App Path", colLeft, y);
        _txtCustomPath = new RoundedTextBox { Location = new Point(colLeft, y + labelH), Size = new Size(310, 36) };
        Controls.Add(_txtCustomPath);

        var btnBrowse = new ModernButton { Text = "...", Location = new Point(345, y + labelH), Size = new Size(40, 36) };
        btnBrowse.Click += BrowseFile;
        Controls.Add(btnBrowse);

        y += 70;

        _chkStartup = new ModernCheckBox { Text = "Run at Windows Startup", Location = new Point(colLeft, y) };
        Controls.Add(_chkStartup);

        var btnSave = new ModernButton { Text = "Save & Minimize", Location = new Point(colLeft, y + 40), Size = new Size(360, 45), IsPrimary = true };
        btnSave.Click += SaveAndHide;
        Controls.Add(btnSave);
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
        string json = $$"""
        {
            "TopLeft": {{_cbTopLeft?.SelectedIndex ?? 0}},
            "TopRight": {{_cbTopRight?.SelectedIndex ?? 0}},
            "BottomLeft": {{_cbBottomLeft?.SelectedIndex ?? 0}},
            "BottomRight": {{_cbBottomRight?.SelectedIndex ?? 0}},
            "RunAtStartup": {{(_chkStartup?.Checked ?? false).ToString().ToLower()}},
            "Sensitivity": {{_numSensitivity?.Value ?? 10}},
            "Cooldown": {{_numCooldown?.Value ?? 500}},
            "TriggerDelay": {{_numTriggerDelay?.Value ?? 0}},
            "CustomAppPath": "{{_txtCustomPath?.InnerTextBox.Text.Replace("\\", "\\\\") ?? ""}}"
        }
        """;
        try {
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

        if ((DateTime.Now - _lastTriggerTime).TotalMilliseconds < (_numCooldown?.Value ?? 500)) return;

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
        int s = _numSensitivity?.Value ?? 10;

        bool tl = (cursor.X < s && cursor.Y < s);
        bool tr = (cursor.X > screen.Width - s && cursor.Y < s);
        bool bl = (cursor.X < s && cursor.Y > screen.Height - s);
        bool br = (cursor.X > screen.Width - s && cursor.Y > screen.Height - s);

        if (tl || tr || bl || br)
        {
            if (_isTriggered) return;

            if (_cornerEntryTime == null)
            {
                _cornerEntryTime = DateTime.Now;
            }

            int delay = _numTriggerDelay?.Value ?? 0;
            if ((DateTime.Now - _cornerEntryTime.Value).TotalMilliseconds >= delay)
            {
                _isTriggered = true;
                _lastTriggerTime = DateTime.Now;
                _cornerEntryTime = null;

                if (tl) ExecuteAction(_cbTopLeft?.SelectedIndex ?? 0);
                else if (tr) ExecuteAction(_cbTopRight?.SelectedIndex ?? 0);
                else if (bl) ExecuteAction(_cbBottomLeft?.SelectedIndex ?? 0);
                else if (br) ExecuteAction(_cbBottomRight?.SelectedIndex ?? 0);
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
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        if (hwnd == GetDesktopWindow() || hwnd == GetShellWindow()) return false;

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
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);
    [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_SCREENSAVE = 0xF140;
    private const int GWL_STYLE = -16;
    private const uint WS_CAPTION = 0x00C00000;

    private void ExecuteAction(int idx)
    {
        switch (idx)
        {
            case 1: SendKeysTriple(0x11, 0x10, 0x09); break; // Ctrl+Shift+Tab
            case 2: SendKeys(0x5B, 0x09); break; // Win+Tab
            case 3: try { Process.Start(new ProcessStartInfo(_txtCustomPath?.InnerTextBox.Text ?? "") { UseShellExecute = true }); } catch { } break;
            case 4: SendKeys(0x5B, 0x44); break; // Win+D
            case 5: LockWorkStation(); break;
            case 6: SendKeys(0x5B, 0x41); break; // Win+A
            case 7: SendKeys(0x5B, 0x45); break; // Win+E
            case 8: SendKeys(0x5B, 0x4D); break; // Win+M
            case 9: SendSingleKey(0x5B); break; // Win
            case 10: SendMessage(GetDesktopWindow(), WM_SYSCOMMAND, SC_SCREENSAVE, 0); break;
            case 11: SendKeysTriple(0x11, 0x10, 0x1B); break; // Ctrl+Shift+Esc
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
                if (_cbTopLeft != null) _cbTopLeft.SelectedIndex = GetVal(content, "TopLeft");
                if (_cbTopRight != null) _cbTopRight.SelectedIndex = GetVal(content, "TopRight");
                if (_cbBottomLeft != null) _cbBottomLeft.SelectedIndex = GetVal(content, "BottomLeft");
                if (_cbBottomRight != null) _cbBottomRight.SelectedIndex = GetVal(content, "BottomRight");
                if (_chkStartup != null) _chkStartup.Checked = content.Contains("\"RunAtStartup\": true");
                if (_numSensitivity != null) _numSensitivity.Value = GetVal(content, "Sensitivity");
                if (_numCooldown != null) _numCooldown.Value = GetVal(content, "Cooldown");
                if (_numTriggerDelay != null) _numTriggerDelay.Value = GetVal(content, "TriggerDelay");

                int start = content.IndexOf("CustomAppPath") + 16;
                int end = content.LastIndexOf("\"");
                if (start > 15 && end > start && _txtCustomPath != null)
                    _txtCustomPath.InnerTextBox.Text = content.Substring(start, end - start).Replace("\\\\", "\\").Trim('"');
            }
            catch { }
        }
    }

    private int GetVal(string json, string key)
    {
        try
        {
            int start = json.IndexOf(key) + key.Length + 2;
            int end = json.IndexOf(",", start);
            if (end == -1) end = json.IndexOf("}", start);
            return int.Parse(json.Substring(start, end - start).Trim());
        }
        catch { return 0; }
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

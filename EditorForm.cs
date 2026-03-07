using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Skjermbilde;

public class EditorForm : Form
{
    private Bitmap _original;
    private Bitmap _current;
    private readonly AppSettings _settings;
    private readonly float _dpiScale;

    // UI
    private PictureBox _pictureBox = null!;
    private Label _statusLabel = null!;
    private Panel _sidePanel = null!;
    private Label _numberPreviewLabel = null!;
    private FlowLayoutPanel _textSizePanel = null!;
    private Panel _numberSection = null!;
    private FlowLayoutPanel _colorGrid = null!;

    // Drawing state
    private Tool _tool = Tool.Number;
    private Color _drawColor = Color.FromArgb(239, 68, 68);
    private float _penWidth = 4f;
    private Point _lastPoint;
    private bool _drawing;
    private readonly List<DrawAction> _actions = new();
    private readonly List<DrawAction> _redoStack = new();
    private DrawAction? _currentAction;
    private int _numberCounter = 1;
    private float _textSize = 20f;

    // Text input
    private TextBox? _activeTextBox;

    // Crop
    private bool _cropping;
    private Rectangle _cropRect;
    private Panel? _cropBar;

    // Tool buttons for highlight
    private readonly List<Button> _toolButtons = new();

    // Color presets
    private static readonly Color[] Presets = {
        Color.FromArgb(239, 68, 68), Color.FromArgb(249, 115, 22),
        Color.FromArgb(234, 179, 8), Color.FromArgb(34, 197, 94),
        Color.FromArgb(59, 130, 246), Color.FromArgb(99, 102, 241),
        Color.White, Color.Black
    };

    public EditorForm(Bitmap screenshot, AppSettings settings)
    {
        _original = new Bitmap(screenshot);
        _current = new Bitmap(screenshot);
        _settings = settings;

        using var g = CreateGraphics();
        _dpiScale = g.DpiX / 96f;

        BuildUI();
        UpdateCanvas();
        HighlightActiveTool();
    }

    private int S(int px) => (int)(px * _dpiScale);

    private void BuildUI()
    {
        Text = "Skjermbilde.no - Redigering";
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Size = new Size(Math.Min(S(1280), wa.Width - 40), Math.Min(S(900), wa.Height - 40));
        MinimumSize = new Size(S(700), S(500));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(13, 13, 20);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        try
        {
            using var stream = typeof(EditorForm).Assembly.GetManifestResourceStream("Skjermbilde.assets.icon_32.png");
            if (stream != null) { using var bmp = new Bitmap(stream); Icon = Icon.FromHandle(bmp.GetHicon()); }
        }
        catch { }

        // ========== TOOLBAR (top) ==========
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = S(46),
            BackColor = Color.FromArgb(26, 26, 40),
            Padding = new Padding(S(6), S(4), S(6), S(4)),
            WrapContents = false
        };

        // Tool buttons with drawn icons
        AddTool(toolbar, Tool.Number, "Nummer (N)", DrawNumberIcon);
        AddTool(toolbar, Tool.Arrow, "Pil (A)", DrawArrowIcon);
        AddTool(toolbar, Tool.Rectangle, "Rektangel (R)", DrawRectIcon);
        AddTool(toolbar, Tool.Text, "Tekst (T)", DrawTextIcon);
        AddTool(toolbar, Tool.Blur, "Blur (B)", DrawBlurIcon);
        AddTool(toolbar, Tool.Crop, "Beskj\u00e6r (C)", DrawCropIcon);
        AddTool(toolbar, Tool.Pen, "Penn (P)", DrawPenIcon);
        AddTool(toolbar, Tool.Ellipse, "Ellipse (E)", DrawEllipseIcon);

        toolbar.Controls.Add(MakeSep());

        AddActionIcon(toolbar, "Angre (Ctrl+Z)", DrawUndoIcon, () => Undo());
        AddActionIcon(toolbar, "Gjenta (Ctrl+Y)", DrawRedoIcon, () => Redo());
        AddActionIcon(toolbar, "Slett alt", DrawDeleteIcon, () => ClearAll());

        toolbar.Controls.Add(MakeSep());

        // Size slider
        toolbar.Controls.Add(new Label
        {
            Text = "Str:",
            AutoSize = true,
            ForeColor = Color.FromArgb(170, 170, 190),
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(S(4), S(8), 0, 0)
        });
        var sizeTrack = new TrackBar
        {
            Minimum = 1, Maximum = 30, Value = 4,
            Width = S(90), Height = S(30),
            TickStyle = TickStyle.None,
            BackColor = Color.FromArgb(26, 26, 40),
            Margin = new Padding(0, S(2), 0, 0)
        };
        var sizeVal = new Label
        {
            Text = "4",
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 200, 220),
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, S(8), 0, 0)
        };
        sizeTrack.ValueChanged += (_, _) => { _penWidth = sizeTrack.Value; sizeVal.Text = sizeTrack.Value.ToString(); };
        toolbar.Controls.Add(sizeTrack);
        toolbar.Controls.Add(sizeVal);

        Controls.Add(toolbar);

        // ========== STATUS BAR (bottom) ==========
        var statusBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = S(24),
            BackColor = Color.FromArgb(18, 18, 28)
        };
        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(120, 120, 150),
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(S(8), S(3)),
            Text = $"{_current.Width} x {_current.Height}"
        };
        statusBar.Controls.Add(_statusLabel);
        Controls.Add(statusBar);

        // ========== SIDE PANEL (right) ==========
        _sidePanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = S(210),
            BackColor = Color.FromArgb(22, 22, 34),
            AutoScroll = true,
            Padding = new Padding(S(14), S(12), S(14), S(12))
        };

        var side = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(S(14), S(12), S(14), S(12))
        };

        // -- Colors --
        side.Controls.Add(SideLabel("Farger"));
        _colorGrid = new FlowLayoutPanel
        {
            Width = S(180), Height = S(44),
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, S(4))
        };
        foreach (var c in Presets)
        {
            var swatch = new Panel
            {
                Size = new Size(S(26), S(26)),
                BackColor = c,
                Margin = new Padding(S(2)),
                Cursor = Cursors.Hand
            };
            var cc = c;
            swatch.Paint += (_, pe) =>
            {
                if (cc == _drawColor)
                    using (var p = new Pen(cc == Color.White ? Color.FromArgb(37, 99, 235) : Color.White, 2))
                        pe.Graphics.DrawRectangle(p, 1, 1, swatch.Width - 3, swatch.Height - 3);
            };
            swatch.Click += (_, _) => { _drawColor = cc; _colorGrid.Invalidate(true); _numberPreviewLabel?.Invalidate(); };
            _colorGrid.Controls.Add(swatch);
        }
        side.Controls.Add(_colorGrid);

        var pickBtn = SideButton("Velg farge...", false);
        pickBtn.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _drawColor, FullOpen = true };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _drawColor = dlg.Color;
                _colorGrid.Invalidate(true);
                _numberPreviewLabel?.Invalidate();
            }
        };
        side.Controls.Add(pickBtn);

        // -- Number preview --
        _numberSection = new Panel
        {
            Width = S(180), Height = S(70),
            BackColor = Color.Transparent,
            Margin = new Padding(0, S(4), 0, S(4))
        };
        _numberSection.Controls.Add(new Label
        {
            Text = "Neste markering",
            AutoSize = true,
            ForeColor = Color.FromArgb(152, 152, 184),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Location = new Point(0, 0)
        });
        _numberPreviewLabel = new Label
        {
            Size = new Size(S(38), S(38)),
            Location = new Point(0, S(22)),
            BackColor = Color.Transparent
        };
        _numberPreviewLabel.Paint += (_, pe) =>
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var sz = _numberPreviewLabel.Width - 4;
            using var b = new SolidBrush(_drawColor);
            pe.Graphics.FillEllipse(b, 2, 2, sz, sz);
            using var wp = new Pen(Color.White, 2);
            pe.Graphics.DrawEllipse(wp, 2, 2, sz, sz);
            using var f = new Font("Segoe UI", sz * 0.38f, FontStyle.Bold);
            using var wb = new SolidBrush(Color.White);
            var t = _numberCounter.ToString();
            var ms = pe.Graphics.MeasureString(t, f);
            pe.Graphics.DrawString(t, f, wb, (sz + 4 - ms.Width) / 2, (sz + 4 - ms.Height) / 2);
        };
        _numberSection.Controls.Add(_numberPreviewLabel);

        var resetBtn = SideButton("Nullstill", false);
        resetBtn.Size = new Size(S(70), S(26));
        resetBtn.Location = new Point(S(48), S(26));
        resetBtn.Click += (_, _) => { _numberCounter = 1; _numberPreviewLabel.Invalidate(); };
        _numberSection.Controls.Add(resetBtn);
        side.Controls.Add(_numberSection);

        // -- Text size --
        side.Controls.Add(SideLabel("Tekststorrelse"));
        _textSizePanel = new FlowLayoutPanel
        {
            Width = S(180), Height = S(34),
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, S(8))
        };
        foreach (var (lbl, sz) in new[] { ("XS", 12f), ("S", 16f), ("M", 20f), ("L", 28f), ("XL", 36f) })
        {
            var tsb = new Button
            {
                Text = lbl,
                Size = new Size(S(32), S(28)),
                FlatStyle = FlatStyle.Flat,
                BackColor = sz == _textSize ? Color.FromArgb(37, 99, 235) : Color.FromArgb(30, 30, 45),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand,
                Tag = sz,
                Margin = new Padding(S(1))
            };
            tsb.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 70);
            tsb.Click += (s, _) =>
            {
                _textSize = (float)(s as Button)!.Tag!;
                foreach (Control c in _textSizePanel.Controls)
                    if (c is Button b) b.BackColor = (float)b.Tag! == _textSize
                        ? Color.FromArgb(37, 99, 235) : Color.FromArgb(30, 30, 45);
            };
            _textSizePanel.Controls.Add(tsb);
        }
        side.Controls.Add(_textSizePanel);

        // -- Separator --
        side.Controls.Add(new Panel { Width = S(180), Height = 1, BackColor = Color.FromArgb(45, 45, 65), Margin = new Padding(0, S(6), 0, S(10)) });

        // -- Action buttons --
        var copyBtn = SideButton("Kopier", false);
        copyBtn.BackColor = Color.FromArgb(20, 60, 45);
        copyBtn.FlatAppearance.BorderColor = Color.FromArgb(34, 197, 94);
        copyBtn.Click += (_, _) => CopyToClipboard();
        side.Controls.Add(copyBtn);

        var saveBtn = SideButton("Lagre lokalt", false);
        saveBtn.Click += (_, _) => SaveDialog();
        side.Controls.Add(saveBtn);

        var shareBtn = SideButton("Hurtigdeling", true);
        shareBtn.Click += async (_, _) => await QuickShareFromEditor();
        side.Controls.Add(shareBtn);

        var uploadBtn = SideButton("Last opp", false);
        uploadBtn.Click += async (_, _) => await UploadFromEditor();
        side.Controls.Add(uploadBtn);

        _sidePanel.Controls.Add(side);
        Controls.Add(_sidePanel);

        // ========== CANVAS (center, fill) ==========
        var canvasPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(13, 13, 20)
        };
        _pictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(13, 13, 20),
            Cursor = Cursors.Cross
        };
        _pictureBox.MouseDown += Canvas_MouseDown;
        _pictureBox.MouseMove += Canvas_MouseMove;
        _pictureBox.MouseUp += Canvas_MouseUp;
        canvasPanel.Controls.Add(_pictureBox);
        Controls.Add(canvasPanel);
        canvasPanel.BringToFront();
    }

    // ====== UI helpers ======

    private void AddTool(FlowLayoutPanel parent, Tool tool, string tooltip, Action<Graphics, Rectangle, Color> drawIcon)
    {
        var btn = MakeIconBtn(tooltip, drawIcon);
        btn.Tag = tool;
        btn.Click += (_, _) => { _tool = tool; HighlightActiveTool(); };
        _toolButtons.Add(btn);
        parent.Controls.Add(btn);
    }

    private void AddActionIcon(FlowLayoutPanel parent, string tooltip, Action<Graphics, Rectangle, Color> drawIcon, Action action)
    {
        var btn = MakeIconBtn(tooltip, drawIcon);
        btn.Click += (_, _) => action();
        parent.Controls.Add(btn);
    }

    private Button MakeIconBtn(string tooltip, Action<Graphics, Rectangle, Color> drawIcon)
    {
        var size = S(34);
        var btn = new Button
        {
            Size = new Size(size, size),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 30, 45),
            ForeColor = Color.FromArgb(210, 210, 230),
            Cursor = Cursors.Hand,
            Margin = new Padding(S(1)),
            FlatAppearance = { BorderColor = Color.FromArgb(45, 45, 60) }
        };
        var tt = new ToolTip();
        tt.SetToolTip(btn, tooltip);
        btn.Paint += (_, pe) =>
        {
            var active = btn.Tag is Tool t && t == _tool;
            var fg = active ? Color.White : Color.FromArgb(200, 200, 220);
            var iconRect = new Rectangle(size / 2 - S(9), size / 2 - S(9), S(18), S(18));
            drawIcon(pe.Graphics, iconRect, fg);
        };
        return btn;
    }

    // ====== Toolbar icon painters ======

    private static void DrawNumberIcon(Graphics g, Rectangle r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(c, 1.5f);
        g.DrawEllipse(pen, r);
        using var font = new Font("Segoe UI", r.Height * 0.45f, FontStyle.Bold);
        using var brush = new SolidBrush(c);
        var ts = g.MeasureString("1", font);
        g.DrawString("1", font, brush, r.X + (r.Width - ts.Width) / 2, r.Y + (r.Height - ts.Height) / 2);
    }

    private static void DrawArrowIcon(Graphics g, Rectangle r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(c, 2f) { CustomEndCap = new AdjustableArrowCap(4, 4) };
        g.DrawLine(pen, r.Left + 2, r.Bottom - 2, r.Right - 2, r.Top + 2);
    }

    private static void DrawRectIcon(Graphics g, Rectangle r, Color c)
    {
        using var pen = new Pen(c, 1.5f);
        g.DrawRectangle(pen, r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
    }

    private static void DrawTextIcon(Graphics g, Rectangle r, Color c)
    {
        using var font = new Font("Segoe UI", r.Height * 0.6f, FontStyle.Bold);
        using var brush = new SolidBrush(c);
        var ts = g.MeasureString("T", font);
        g.DrawString("T", font, brush, r.X + (r.Width - ts.Width) / 2, r.Y + (r.Height - ts.Height) / 2);
    }

    private static void DrawBlurIcon(Graphics g, Rectangle r, Color c)
    {
        using var pen = new Pen(Color.FromArgb(120, c), 1f);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                int x = r.X + 2 + i * (r.Width - 4) / 3;
                int y = r.Y + 2 + j * (r.Height - 4) / 3;
                int sz = (r.Width - 4) / 3 - 1;
                g.FillRectangle(new SolidBrush(Color.FromArgb(60 + j * 30, c)), x, y, sz, sz);
            }
    }

    private static void DrawCropIcon(Graphics g, Rectangle r, Color c)
    {
        using var pen = new Pen(c, 2f);
        int m = 3;
        // Two L-shaped crop marks
        g.DrawLine(pen, r.X + m, r.Bottom - m, r.X + m, r.Y + m + 4);
        g.DrawLine(pen, r.X + m, r.Bottom - m, r.Right - m - 4, r.Bottom - m);
        g.DrawLine(pen, r.Right - m, r.Y + m, r.Right - m, r.Bottom - m - 4);
        g.DrawLine(pen, r.Right - m, r.Y + m, r.X + m + 4, r.Y + m);
    }

    private static void DrawPenIcon(Graphics g, Rectangle r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(c, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var pts = new[] {
            new Point(r.X + 2, r.Bottom - 4),
            new Point(r.X + r.Width / 3, r.Y + r.Height / 3),
            new Point(r.X + r.Width * 2 / 3, r.Bottom - r.Height / 3),
            new Point(r.Right - 2, r.Y + 4)
        };
        g.DrawCurve(pen, pts, 0.5f);
    }

    private static void DrawEllipseIcon(Graphics g, Rectangle r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(c, 1.5f);
        g.DrawEllipse(pen, r.X + 1, r.Y + 3, r.Width - 2, r.Height - 6);
    }

    private static void DrawUndoIcon(Graphics g, Rectangle r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(c, 1.8f);
        var arc = new Rectangle(r.X + 3, r.Y + 3, r.Width - 4, r.Height - 4);
        g.DrawArc(pen, arc, 180, 230);
        // Arrow head
        int ax = r.X + 4, ay = r.Y + r.Height / 2;
        g.DrawLine(pen, ax, ay, ax + 4, ay - 4);
        g.DrawLine(pen, ax, ay, ax + 4, ay + 3);
    }

    private static void DrawRedoIcon(Graphics g, Rectangle r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(c, 1.8f);
        var arc = new Rectangle(r.X + 1, r.Y + 3, r.Width - 4, r.Height - 4);
        g.DrawArc(pen, arc, 0, -230);
        int ax = r.Right - 4, ay = r.Y + r.Height / 2;
        g.DrawLine(pen, ax, ay, ax - 4, ay - 4);
        g.DrawLine(pen, ax, ay, ax - 4, ay + 3);
    }

    private static void DrawDeleteIcon(Graphics g, Rectangle r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(220, 70, 70), 2f);
        int m = 4;
        g.DrawLine(pen, r.X + m, r.Y + m, r.Right - m, r.Bottom - m);
        g.DrawLine(pen, r.Right - m, r.Y + m, r.X + m, r.Bottom - m);
    }

    private Panel MakeSep() => new()
    {
        Size = new Size(1, S(30)),
        BackColor = Color.FromArgb(55, 55, 75),
        Margin = new Padding(S(4), S(4), S(4), 0)
    };

    private Label SideLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.FromArgb(152, 152, 184),
        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        Margin = new Padding(0, S(4), 0, S(4))
    };

    private Button SideButton(string text, bool primary)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(S(180), S(36)),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.FromArgb(30, 30, 45),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, S(2), 0, S(2))
        };
        btn.FlatAppearance.BorderColor = primary ? Color.FromArgb(59, 130, 246) : Color.FromArgb(50, 50, 70);
        return btn;
    }

    private void HighlightActiveTool()
    {
        foreach (var b in _toolButtons)
        {
            var active = b.Tag is Tool t && t == _tool;
            b.BackColor = active ? Color.FromArgb(37, 99, 235) : Color.FromArgb(30, 30, 45);
            b.Invalidate();
        }
        _numberSection.Visible = _tool == Tool.Number;
        _textSizePanel.Visible = _tool == Tool.Text;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var names = new Dictionary<Tool, string>
        {
            {Tool.None,""},  {Tool.Pen,"Penn"}, {Tool.Rectangle,"Rektangel"}, {Tool.Ellipse,"Ellipse"},
            {Tool.Arrow,"Pil"}, {Tool.Text,"Tekst"}, {Tool.Number,"Nummer"}, {Tool.Blur,"Blur"}, {Tool.Crop,"Crop"}
        };
        _statusLabel.Text = $"{_current.Width} x {_current.Height}  |  {names.GetValueOrDefault(_tool, "")}";
    }

    // ====== Canvas events ======

    private Point ScreenToImage(Point p)
    {
        if (_pictureBox.Image == null) return p;
        float rx = (float)_pictureBox.ClientSize.Width / _current.Width;
        float ry = (float)_pictureBox.ClientSize.Height / _current.Height;
        float r = Math.Min(rx, ry);
        float w = _current.Width * r, h = _current.Height * r;
        float ox = (_pictureBox.ClientSize.Width - w) / 2f;
        float oy = (_pictureBox.ClientSize.Height - h) / 2f;
        return new Point(
            Math.Clamp((int)((p.X - ox) / r), 0, _current.Width - 1),
            Math.Clamp((int)((p.Y - oy) / r), 0, _current.Height - 1));
    }

    private void Canvas_MouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (_tool == Tool.Text) { PlaceText(e.Location); return; }

        if (_tool == Tool.Number)
        {
            var pt = ScreenToImage(e.Location);
            _actions.Add(new DrawAction
            {
                Tool = Tool.Number, Color = _drawColor, PenWidth = _penWidth,
                StartPoint = pt, NumberValue = _numberCounter++
            });
            _redoStack.Clear();
            Redraw();
            _numberPreviewLabel?.Invalidate();
            return;
        }

        if (_tool == Tool.None) return;
        _drawing = true;
        _lastPoint = ScreenToImage(e.Location);
        _currentAction = new DrawAction
        {
            Tool = _tool, Color = _drawColor, PenWidth = _penWidth,
            StartPoint = _lastPoint, TextSize = _textSize
        };
    }

    private void Canvas_MouseMove(object? s, MouseEventArgs e)
    {
        var imgPt = ScreenToImage(e.Location);
        _statusLabel.Text = $"{imgPt.X}, {imgPt.Y}  |  {_current.Width} x {_current.Height}";

        if (!_drawing || _currentAction == null) return;

        if (_tool == Tool.Pen)
        {
            _currentAction.Points.Add(imgPt);
            using var g = Graphics.FromImage(_current);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(_drawColor, _penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, _lastPoint, imgPt);
            _lastPoint = imgPt;
            UpdateCanvas();
        }
        else
        {
            _currentAction.EndPoint = imgPt;
            // For crop, just preview the rect without full redraw of all actions
            if (_tool == Tool.Crop)
                RedrawFast();
            else
                RedrawWithPreview();
        }
    }

    private void Canvas_MouseUp(object? s, MouseEventArgs e)
    {
        if (!_drawing || _currentAction == null) return;
        _drawing = false;
        _currentAction.EndPoint = ScreenToImage(e.Location);

        if (_tool == Tool.Crop)
        {
            var rect = MakeRect(_currentAction.StartPoint, _currentAction.EndPoint);
            if (rect.Width > 5 && rect.Height > 5) ShowCropBar(rect);
            _currentAction = null;
            return;
        }

        _actions.Add(_currentAction);
        _redoStack.Clear();
        if (_tool != Tool.Pen) Redraw();
        _currentAction = null;
    }

    // ====== Text ======

    private void PlaceText(Point screenPt)
    {
        CommitText();
        var imgPt = ScreenToImage(screenPt);
        _activeTextBox = new TextBox
        {
            Location = screenPt,
            Font = new Font("Segoe UI", _textSize * 0.6f, FontStyle.Bold),
            BackColor = Color.FromArgb(30, 30, 45),
            ForeColor = _drawColor,
            BorderStyle = BorderStyle.None,
            Width = S(300),
            Tag = imgPt
        };
        _activeTextBox.KeyDown += (_, ke) =>
        {
            if (ke.KeyCode == Keys.Enter) { ke.SuppressKeyPress = true; CommitText(); }
            else if (ke.KeyCode == Keys.Escape) { _activeTextBox?.Dispose(); _activeTextBox = null; }
        };
        _pictureBox.Controls.Add(_activeTextBox);
        _activeTextBox.Focus();
    }

    private void CommitText()
    {
        if (_activeTextBox == null || string.IsNullOrWhiteSpace(_activeTextBox.Text))
        {
            _activeTextBox?.Dispose(); _activeTextBox = null; return;
        }
        _actions.Add(new DrawAction
        {
            Tool = Tool.Text, Color = _drawColor, StartPoint = (Point)_activeTextBox.Tag!,
            TextContent = _activeTextBox.Text, TextSize = _textSize
        });
        _redoStack.Clear();
        _activeTextBox.Dispose(); _activeTextBox = null;
        Redraw();
    }

    // ====== Crop ======

    private void ShowCropBar(Rectangle rect)
    {
        _cropping = true;
        _cropRect = rect;

        var cropLabel = new Label
        {
            Text = $"Beskj\u00e6r {rect.Width} x {rect.Height}?",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(S(10), S(10))
        };

        var applyBtn = new Button
        {
            Text = "Bruk (Enter)",
            Size = new Size(S(100), S(30)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand
        };
        applyBtn.FlatAppearance.BorderSize = 0;
        applyBtn.Click += (_, _) => ApplyCrop();

        var cancelBtn = new Button
        {
            Text = "Avbryt (Esc)",
            Size = new Size(S(100), S(30)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 30, 30),
            ForeColor = Color.FromArgb(240, 86, 86),
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand
        };
        cancelBtn.FlatAppearance.BorderColor = Color.FromArgb(240, 86, 86);
        cancelBtn.Click += (_, _) => CancelCrop();

        // Calculate positions based on actual label width
        using var tmpG = CreateGraphics();
        var labelWidth = (int)tmpG.MeasureString(cropLabel.Text, cropLabel.Font).Width + S(20);
        var barWidth = labelWidth + applyBtn.Width + S(8) + cancelBtn.Width + S(16);

        applyBtn.Location = new Point(labelWidth, S(6));
        cancelBtn.Location = new Point(labelWidth + applyBtn.Width + S(8), S(6));

        _cropBar = new Panel
        {
            Size = new Size(barWidth, S(42)),
            BackColor = Color.FromArgb(235, 26, 26, 40)
        };
        _cropBar.Location = new Point((_pictureBox.Width - _cropBar.Width) / 2, S(10));
        _cropBar.Controls.Add(cropLabel);
        _cropBar.Controls.Add(applyBtn);
        _cropBar.Controls.Add(cancelBtn);

        _pictureBox.Controls.Add(_cropBar);
        Redraw();
    }

    private void ApplyCrop()
    {
        if (!_cropping) return;
        var rect = Rectangle.Intersect(_cropRect, new Rectangle(0, 0, _current.Width, _current.Height));
        if (rect.Width < 2 || rect.Height < 2) { CancelCrop(); return; }

        var cropped = ScreenCapture.CropBitmap(_current, rect);
        _original.Dispose(); _current.Dispose();
        _original = new Bitmap(cropped);
        _current = new Bitmap(cropped);
        cropped.Dispose();
        _actions.Clear(); _redoStack.Clear();
        _numberCounter = 1; _numberPreviewLabel?.Invalidate();
        CancelCrop();
        UpdateCanvas();
        UpdateStatus();
    }

    private void CancelCrop()
    {
        _cropping = false;
        if (_cropBar != null)
        {
            _pictureBox.Controls.Remove(_cropBar);
            _cropBar.Dispose();
            _cropBar = null;
        }
        Redraw();
    }

    // ====== Drawing engine ======

    private void RedrawWithPreview()
    {
        var bmp = new Bitmap(_original);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var a in _actions) Paint(g, a);
        if (_currentAction != null) Paint(g, _currentAction);
        _current.Dispose();
        _current = bmp;
        UpdateCanvas();
    }

    private void RedrawFast()
    {
        // For crop preview: redraw from current + overlay, without rebuilding from original
        var bmp = new Bitmap(_original);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var a in _actions) Paint(g, a);
        if (_currentAction != null && _currentAction.Tool == Tool.Crop)
        {
            var r = MakeRect(_currentAction.StartPoint, _currentAction.EndPoint);
            DrawCropOverlay(g, bmp.Width, bmp.Height, r);
        }
        _current.Dispose();
        _current = bmp;
        UpdateCanvas();
    }

    private void Redraw()
    {
        var bmp = new Bitmap(_original);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var a in _actions) Paint(g, a);
        if (_cropping) DrawCropOverlay(g, bmp.Width, bmp.Height, _cropRect);
        _current.Dispose();
        _current = bmp;
        UpdateCanvas();
    }

    private void DrawCropOverlay(Graphics g, int w, int h, Rectangle r)
    {
        using var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
        g.FillRectangle(dim, 0, 0, w, r.Y);
        g.FillRectangle(dim, 0, r.Bottom, w, h - r.Bottom);
        g.FillRectangle(dim, 0, r.Y, r.X, r.Height);
        g.FillRectangle(dim, r.Right, r.Y, w - r.Right, r.Height);
        using var pen = new Pen(Color.FromArgb(37, 99, 235), 2) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, r);
    }

    private static void Paint(Graphics g, DrawAction a)
    {
        using var pen = new Pen(a.Color, a.PenWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(a.Color);

        switch (a.Tool)
        {
            case Tool.Pen:
                var pts = new List<Point> { a.StartPoint };
                pts.AddRange(a.Points);
                if (pts.Count >= 2)
                    for (int i = 1; i < pts.Count; i++)
                        g.DrawLine(pen, pts[i - 1], pts[i]);
                break;

            case Tool.Rectangle:
                pen.LineJoin = LineJoin.Round;
                g.DrawRectangle(pen, MakeRect(a.StartPoint, a.EndPoint));
                break;

            case Tool.Ellipse:
                g.DrawEllipse(pen, MakeRect(a.StartPoint, a.EndPoint));
                break;

            case Tool.Arrow:
                pen.CustomEndCap = new AdjustableArrowCap(6, 6);
                g.DrawLine(pen, a.StartPoint, a.EndPoint);
                break;

            case Tool.Text:
                if (!string.IsNullOrEmpty(a.TextContent))
                {
                    using var font = new Font("Segoe UI", a.TextSize, FontStyle.Bold);
                    var sz = g.MeasureString(a.TextContent, font);
                    using var bg = new SolidBrush(Color.FromArgb(180, 20, 20, 30));
                    g.FillRectangle(bg, a.StartPoint.X - 2, a.StartPoint.Y - 2, sz.Width + 4, sz.Height + 4);
                    g.DrawString(a.TextContent, font, brush, a.StartPoint);
                }
                break;

            case Tool.Number:
                var rad = Math.Max((int)(a.PenWidth * 2.5), 18);
                int cx = a.StartPoint.X, cy = a.StartPoint.Y;
                g.FillEllipse(brush, cx - rad, cy - rad, rad * 2, rad * 2);
                using (var wp = new Pen(Color.White, 2)) g.DrawEllipse(wp, cx - rad, cy - rad, rad * 2, rad * 2);
                using (var nf = new Font("Segoe UI", rad * 0.75f, FontStyle.Bold))
                {
                    var nt = a.NumberValue.ToString();
                    var ns = g.MeasureString(nt, nf);
                    using var wb = new SolidBrush(Color.White);
                    g.DrawString(nt, nf, wb, cx - ns.Width / 2, cy - ns.Height / 2);
                }
                break;

            case Tool.Blur:
                var br = MakeRect(a.StartPoint, a.EndPoint);
                if (br.Width > 2 && br.Height > 2)
                {
                    var psz = Math.Max(6, Math.Min(br.Width, br.Height) / 8);
                    for (int bx = br.X; bx < br.Right; bx += psz)
                        for (int by = br.Y; by < br.Bottom; by += psz)
                            using (var pb = new SolidBrush(Color.FromArgb(160, 30, 30, 40)))
                                g.FillRectangle(pb, bx, by,
                                    Math.Min(psz, br.Right - bx),
                                    Math.Min(psz, br.Bottom - by));
                }
                break;
        }
    }

    private static Rectangle MakeRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void UpdateCanvas() => _pictureBox.Image = _current;

    // ====== Actions ======

    private void Undo()
    {
        if (_actions.Count == 0) return;
        var last = _actions[^1];
        _actions.RemoveAt(_actions.Count - 1);
        _redoStack.Add(last);
        if (last.Tool == Tool.Number) _numberCounter = Math.Max(1, last.NumberValue);
        Redraw(); _numberPreviewLabel?.Invalidate();
    }

    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        var a = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _actions.Add(a);
        if (a.Tool == Tool.Number) _numberCounter = a.NumberValue + 1;
        Redraw(); _numberPreviewLabel?.Invalidate();
    }

    private void ClearAll()
    {
        if (_actions.Count == 0) return;
        if (MessageBox.Show("Slett alle markeringer?", "Skjermbilde.no",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _actions.Clear(); _redoStack.Clear();
        _numberCounter = 1; _numberPreviewLabel?.Invalidate();
        Redraw();
    }

    private void CopyToClipboard()
    {
        CommitText();
        Clipboard.SetImage(_current);
        Toast("Kopiert til utklippstavle!");
    }

    private void SaveDialog()
    {
        CommitText();
        using var dlg = new SaveFileDialog
        {
            FileName = _settings.GenerateLocalFilename(),
            Filter = "PNG|*.png|JPEG|*.jpg",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            var fmt = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Jpeg : ImageFormat.Png;
            _current.Save(dlg.FileName, fmt);
            Toast("Lagret!");
        }
    }

    private async System.Threading.Tasks.Task QuickShareFromEditor()
    {
        CommitText();
        var pngData = ScreenCapture.BitmapToPng(_current);
        var filename = _settings.GenerateLocalFilename();
        if (_settings.SaveLocal) SaveLocal(pngData, filename);

        Toast("Laster opp...");
        var result = await ApiClient.UploadScreenshot(_settings, pngData, filename);
        if (result.Success && result.ScreenshotId != null)
        {
            var url = await ApiClient.CreateShareLink(_settings, result.ScreenshotId);
            if (url != null) { Clipboard.SetText(url); Toast("Lenke kopiert!"); return; }
        }
        Toast(result.Error ?? "Feil ved opplasting", true);
    }

    private async System.Threading.Tasks.Task UploadFromEditor()
    {
        CommitText();
        var pngData = ScreenCapture.BitmapToPng(_current);
        var filename = _settings.GenerateLocalFilename();
        if (_settings.SaveLocal) SaveLocal(pngData, filename);

        Toast("Laster opp...");
        var result = await ApiClient.UploadScreenshot(_settings, pngData, filename);
        if (result.Success)
        {
            Toast("Lastet opp!");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _settings.ServerUrl + "/dashboard",
                UseShellExecute = true
            });
        }
        else
        {
            Toast(result.Error ?? "Feil ved opplasting", true);
        }
    }

    private void SaveLocal(byte[] data, string filename)
    {
        try
        {
            var dir = Path.Combine(_settings.LocalDir, $"{DateTime.Now:yyyy-MM}");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, filename), data);
        }
        catch { }
    }

    private void Toast(string msg, bool error = false)
    {
        var toast = new Panel { Size = new Size(S(260), S(38)), BackColor = Color.FromArgb(235, 22, 22, 34) };
        toast.Location = new Point((_pictureBox.Width - toast.Width) / 2, _pictureBox.Height - S(55));
        toast.Paint += (_, pe) =>
        {
            using var p = new Pen(error ? Color.FromArgb(240, 86, 86) : Color.FromArgb(34, 197, 94));
            pe.Graphics.DrawRectangle(p, 0, 0, toast.Width - 1, toast.Height - 1);
        };
        toast.Controls.Add(new Label
        {
            Text = msg, AutoSize = true, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f), Location = new Point(S(10), S(8))
        });
        _pictureBox.Controls.Add(toast);
        var timer = new Timer { Interval = 3000 };
        timer.Tick += (_, _) => { _pictureBox.Controls.Remove(toast); toast.Dispose(); timer.Dispose(); };
        timer.Start();
    }

    // ====== Keyboard ======

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.Y) { Redo(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.C) { CopyToClipboard(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.S) { SaveDialog(); e.Handled = true; }
        else if (e.KeyCode == Keys.Escape)
        {
            if (_cropping) CancelCrop();
            else if (_activeTextBox != null) { _activeTextBox.Dispose(); _activeTextBox = null; }
            else Close();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter && _cropping) { ApplyCrop(); e.Handled = true; }
        else if (!e.Control && !e.Alt && _activeTextBox == null)
        {
            switch (e.KeyCode)
            {
                case Keys.N: _tool = Tool.Number; HighlightActiveTool(); e.Handled = true; break;
                case Keys.A: _tool = Tool.Arrow; HighlightActiveTool(); e.Handled = true; break;
                case Keys.R: _tool = Tool.Rectangle; HighlightActiveTool(); e.Handled = true; break;
                case Keys.T: _tool = Tool.Text; HighlightActiveTool(); e.Handled = true; break;
                case Keys.B: _tool = Tool.Blur; HighlightActiveTool(); e.Handled = true; break;
                case Keys.C: _tool = Tool.Crop; HighlightActiveTool(); e.Handled = true; break;
                case Keys.P: _tool = Tool.Pen; HighlightActiveTool(); e.Handled = true; break;
                case Keys.E: _tool = Tool.Ellipse; HighlightActiveTool(); e.Handled = true; break;
            }
        }
        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _original.Dispose();
        _current.Dispose();
        base.OnFormClosed(e);
    }
}

public enum Tool { None, Pen, Rectangle, Ellipse, Arrow, Text, Number, Blur, Crop }

public class DrawAction
{
    public Tool Tool { get; set; }
    public Color Color { get; set; }
    public float PenWidth { get; set; }
    public Point StartPoint { get; set; }
    public Point EndPoint { get; set; }
    public List<Point> Points { get; set; } = new();
    public string? TextContent { get; set; }
    public float TextSize { get; set; } = 20f;
    public int NumberValue { get; set; }
}

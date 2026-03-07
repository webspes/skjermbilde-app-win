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

    // UI panels
    private Panel _headerPanel = null!;
    private Panel _toolbarPanel = null!;
    private Panel _sidePanel = null!;
    private Panel _statusPanel = null!;
    private PictureBox _pictureBox = null!;

    // Drawing state
    private Tool _tool = Tool.Number;
    private Color _drawColor = Color.Red;
    private float _penWidth = 4f;
    private Point _lastPoint;
    private bool _drawing;
    private readonly List<DrawAction> _actions = new();
    private readonly List<DrawAction> _redoStack = new();
    private DrawAction? _currentAction;
    private int _numberCounter = 1;

    // Text input
    private TextBox? _activeTextBox;

    // Crop state
    private bool _cropping;
    private Rectangle _cropRect;
    private Panel? _cropBar;

    // Zoom
    private float _zoomLevel = 1f;

    // UI references
    private Label? _statusLabel;
    private Label? _numberPreview;
    private Panel? _numberSection;
    private FlowLayoutPanel? _textSizeSection;
    private readonly List<Button> _toolButtons = new();

    // Color presets
    private static readonly Color[] ColorPresets = {
        Color.FromArgb(239, 68, 68),
        Color.FromArgb(249, 115, 22),
        Color.FromArgb(234, 179, 8),
        Color.FromArgb(34, 197, 94),
        Color.FromArgb(59, 130, 246),
        Color.FromArgb(99, 102, 241),
        Color.White,
        Color.Black
    };

    // Text size presets
    private static readonly (string Label, float Size)[] TextSizes = {
        ("XS", 12f), ("S", 16f), ("M", 20f), ("L", 28f), ("XL", 36f)
    };
    private float _textSize = 20f;
    private FlowLayoutPanel? _colorGrid;

    public EditorForm(Bitmap screenshot, AppSettings settings)
    {
        _original = new Bitmap(screenshot);
        _current = new Bitmap(screenshot);
        _settings = settings;
        InitializeComponents();
        UpdateCanvas();
        UpdateToolSelection();
    }

    private void InitializeComponents()
    {
        Text = "Skjermbilde.no - Redigering";
        Size = new Size(
            Math.Min(1280, Screen.PrimaryScreen!.WorkingArea.Width - 40),
            Math.Min(900, Screen.PrimaryScreen!.WorkingArea.Height - 40));
        MinimumSize = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(13, 13, 20);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        try
        {
            using var stream = typeof(EditorForm).Assembly.GetManifestResourceStream("Skjermbilde.assets.icon_32.png");
            if (stream != null) Icon = Icon.FromHandle(new Bitmap(stream).GetHicon());
        }
        catch { }

        // === Header ===
        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Color.FromArgb(22, 22, 34),
            Padding = new Padding(16, 0, 16, 0)
        };

        var logo = new Label
        {
            Text = "Skjermbilde.no",
            AutoSize = true,
            ForeColor = Color.FromArgb(240, 240, 255),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Location = new Point(16, 11)
        };
        _headerPanel.Controls.Add(logo);

        // Zoom controls (right side)
        var zoomPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent
        };

        AddHeaderButton(zoomPanel, "+", "Zoom inn (+)", () => SetZoom(_zoomLevel + 0.25f));
        AddHeaderButton(zoomPanel, "-", "Zoom ut (-)", () => SetZoom(_zoomLevel - 0.25f));
        AddHeaderButton(zoomPanel, "Tilpass", "Tilpass vindu (0)", () => SetZoom(1f));

        _headerPanel.Controls.Add(zoomPanel);
        _headerPanel.Resize += (_, _) => zoomPanel.Location = new Point(_headerPanel.Width - zoomPanel.PreferredSize.Width - 16, 6);
        zoomPanel.Location = new Point(_headerPanel.Width - 160, 6);

        Controls.Add(_headerPanel);

        // === Toolbar ===
        _toolbarPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = Color.FromArgb(26, 26, 40),
            Padding = new Padding(8, 6, 8, 6)
        };

        var toolFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            WrapContents = false
        };

        // Drawing tools
        AddToolButton(toolFlow, "1", "Nummer (N)", Tool.Number);
        AddToolButton(toolFlow, "\u2192", "Pil (A)", Tool.Arrow);
        AddToolButton(toolFlow, "\u25A1", "Rektangel (R)", Tool.Rectangle);
        AddToolButton(toolFlow, "T", "Tekst (T)", Tool.Text);
        AddToolButton(toolFlow, "\u2248", "Blur (B)", Tool.Blur);
        AddToolButton(toolFlow, "\u2702", "Beskjaer (C)", Tool.Crop);
        AddToolButton(toolFlow, "\u270E", "Penn", Tool.Pen);
        AddToolButton(toolFlow, "\u25CB", "Ellipse", Tool.Ellipse);

        AddToolSeparator(toolFlow);

        // Undo/Redo/Clear
        var undoBtn = MakeToolbarButton("\u21A9", "Angre (Ctrl+Z)");
        undoBtn.Click += (_, _) => Undo();
        toolFlow.Controls.Add(undoBtn);

        var redoBtn = MakeToolbarButton("\u21AA", "Gjenta (Ctrl+Y)");
        redoBtn.Click += (_, _) => Redo();
        toolFlow.Controls.Add(redoBtn);

        var clearBtn = MakeToolbarButton("\u2716", "Slett alle");
        clearBtn.Click += (_, _) => ClearAll();
        toolFlow.Controls.Add(clearBtn);

        AddToolSeparator(toolFlow);

        // Size control
        var sizeLabel = new Label
        {
            Text = "Str:",
            AutoSize = true,
            ForeColor = Color.FromArgb(152, 152, 184),
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(4, 9, 0, 0)
        };
        toolFlow.Controls.Add(sizeLabel);

        var sizeTrack = new TrackBar
        {
            Minimum = 1,
            Maximum = 30,
            Value = 4,
            Width = 100,
            Height = 32,
            TickStyle = TickStyle.None,
            BackColor = Color.FromArgb(26, 26, 40),
            Margin = new Padding(0, 2, 0, 0)
        };
        var sizeVal = new Label
        {
            Text = "4",
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 200, 220),
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(2, 9, 0, 0)
        };
        sizeTrack.ValueChanged += (_, _) => { _penWidth = sizeTrack.Value; sizeVal.Text = sizeTrack.Value.ToString(); };
        toolFlow.Controls.Add(sizeTrack);
        toolFlow.Controls.Add(sizeVal);

        _toolbarPanel.Controls.Add(toolFlow);
        Controls.Add(_toolbarPanel);

        // === Status bar ===
        _statusPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            BackColor = Color.FromArgb(18, 18, 28)
        };
        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(100, 100, 130),
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(8, 4),
            Text = "Klar"
        };
        _statusPanel.Controls.Add(_statusLabel);
        Controls.Add(_statusPanel);

        // === Side panel ===
        _sidePanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 200,
            BackColor = Color.FromArgb(22, 22, 34),
            Padding = new Padding(12, 12, 12, 12),
            AutoScroll = true
        };

        var sideLay = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = false,
            BackColor = Color.Transparent
        };

        // Color section
        sideLay.Controls.Add(MakeSideSectionLabel("Farger"));
        _colorGrid = new FlowLayoutPanel
        {
            Width = 170,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8)
        };
        foreach (var c in ColorPresets)
        {
            var colorBtn = new Panel
            {
                Size = new Size(28, 28),
                BackColor = c,
                Margin = new Padding(3),
                Cursor = Cursors.Hand
            };
            var capturedColor = c;
            colorBtn.Paint += (s, pe) =>
            {
                if (capturedColor == _drawColor)
                {
                    using var pen = new Pen(capturedColor == Color.White ? Color.FromArgb(37, 99, 235) : Color.White, 2);
                    pe.Graphics.DrawRectangle(pen, 1, 1, 24, 24);
                }
            };
            colorBtn.Click += (_, _) =>
            {
                _drawColor = capturedColor;
                _colorGrid!.Invalidate(true);
                UpdateNumberPreview();
            };
            _colorGrid.Controls.Add(colorBtn);
        }
        sideLay.Controls.Add(_colorGrid);

        // Custom color
        var customColorBtn = new Button
        {
            Text = "Velg farge...",
            Size = new Size(170, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 30, 45),
            ForeColor = Color.FromArgb(152, 152, 184),
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 12)
        };
        customColorBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 70);
        customColorBtn.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _drawColor, FullOpen = true };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _drawColor = dlg.Color;
                _colorGrid!.Invalidate(true);
                UpdateNumberPreview();
            }
        };
        sideLay.Controls.Add(customColorBtn);

        // Number section
        _numberSection = new Panel
        {
            Width = 170,
            Height = 80,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8)
        };
        var numLabel = MakeSideSectionLabel("Neste markering");
        numLabel.Location = new Point(0, 0);
        _numberSection.Controls.Add(numLabel);

        _numberPreview = new Label
        {
            Size = new Size(40, 40),
            Location = new Point(0, 24),
            BackColor = Color.Transparent
        };
        _numberPreview.Paint += (s, pe) =>
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(_drawColor);
            pe.Graphics.FillEllipse(b, 2, 2, 36, 36);
            using var borderPen = new Pen(Color.White, 2);
            pe.Graphics.DrawEllipse(borderPen, 2, 2, 36, 36);
            using var textBrush = new SolidBrush(Color.White);
            using var font = new Font("Segoe UI", 13f, FontStyle.Bold);
            var text = _numberCounter.ToString();
            var sz = pe.Graphics.MeasureString(text, font);
            pe.Graphics.DrawString(text, font, textBrush, (40 - sz.Width) / 2, (40 - sz.Height) / 2);
        };
        _numberSection.Controls.Add(_numberPreview);

        var resetNumBtn = new Button
        {
            Text = "Nullstill",
            Size = new Size(70, 28),
            Location = new Point(50, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 30, 45),
            ForeColor = Color.FromArgb(152, 152, 184),
            Font = new Font("Segoe UI", 8f),
            Cursor = Cursors.Hand
        };
        resetNumBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 70);
        resetNumBtn.Click += (_, _) => { _numberCounter = 1; UpdateNumberPreview(); };
        _numberSection.Controls.Add(resetNumBtn);
        sideLay.Controls.Add(_numberSection);

        // Text size section
        var tsSectionLabel = MakeSideSectionLabel("Tekststorrelse");
        sideLay.Controls.Add(tsSectionLabel);
        _textSizeSection = new FlowLayoutPanel
        {
            Width = 170,
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 12),
            Visible = false
        };
        foreach (var (label, size) in TextSizes)
        {
            var tsBtn = new Button
            {
                Text = label,
                Size = new Size(30, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = size == _textSize ? Color.FromArgb(37, 99, 235) : Color.FromArgb(30, 30, 45),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand,
                Tag = size,
                Margin = new Padding(1)
            };
            tsBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 70);
            tsBtn.Click += (s, _) =>
            {
                _textSize = (float)(s as Button)!.Tag!;
                foreach (Control c in _textSizeSection!.Controls)
                    if (c is Button btn) btn.BackColor = (float)btn.Tag! == _textSize
                        ? Color.FromArgb(37, 99, 235) : Color.FromArgb(30, 30, 45);
            };
            _textSizeSection.Controls.Add(tsBtn);
        }
        sideLay.Controls.Add(_textSizeSection);

        // Separator
        sideLay.Controls.Add(new Panel { Width = 170, Height = 1, BackColor = Color.FromArgb(40, 40, 60), Margin = new Padding(0, 8, 0, 12) });

        // Action buttons
        AddSideActionButton(sideLay, "Kopier", "copy");
        AddSideActionButton(sideLay, "Lagre lokalt", "save");
        AddSideActionButton(sideLay, "Hurtigdeling", "share");
        AddSideActionButton(sideLay, "Last opp", "upload");

        _sidePanel.Controls.Add(sideLay);
        Controls.Add(_sidePanel);

        // === Canvas ===
        var canvasContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(13, 13, 20)
        };

        _pictureBox = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(13, 13, 20),
            Cursor = Cursors.Cross,
            Dock = DockStyle.Fill
        };
        _pictureBox.MouseDown += PictureBox_MouseDown;
        _pictureBox.MouseMove += PictureBox_MouseMove;
        _pictureBox.MouseUp += PictureBox_MouseUp;

        canvasContainer.Controls.Add(_pictureBox);
        Controls.Add(canvasContainer);
        canvasContainer.BringToFront();
    }

    private void AddSideActionButton(FlowLayoutPanel parent, string text, string type)
    {
        Color bg, borderColor;
        switch (type)
        {
            case "copy":
                bg = Color.FromArgb(20, 60, 45);
                borderColor = Color.FromArgb(34, 197, 94);
                break;
            case "share":
                bg = Color.FromArgb(37, 99, 235);
                borderColor = Color.FromArgb(59, 130, 246);
                break;
            default:
                bg = Color.FromArgb(30, 30, 45);
                borderColor = Color.FromArgb(50, 50, 70);
                break;
        }

        var btn = new Button
        {
            Text = text,
            Size = new Size(170, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 2, 0, 2),
            TextAlign = ContentAlignment.MiddleCenter
        };
        btn.FlatAppearance.BorderColor = borderColor;

        switch (type)
        {
            case "copy": btn.Click += (_, _) => CopyToClipboard(); break;
            case "save": btn.Click += (_, _) => SaveDialog(); break;
            case "share": btn.Click += async (_, _) => await QuickShareFromEditor(); break;
            case "upload": btn.Click += async (_, _) => await UploadFromEditor(); break;
        }

        parent.Controls.Add(btn);
    }

    private void AddHeaderButton(FlowLayoutPanel parent, string text, string tooltip, Action onClick)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(text.Length > 2 ? 60 : 36, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 30, 45),
            ForeColor = Color.FromArgb(200, 200, 220),
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand,
            Margin = new Padding(2)
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 70);
        var tip = new ToolTip();
        tip.SetToolTip(btn, tooltip);
        btn.Click += (_, _) => onClick();
        parent.Controls.Add(btn);
    }

    private Button MakeToolbarButton(string text, string tooltip)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(36, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 30, 45),
            ForeColor = Color.FromArgb(200, 200, 220),
            Font = new Font("Segoe UI", 10f),
            Cursor = Cursors.Hand,
            Margin = new Padding(2)
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(40, 40, 55);
        var tip = new ToolTip();
        tip.SetToolTip(btn, tooltip);
        return btn;
    }

    private void AddToolButton(FlowLayoutPanel parent, string text, string tooltip, Tool tool)
    {
        var btn = MakeToolbarButton(text, tooltip);
        btn.Tag = tool;
        btn.Click += (_, _) => { _tool = tool; UpdateToolSelection(); };
        _toolButtons.Add(btn);
        parent.Controls.Add(btn);
    }

    private void AddToolSeparator(FlowLayoutPanel parent)
    {
        parent.Controls.Add(new Panel
        {
            Size = new Size(1, 34),
            BackColor = Color.FromArgb(50, 50, 70),
            Margin = new Padding(6, 2, 6, 0)
        });
    }

    private void UpdateToolSelection()
    {
        foreach (var btn in _toolButtons)
        {
            var isActive = btn.Tag is Tool t && t == _tool;
            btn.BackColor = isActive ? Color.FromArgb(37, 99, 235) : Color.FromArgb(30, 30, 45);
            btn.ForeColor = isActive ? Color.White : Color.FromArgb(200, 200, 220);
        }
        if (_numberSection != null) _numberSection.Visible = _tool == Tool.Number;
        if (_textSizeSection != null) _textSizeSection.Visible = _tool == Tool.Text;
        UpdateStatus();
    }

    private void UpdateNumberPreview()
    {
        _numberPreview?.Invalidate();
    }

    private static Label MakeSideSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.FromArgb(152, 152, 184),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 4)
        };
    }

    private void SetZoom(float level)
    {
        _zoomLevel = Math.Max(0.25f, Math.Min(4f, level));
        UpdateCanvas();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_statusLabel == null) return;
        var toolNames = new Dictionary<Tool, string>
        {
            { Tool.None, "Ingen" }, { Tool.Pen, "Penn" }, { Tool.Rectangle, "Rektangel" },
            { Tool.Ellipse, "Ellipse" }, { Tool.Arrow, "Pil" }, { Tool.Text, "Tekst" },
            { Tool.Number, "Nummer" }, { Tool.Blur, "Blur" }, { Tool.Crop, "Beskjaer" }
        };
        var toolName = toolNames.GetValueOrDefault(_tool, "Ukjent");
        _statusLabel.Text = $"{_current.Width} x {_current.Height} px  |  {toolName}  |  {(int)(_zoomLevel * 100)}%";
    }

    // ===== Drawing Logic =====

    private Point ScreenToImage(Point screenPt)
    {
        if (_pictureBox.Image == null) return screenPt;
        var imgRect = GetImageRect();
        var scaleX = (float)_current.Width / imgRect.Width;
        var scaleY = (float)_current.Height / imgRect.Height;
        return new Point(
            (int)((screenPt.X - imgRect.X) * scaleX),
            (int)((screenPt.Y - imgRect.Y) * scaleY));
    }

    private RectangleF GetImageRect()
    {
        if (_pictureBox.Image == null) return _pictureBox.ClientRectangle;
        float ratioX = (float)_pictureBox.ClientSize.Width / _current.Width;
        float ratioY = (float)_pictureBox.ClientSize.Height / _current.Height;
        float ratio = Math.Min(ratioX, ratioY);
        float w = _current.Width * ratio;
        float h = _current.Height * ratio;
        float x = (_pictureBox.ClientSize.Width - w) / 2f;
        float y = (_pictureBox.ClientSize.Height - h) / 2f;
        return new RectangleF(x, y, w, h);
    }

    private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (_tool == Tool.Text) { PlaceTextInput(e.Location); return; }

        if (_tool == Tool.Number)
        {
            var imgPt = ScreenToImage(e.Location);
            var action = new DrawAction
            {
                Tool = Tool.Number, Color = _drawColor, PenWidth = _penWidth,
                StartPoint = imgPt, NumberValue = _numberCounter++
            };
            _actions.Add(action);
            _redoStack.Clear();
            Redraw();
            UpdateNumberPreview();
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

    private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
    {
        // Update status with cursor position
        if (_pictureBox.Image != null)
        {
            var imgPt = ScreenToImage(e.Location);
            var imgRect = GetImageRect();
            if (imgRect.Contains(e.Location))
            {
                var toolNames = new Dictionary<Tool, string>
                {
                    { Tool.None, "Ingen" }, { Tool.Pen, "Penn" }, { Tool.Rectangle, "Rektangel" },
                    { Tool.Ellipse, "Ellipse" }, { Tool.Arrow, "Pil" }, { Tool.Text, "Tekst" },
                    { Tool.Number, "Nummer" }, { Tool.Blur, "Blur" }, { Tool.Crop, "Beskjaer" }
                };
                if (_statusLabel != null)
                    _statusLabel.Text = $"{imgPt.X}, {imgPt.Y}  |  {_current.Width} x {_current.Height}  |  {toolNames.GetValueOrDefault(_tool, "")}  |  {(int)(_zoomLevel * 100)}%";
            }
        }

        if (!_drawing || _currentAction == null) return;
        var pt = ScreenToImage(e.Location);

        if (_tool == Tool.Pen)
        {
            _currentAction.Points.Add(pt);
            using var g = Graphics.FromImage(_current);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(_drawColor, _penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, _lastPoint, pt);
            _lastPoint = pt;
            UpdateCanvas();
        }
        else
        {
            _currentAction.EndPoint = pt;
            RedrawWithPreview();
        }
    }

    private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
    {
        if (!_drawing || _currentAction == null) return;
        _drawing = false;
        _currentAction.EndPoint = ScreenToImage(e.Location);

        if (_tool == Tool.Crop)
        {
            var rect = GetRect(_currentAction.StartPoint, _currentAction.EndPoint);
            if (rect.Width > 5 && rect.Height > 5)
                ShowCropConfirmation(rect);
            _currentAction = null;
            return;
        }

        _actions.Add(_currentAction);
        _redoStack.Clear();
        if (_tool != Tool.Pen) Redraw();
        _currentAction = null;
    }

    private void PlaceTextInput(Point screenLocation)
    {
        CommitTextInput();
        var imgPt = ScreenToImage(screenLocation);

        _activeTextBox = new TextBox
        {
            Location = screenLocation,
            Font = new Font("Segoe UI", _textSize * 0.6f, FontStyle.Bold),
            BackColor = Color.FromArgb(30, 30, 45),
            ForeColor = _drawColor,
            BorderStyle = BorderStyle.None,
            Width = 300,
            Tag = imgPt
        };
        _activeTextBox.KeyDown += (s, ke) =>
        {
            if (ke.KeyCode == Keys.Enter) { ke.SuppressKeyPress = true; CommitTextInput(); }
            else if (ke.KeyCode == Keys.Escape) { _activeTextBox?.Dispose(); _activeTextBox = null; }
        };
        _pictureBox.Controls.Add(_activeTextBox);
        _activeTextBox.Focus();
    }

    private void CommitTextInput()
    {
        if (_activeTextBox == null || string.IsNullOrWhiteSpace(_activeTextBox.Text))
        {
            _activeTextBox?.Dispose();
            _activeTextBox = null;
            return;
        }
        var imgPt = (Point)_activeTextBox.Tag!;
        _actions.Add(new DrawAction
        {
            Tool = Tool.Text, Color = _drawColor, PenWidth = _penWidth,
            StartPoint = imgPt, TextContent = _activeTextBox.Text, TextSize = _textSize
        });
        _redoStack.Clear();
        _activeTextBox.Dispose();
        _activeTextBox = null;
        Redraw();
    }

    private void ShowCropConfirmation(Rectangle cropRect)
    {
        _cropping = true;
        _cropRect = cropRect;

        _cropBar = new Panel
        {
            Size = new Size(300, 44),
            BackColor = Color.FromArgb(230, 22, 22, 34)
        };
        _cropBar.Location = new Point((_pictureBox.Width - _cropBar.Width) / 2, 10);

        _cropBar.Controls.Add(new Label
        {
            Text = $"Beskjaer {cropRect.Width}x{cropRect.Height}?",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(10, 12)
        });

        var applyBtn = new Button
        {
            Text = "Bruk (Enter)",
            Size = new Size(90, 30),
            Location = new Point(185, 7),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand
        };
        applyBtn.FlatAppearance.BorderSize = 0;
        applyBtn.Click += (_, _) => ApplyCrop();
        _cropBar.Controls.Add(applyBtn);

        _pictureBox.Controls.Add(_cropBar);
        Redraw();
    }

    private void ApplyCrop()
    {
        if (!_cropping) return;
        var rect = Rectangle.Intersect(_cropRect, new Rectangle(0, 0, _current.Width, _current.Height));
        if (rect.Width < 2 || rect.Height < 2) { CancelCrop(); return; }

        var cropped = ScreenCapture.CropBitmap(_current, rect);
        _original.Dispose();
        _current.Dispose();
        _original = new Bitmap(cropped);
        _current = new Bitmap(cropped);
        cropped.Dispose();
        _actions.Clear();
        _redoStack.Clear();
        _numberCounter = 1;
        UpdateNumberPreview();
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

    private void RedrawWithPreview()
    {
        var bmp = new Bitmap(_original);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var a in _actions) DrawActionOnGraphics(g, a);
        if (_currentAction != null) DrawActionOnGraphics(g, _currentAction);
        if (_cropping) DrawCropOverlay(g, bmp.Width, bmp.Height);
        _current.Dispose();
        _current = bmp;
        UpdateCanvas();
    }

    private void Redraw()
    {
        var bmp = new Bitmap(_original);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var a in _actions) DrawActionOnGraphics(g, a);
        if (_cropping) DrawCropOverlay(g, bmp.Width, bmp.Height);
        _current.Dispose();
        _current = bmp;
        UpdateCanvas();
    }

    private void DrawCropOverlay(Graphics g, int width, int height)
    {
        using var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
        g.FillRectangle(dim, 0, 0, width, _cropRect.Y);
        g.FillRectangle(dim, 0, _cropRect.Bottom, width, height - _cropRect.Bottom);
        g.FillRectangle(dim, 0, _cropRect.Y, _cropRect.X, _cropRect.Height);
        g.FillRectangle(dim, _cropRect.Right, _cropRect.Y, width - _cropRect.Right, _cropRect.Height);
        using var border = new Pen(Color.FromArgb(37, 99, 235), 2) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(border, _cropRect);
    }

    private static void DrawActionOnGraphics(Graphics g, DrawAction a)
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
                g.DrawRectangle(pen, GetRect(a.StartPoint, a.EndPoint));
                break;

            case Tool.Ellipse:
                g.DrawEllipse(pen, GetRect(a.StartPoint, a.EndPoint));
                break;

            case Tool.Arrow:
                pen.CustomEndCap = new AdjustableArrowCap(6, 6);
                g.DrawLine(pen, a.StartPoint, a.EndPoint);
                break;

            case Tool.Text:
                if (!string.IsNullOrEmpty(a.TextContent))
                {
                    using var font = new Font("Segoe UI", a.TextSize, FontStyle.Bold);
                    var textSize = g.MeasureString(a.TextContent, font);
                    using var bgBrush = new SolidBrush(Color.FromArgb(180, 20, 20, 30));
                    g.FillRectangle(bgBrush, a.StartPoint.X - 2, a.StartPoint.Y - 2,
                        textSize.Width + 4, textSize.Height + 4);
                    g.DrawString(a.TextContent, font, brush, a.StartPoint);
                }
                break;

            case Tool.Number:
                var radius = Math.Max((int)(a.PenWidth * 2), 16);
                var cx = a.StartPoint.X;
                var cy = a.StartPoint.Y;
                g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
                using (var wp = new Pen(Color.White, 2))
                    g.DrawEllipse(wp, cx - radius, cy - radius, radius * 2, radius * 2);
                using (var nf = new Font("Segoe UI", radius * 0.8f, FontStyle.Bold))
                {
                    var nt = a.NumberValue.ToString();
                    var ns = g.MeasureString(nt, nf);
                    using var wb = new SolidBrush(Color.White);
                    g.DrawString(nt, nf, wb, cx - ns.Width / 2, cy - ns.Height / 2);
                }
                break;

            case Tool.Blur:
                var blurRect = GetRect(a.StartPoint, a.EndPoint);
                if (blurRect.Width > 2 && blurRect.Height > 2)
                {
                    var pixelSize = Math.Max(8, (int)(blurRect.Width / 12));
                    for (int bx = blurRect.X; bx < blurRect.Right; bx += pixelSize)
                        for (int by = blurRect.Y; by < blurRect.Bottom; by += pixelSize)
                        {
                            var pw = Math.Min(pixelSize, blurRect.Right - bx);
                            var ph = Math.Min(pixelSize, blurRect.Bottom - by);
                            using var pb = new SolidBrush(Color.FromArgb(140, 40, 40, 50));
                            g.FillRectangle(pb, bx, by, pw, ph);
                        }
                }
                break;
        }
    }

    private static Rectangle GetRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void UpdateCanvas() => _pictureBox.Image = _current;

    private void Undo()
    {
        if (_actions.Count == 0) return;
        var last = _actions[^1];
        _actions.RemoveAt(_actions.Count - 1);
        _redoStack.Add(last);
        if (last.Tool == Tool.Number)
            _numberCounter = Math.Max(1, last.NumberValue);
        Redraw();
        UpdateNumberPreview();
    }

    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        var action = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _actions.Add(action);
        if (action.Tool == Tool.Number)
            _numberCounter = action.NumberValue + 1;
        Redraw();
        UpdateNumberPreview();
    }

    private void ClearAll()
    {
        if (_actions.Count == 0) return;
        if (MessageBox.Show("Slett alle markeringer?", "Skjermbilde.no",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _actions.Clear();
        _redoStack.Clear();
        _numberCounter = 1;
        UpdateNumberPreview();
        Redraw();
    }

    private void CopyToClipboard()
    {
        CommitTextInput();
        Clipboard.SetImage(_current);
        ShowToast("Kopiert til utklippstavle!");
    }

    private void SaveDialog()
    {
        CommitTextInput();
        using var dlg = new SaveFileDialog
        {
            FileName = _settings.GenerateLocalFilename(),
            Filter = "PNG-bilde|*.png|JPEG|*.jpg",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            var fmt = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Jpeg : ImageFormat.Png;
            _current.Save(dlg.FileName, fmt);
            ShowToast("Lagret!");
        }
    }

    private async System.Threading.Tasks.Task QuickShareFromEditor()
    {
        CommitTextInput();
        ShowUploadProgress("Laster opp...");

        var pngData = ScreenCapture.BitmapToPng(_current);
        var filename = _settings.GenerateLocalFilename();
        if (_settings.SaveLocal) SaveLocal(pngData, filename);

        var result = await ApiClient.UploadScreenshot(_settings, pngData, filename);
        if (result.Success && result.ScreenshotId != null)
        {
            UpdateUploadProgress("Oppretter delingslenke...");
            var shareUrl = await ApiClient.CreateShareLink(_settings, result.ScreenshotId);
            if (shareUrl != null)
            {
                Clipboard.SetText(shareUrl);
                HideUploadProgress();
                ShowToast("Lenke kopiert!");
                return;
            }
        }
        HideUploadProgress();
        ShowToast(result.Error ?? "Opplasting feilet", true);
    }

    private async System.Threading.Tasks.Task UploadFromEditor()
    {
        CommitTextInput();
        ShowUploadProgress("Laster opp...");

        var pngData = ScreenCapture.BitmapToPng(_current);
        var filename = _settings.GenerateLocalFilename();
        if (_settings.SaveLocal) SaveLocal(pngData, filename);

        var result = await ApiClient.UploadScreenshot(_settings, pngData, filename);
        HideUploadProgress();
        ShowToast(result.Success ? "Lastet opp!" : (result.Error ?? "Opplasting feilet"), !result.Success);
    }

    private void SaveLocal(byte[] pngData, string filename)
    {
        try
        {
            var now = DateTime.Now;
            var dir = Path.Combine(_settings.LocalDir, $"{now.Year}-{now.Month:D2}");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, filename), pngData);
        }
        catch { }
    }

    // Upload progress
    private Panel? _uploadPanel;

    private void ShowUploadProgress(string text)
    {
        _uploadPanel?.Dispose();
        _uploadPanel = new Panel
        {
            Size = new Size(260, 50),
            BackColor = Color.FromArgb(230, 22, 22, 34)
        };
        _uploadPanel.Location = new Point((_pictureBox.Width - _uploadPanel.Width) / 2, _pictureBox.Height - 70);

        var label = new Label
        {
            Text = text, AutoSize = true, ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f), Location = new Point(12, 8)
        };
        _uploadPanel.Controls.Add(label);
        _uploadPanel.Tag = label;

        var progressBg = new Panel
        {
            Size = new Size(236, 4), Location = new Point(12, 34),
            BackColor = Color.FromArgb(40, 40, 60)
        };
        progressBg.Controls.Add(new Panel { Size = new Size(80, 4), BackColor = Color.FromArgb(37, 99, 235) });
        _uploadPanel.Controls.Add(progressBg);

        _pictureBox.Controls.Add(_uploadPanel);
    }

    private void UpdateUploadProgress(string text)
    {
        if (_uploadPanel?.Tag is Label label) label.Text = text;
    }

    private void HideUploadProgress()
    {
        if (_uploadPanel != null)
        {
            _pictureBox.Controls.Remove(_uploadPanel);
            _uploadPanel.Dispose();
            _uploadPanel = null;
        }
    }

    private void ShowToast(string message, bool isError = false)
    {
        var toast = new Panel { Size = new Size(260, 40), BackColor = Color.FromArgb(230, 22, 22, 34) };
        toast.Location = new Point((_pictureBox.Width - toast.Width) / 2, _pictureBox.Height - 60);
        toast.Paint += (_, pe) =>
        {
            using var bp = new Pen(isError ? Color.FromArgb(240, 86, 86) : Color.FromArgb(34, 197, 94), 1);
            pe.Graphics.DrawRectangle(bp, 0, 0, toast.Width - 1, toast.Height - 1);
        };
        toast.Controls.Add(new Label
        {
            Text = message, AutoSize = true, ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f), Location = new Point(12, 10)
        });
        _pictureBox.Controls.Add(toast);

        var timer = new Timer { Interval = 3000 };
        timer.Tick += (_, _) => { _pictureBox.Controls.Remove(toast); toast.Dispose(); timer.Dispose(); };
        timer.Start();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.Y) { Redo(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.C) { CopyToClipboard(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.S) { SaveDialog(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.U) { _ = UploadFromEditor(); e.Handled = true; }
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
                case Keys.N: _tool = Tool.Number; UpdateToolSelection(); e.Handled = true; break;
                case Keys.A: _tool = Tool.Arrow; UpdateToolSelection(); e.Handled = true; break;
                case Keys.R: _tool = Tool.Rectangle; UpdateToolSelection(); e.Handled = true; break;
                case Keys.T: _tool = Tool.Text; UpdateToolSelection(); e.Handled = true; break;
                case Keys.B: _tool = Tool.Blur; UpdateToolSelection(); e.Handled = true; break;
                case Keys.C: _tool = Tool.Crop; UpdateToolSelection(); e.Handled = true; break;
                case Keys.Oemplus: case Keys.Add: SetZoom(_zoomLevel + 0.25f); e.Handled = true; break;
                case Keys.OemMinus: case Keys.Subtract: SetZoom(_zoomLevel - 0.25f); e.Handled = true; break;
                case Keys.D0: SetZoom(1f); e.Handled = true; break;
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

public class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Color.FromArgb(22, 22, 34));
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is ToolStripButton btn && btn.Checked)
        {
            using var brush = new SolidBrush(Color.FromArgb(37, 99, 235));
            e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
        }
        else if (e.Item.Selected)
        {
            using var brush = new SolidBrush(Color.FromArgb(40, 40, 60));
            e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
        }
    }
}

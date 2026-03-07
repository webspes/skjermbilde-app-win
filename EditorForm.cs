using System;
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
    private PictureBox _pictureBox = null!;
    private ToolStrip _toolbar = null!;

    // Drawing state
    private Tool _tool = Tool.None;
    private Color _drawColor = Color.Red;
    private float _penWidth = 3f;
    private Point _lastPoint;
    private bool _drawing;
    private readonly System.Collections.Generic.List<DrawAction> _actions = new();
    private DrawAction? _currentAction;

    public EditorForm(Bitmap screenshot, AppSettings settings)
    {
        _original = new Bitmap(screenshot);
        _current = new Bitmap(screenshot);
        _settings = settings;
        InitializeComponents();
        UpdateCanvas();
    }

    private void InitializeComponents()
    {
        Text = "Skjermbilde.no – Redigering";
        Size = new Size(
            Math.Min(1200, Screen.PrimaryScreen!.WorkingArea.Width - 40),
            Math.Min(800, Screen.PrimaryScreen!.WorkingArea.Height - 40));
        MinimumSize = new Size(600, 400);
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

        // Toolbar
        _toolbar = new ToolStrip
        {
            BackColor = Color.FromArgb(22, 22, 34),
            ForeColor = Color.White,
            GripStyle = ToolStripGripStyle.Hidden,
            Renderer = new DarkToolStripRenderer(),
            Padding = new Padding(8, 4, 8, 4)
        };

        AddToolButton("🖊️ Penn", Tool.Pen);
        AddToolButton("⬜ Rektangel", Tool.Rectangle);
        AddToolButton("⭕ Ellipse", Tool.Ellipse);
        AddToolButton("➡️ Pil", Tool.Arrow);
        AddToolButton("T Tekst", Tool.Text);
        _toolbar.Items.Add(new ToolStripSeparator());

        var colorBtn = new ToolStripButton("🔴") { ToolTipText = "Velg farge" };
        colorBtn.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _drawColor, FullOpen = true };
            if (dlg.ShowDialog() == DialogResult.OK) _drawColor = dlg.Color;
        };
        _toolbar.Items.Add(colorBtn);

        _toolbar.Items.Add(new ToolStripSeparator());

        var undoBtn = new ToolStripButton("↩️ Angre") { ToolTipText = "Angre (Ctrl+Z)" };
        undoBtn.Click += (_, _) => Undo();
        _toolbar.Items.Add(undoBtn);

        _toolbar.Items.Add(new ToolStripSeparator());

        var copyBtn = new ToolStripButton("📋 Kopier");
        copyBtn.Click += (_, _) => CopyToClipboard();
        _toolbar.Items.Add(copyBtn);

        var saveBtn = new ToolStripButton("💾 Lagre");
        saveBtn.Click += (_, _) => SaveDialog();
        _toolbar.Items.Add(saveBtn);

        var uploadBtn = new ToolStripButton("☁️ Last opp");
        uploadBtn.Click += async (_, _) => await UploadImage();
        _toolbar.Items.Add(uploadBtn);

        Controls.Add(_toolbar);

        // Image display
        _pictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(13, 13, 20)
        };
        _pictureBox.MouseDown += PictureBox_MouseDown;
        _pictureBox.MouseMove += PictureBox_MouseMove;
        _pictureBox.MouseUp += PictureBox_MouseUp;

        Controls.Add(_pictureBox);
        _pictureBox.BringToFront();
    }

    private void AddToolButton(string text, Tool tool)
    {
        var btn = new ToolStripButton(text) { Tag = tool };
        btn.Click += (_, _) =>
        {
            _tool = tool;
            foreach (ToolStripItem item in _toolbar.Items)
                if (item is ToolStripButton b && b.Tag is Tool)
                    b.Checked = (Tool)b.Tag == tool;
        };
        _toolbar.Items.Add(btn);
    }

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
        if (_tool == Tool.None || e.Button != MouseButtons.Left) return;
        _drawing = true;
        _lastPoint = ScreenToImage(e.Location);
        _currentAction = new DrawAction { Tool = _tool, Color = _drawColor, PenWidth = _penWidth, StartPoint = _lastPoint };
    }

    private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_drawing || _currentAction == null) return;
        var imgPt = ScreenToImage(e.Location);

        if (_tool == Tool.Pen)
        {
            _currentAction.Points.Add(imgPt);
            // Draw directly for real-time feedback
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
            // Redraw with preview
            RedrawWithPreview();
        }
    }

    private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
    {
        if (!_drawing || _currentAction == null) return;
        _drawing = false;
        _currentAction.EndPoint = ScreenToImage(e.Location);
        _actions.Add(_currentAction);

        if (_tool != Tool.Pen)
        {
            // Apply the action to _current
            Redraw();
        }

        _currentAction = null;
    }

    private void RedrawWithPreview()
    {
        var bmp = new Bitmap(_original);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var a in _actions) DrawActionOnGraphics(g, a);
        if (_currentAction != null) DrawActionOnGraphics(g, _currentAction);
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
        _current.Dispose();
        _current = bmp;
        UpdateCanvas();
    }

    private static void DrawActionOnGraphics(Graphics g, DrawAction a)
    {
        using var pen = new Pen(a.Color, a.PenWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(a.Color);

        switch (a.Tool)
        {
            case Tool.Pen:
                var pts = new System.Collections.Generic.List<Point> { a.StartPoint };
                pts.AddRange(a.Points);
                if (pts.Count >= 2)
                {
                    for (int i = 1; i < pts.Count; i++)
                        g.DrawLine(pen, pts[i - 1], pts[i]);
                }
                break;
            case Tool.Rectangle:
                var rect = GetRect(a.StartPoint, a.EndPoint);
                g.DrawRectangle(pen, rect);
                break;
            case Tool.Ellipse:
                var erect = GetRect(a.StartPoint, a.EndPoint);
                g.DrawEllipse(pen, erect);
                break;
            case Tool.Arrow:
                pen.CustomEndCap = new AdjustableArrowCap(6, 6);
                g.DrawLine(pen, a.StartPoint, a.EndPoint);
                break;
            case Tool.Text:
                using (var font = new Font("Segoe UI", a.PenWidth * 6))
                    g.DrawString("Text", font, brush, a.StartPoint);
                break;
        }
    }

    private static Rectangle GetRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void UpdateCanvas()
    {
        _pictureBox.Image = _current;
    }

    private void Undo()
    {
        if (_actions.Count == 0) return;
        _actions.RemoveAt(_actions.Count - 1);
        Redraw();
    }

    private void CopyToClipboard()
    {
        Clipboard.SetImage(_current);
        MessageBox.Show("Kopiert til utklippstavle!", "Skjermbilde.no", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveDialog()
    {
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
        }
    }

    private async System.Threading.Tasks.Task UploadImage()
    {
        var pngData = ScreenCapture.BitmapToPng(_current);
        var filename = _settings.GenerateLocalFilename();

        // Save locally if enabled
        if (_settings.SaveLocal)
        {
            var now = DateTime.Now;
            var monthDir = $"{now.Year}-{now.Month:D2}";
            var dir = Path.Combine(_settings.LocalDir, monthDir);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, filename), pngData);
        }

        var result = await ApiClient.UploadScreenshot(_settings, pngData, filename);
        if (result.Success)
        {
            // Create share link
            if (result.ScreenshotId != null)
            {
                var shareUrl = await ApiClient.CreateShareLink(_settings, result.ScreenshotId);
                if (shareUrl != null)
                {
                    Clipboard.SetText(shareUrl);
                    MessageBox.Show("Lastet opp og delingslenke kopiert!", "Skjermbilde.no",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                    return;
                }
            }
            MessageBox.Show("Lastet opp!", "Skjermbilde.no", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        else
        {
            MessageBox.Show(result.Error ?? "Ukjent feil", "Opplasting feilet", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Z) Undo();
        else if (e.Control && e.KeyCode == Keys.C) CopyToClipboard();
        else if (e.Control && e.KeyCode == Keys.S) SaveDialog();
        else if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _original.Dispose();
        _current.Dispose();
        base.OnFormClosed(e);
    }
}

public enum Tool { None, Pen, Rectangle, Ellipse, Arrow, Text }

public class DrawAction
{
    public Tool Tool { get; set; }
    public Color Color { get; set; }
    public float PenWidth { get; set; }
    public Point StartPoint { get; set; }
    public Point EndPoint { get; set; }
    public System.Collections.Generic.List<Point> Points { get; set; } = new();
}

public class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(22, 22, 34)), e.AffectedBounds);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is ToolStripButton btn && btn.Checked)
        {
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(37, 99, 235)), e.Item.ContentRectangle);
        }
        else if (e.Item.Selected)
        {
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(40, 40, 60)), e.Item.ContentRectangle);
        }
    }
}

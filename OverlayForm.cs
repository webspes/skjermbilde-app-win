using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Skjermbilde;

public class OverlayForm : Form
{
    private readonly Bitmap _background;
    private readonly float _dpiScale;
    private Point _startPoint;
    private Rectangle _selection;
    private bool _selecting;
    private bool _hasSelection;
    private int _hoveredButton = -1;

    public Rectangle SelectedArea => _selection;
    public OverlayAction Action { get; private set; } = OverlayAction.Cancel;

    // DPI-aware pixel helper
    private int S(int px) => (int)(px * _dpiScale);
    private float Sf(float px) => px * _dpiScale;

    // Colors
    private static readonly Color AccentBlue = Color.FromArgb(37, 99, 235);
    private static readonly Color AccentBlueHover = Color.FromArgb(59, 130, 246);
    private static readonly Color DangerRed = Color.FromArgb(220, 70, 70);
    private static readonly Color BtnBg = Color.FromArgb(235, 20, 20, 28);
    private static readonly Color BtnBgHover = Color.FromArgb(245, 40, 40, 55);
    private static readonly Color BtnBorder = Color.FromArgb(60, 255, 255, 255);
    private static readonly Color ToolbarBg = Color.FromArgb(240, 18, 18, 26);
    private static readonly Color ToolbarBorder = Color.FromArgb(50, 255, 255, 255);
    private static readonly Color TextWhite = Color.FromArgb(240, 255, 255, 255);

    private record struct ButtonDef(string Label, OverlayAction Action, ButtonStyle Style);
    private enum ButtonStyle { Default, Primary, Danger, Record }

    private readonly ButtonDef[] _buttons = {
        new("Avbryt", OverlayAction.Cancel, ButtonStyle.Danger),
        new("Kopier", OverlayAction.Copy, ButtonStyle.Default),
        new("Hurtigdeling", OverlayAction.QuickShare, ButtonStyle.Default),
        new("Rediger", OverlayAction.Edit, ButtonStyle.Default),
        new("Last opp", OverlayAction.Upload, ButtonStyle.Primary),
        new("Opptak", OverlayAction.Record, ButtonStyle.Record),
    };

    public OverlayForm(Bitmap background)
    {
        _background = background;

        // Determine DPI scale
        using var g = CreateGraphics();
        _dpiScale = g.DpiX / 96f;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        var screen = Screen.PrimaryScreen!.Bounds;
        Location = screen.Location;
        Size = screen.Size;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        BackgroundImage = _background;
        BackgroundImageLayout = ImageLayout.Stretch;
        KeyPreview = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Dim the entire screen
        using var dimBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
        g.FillRectangle(dimBrush, ClientRectangle);

        if (_hasSelection && _selection.Width > 0 && _selection.Height > 0)
        {
            // Draw the selected area (clear the dimming)
            g.SetClip(_selection);
            g.DrawImage(_background, ClientRectangle);
            g.ResetClip();

            // Selection border
            using var pen = new Pen(AccentBlue, Sf(2));
            g.DrawRectangle(pen, _selection);

            // Corner handles
            var hs = S(10);
            DrawCornerHandle(g, _selection.X - hs / 2, _selection.Y - hs / 2, hs);
            DrawCornerHandle(g, _selection.Right - hs / 2, _selection.Y - hs / 2, hs);
            DrawCornerHandle(g, _selection.X - hs / 2, _selection.Bottom - hs / 2, hs);
            DrawCornerHandle(g, _selection.Right - hs / 2, _selection.Bottom - hs / 2, hs);

            // Size label above selection
            var sizeText = $"{_selection.Width} x {_selection.Height} px";
            using var sizeFont = new Font("Segoe UI", Sf(11f), FontStyle.Regular);
            var textSize = g.MeasureString(sizeText, sizeFont);
            var labelX = _selection.X;
            var labelY = _selection.Y - textSize.Height - S(14);
            if (labelY < 4) labelY = _selection.Bottom + S(8);

            var labelRect = new RectangleF(labelX, labelY, textSize.Width + S(20), textSize.Height + S(8));
            using var labelPath = RoundedRect(labelRect, S(6));
            using var labelBg = new SolidBrush(Color.FromArgb(220, 13, 13, 20));
            g.FillPath(labelBg, labelPath);
            using var labelBorder = new Pen(ToolbarBorder);
            g.DrawPath(labelBorder, labelPath);
            using var textBrush = new SolidBrush(TextWhite);
            g.DrawString(sizeText, sizeFont, textBrush, labelRect.X + S(10), labelRect.Y + S(4));
        }

        // Help text when no selection
        if (!_selecting && !_hasSelection)
        {
            DrawHelpBar(g);
        }

        // Action toolbar when selection is done
        if (_hasSelection && !_selecting && _selection.Width > 10 && _selection.Height > 10)
        {
            DrawActionToolbar(g);
        }
    }

    private void DrawCornerHandle(Graphics g, int x, int y, int size)
    {
        using var brush = new SolidBrush(AccentBlue);
        using var pen = new Pen(Color.White, Sf(1.5f));
        g.FillRectangle(brush, x, y, size, size);
        g.DrawRectangle(pen, x, y, size, size);
    }

    private void DrawHelpBar(Graphics g)
    {
        var helpText = "Dra for \u00e5 velge omr\u00e5de  \u00b7  ESC avbryt";
        using var helpFont = new Font("Segoe UI", Sf(13f));
        var helpSize = g.MeasureString(helpText, helpFont);
        var barWidth = helpSize.Width + S(60);
        var barHeight = helpSize.Height + S(20);
        var barX = (Width - barWidth) / 2;
        var barY = Height - barHeight - S(40);

        var barRect = new RectangleF(barX, barY, barWidth, barHeight);
        using var barPath = RoundedRect(barRect, S(24));
        using var barBg = new SolidBrush(ToolbarBg);
        g.FillPath(barBg, barPath);
        using var barBorder = new Pen(ToolbarBorder);
        g.DrawPath(barBorder, barPath);

        using var helpBrush = new SolidBrush(TextWhite);
        g.DrawString(helpText, helpFont, helpBrush,
            barX + S(30), barY + S(10));
    }

    private void DrawActionToolbar(Graphics g)
    {
        var rects = GetButtonRects();
        if (rects == null) return;

        var (toolbarRect, buttonRects) = rects.Value;

        // Toolbar shadow
        var shadowRect = toolbarRect;
        shadowRect.Offset(0, S(3));
        using var shadowPath = RoundedRect(shadowRect, S(16));
        using var shadowBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0));
        g.FillPath(shadowBrush, shadowPath);

        // Toolbar background
        using var toolbarPath = RoundedRect(toolbarRect, S(16));
        using var toolbarBgBrush = new SolidBrush(ToolbarBg);
        g.FillPath(toolbarBgBrush, toolbarPath);
        using var toolbarBorderPen = new Pen(ToolbarBorder);
        g.DrawPath(toolbarBorderPen, toolbarPath);

        using var btnFont = new Font("Segoe UI", Sf(11.5f), FontStyle.Bold);

        // Draw separator between Cancel and action buttons
        {
            var sepX = buttonRects[0].Right + (buttonRects[1].X - buttonRects[0].Right) / 2;
            using var sepPen = new Pen(Color.FromArgb(60, 255, 255, 255), Sf(1));
            g.DrawLine(sepPen, sepX, toolbarRect.Y + Sf(10), sepX, toolbarRect.Bottom - Sf(10));
        }

        // Buttons
        for (int i = 0; i < _buttons.Length; i++)
        {
            var btn = _buttons[i];
            var rect = buttonRects[i];
            var hovered = i == _hoveredButton;

            Color bg, fg, border;
            switch (btn.Style)
            {
                case ButtonStyle.Primary:
                    bg = hovered ? AccentBlueHover : AccentBlue;
                    fg = Color.White;
                    border = hovered ? AccentBlueHover : AccentBlue;
                    break;
                case ButtonStyle.Danger:
                    bg = hovered ? Color.FromArgb(250, 70, 25, 25) : Color.FromArgb(230, 45, 15, 15);
                    fg = DangerRed;
                    border = Color.FromArgb(100, 220, 70, 70);
                    break;
                case ButtonStyle.Record:
                    bg = hovered ? Color.FromArgb(250, 180, 30, 30) : Color.FromArgb(230, 160, 20, 20);
                    fg = Color.White;
                    border = Color.FromArgb(180, 200, 50, 50);
                    break;
                default:
                    bg = hovered ? BtnBgHover : BtnBg;
                    fg = TextWhite;
                    border = BtnBorder;
                    break;
            }

            using var btnPath = RoundedRect(rect, S(10));
            using var bgBrush = new SolidBrush(bg);
            g.FillPath(bgBrush, btnPath);
            using var borderPen = new Pen(border, Sf(1.2f));
            g.DrawPath(borderPen, btnPath);

            // Record button: draw red circle indicator
            if (btn.Style == ButtonStyle.Record)
            {
                var circleSize = S(10);
                var circleX = rect.X + S(14);
                var circleY = rect.Y + (rect.Height - circleSize) / 2;
                using var circleBrush = new SolidBrush(Color.FromArgb(255, 80, 80));
                g.FillEllipse(circleBrush, circleX, circleY, circleSize, circleSize);

                // Text offset to make room for circle
                using var fgBrush2 = new SolidBrush(fg);
                var ts2 = g.MeasureString(btn.Label, btnFont);
                g.DrawString(btn.Label, btnFont, fgBrush2,
                    circleX + circleSize + S(6),
                    rect.Y + (rect.Height - ts2.Height) / 2);
                continue;
            }

            using var fgBrush = new SolidBrush(fg);
            var ts = g.MeasureString(btn.Label, btnFont);
            g.DrawString(btn.Label, btnFont, fgBrush,
                rect.X + (rect.Width - ts.Width) / 2,
                rect.Y + (rect.Height - ts.Height) / 2);
        }
    }

    private (RectangleF toolbar, RectangleF[] buttons)? GetButtonRects()
    {
        if (!_hasSelection || _selecting || _selection.Width <= 10) return null;

        using var tmpG = CreateGraphics();
        using var btnFont = new Font("Segoe UI", Sf(11.5f), FontStyle.Bold);

        var btnHeight = Sf(40);
        var btnPadX = Sf(28);
        var btnGap = Sf(8);
        var toolbarPadX = Sf(14);
        var toolbarPadY = Sf(10);

        // Calculate button widths
        var buttonWidths = new float[_buttons.Length];
        for (int i = 0; i < _buttons.Length; i++)
        {
            var textWidth = tmpG.MeasureString(_buttons[i].Label, btnFont).Width;
            var minWidth = Sf(90);

            if (_buttons[i].Style == ButtonStyle.Record)
            {
                // Extra space for the red circle indicator
                buttonWidths[i] = Math.Max(textWidth + btnPadX + S(22), minWidth);
            }
            else
            {
                buttonWidths[i] = Math.Max(textWidth + btnPadX, minWidth);
            }
        }

        var totalBtnWidth = 0f;
        for (int i = 0; i < buttonWidths.Length; i++)
            totalBtnWidth += buttonWidths[i] + (i > 0 ? btnGap : 0);

        // Separator between cancel and the rest
        var separatorWidth = Sf(16);

        var totalWidth = toolbarPadX + buttonWidths[0] + separatorWidth + (totalBtnWidth - buttonWidths[0]) + toolbarPadX;
        var toolbarHeight = btnHeight + toolbarPadY * 2;

        // Position toolbar below selection, centered on selection
        var selCenterX = _selection.X + _selection.Width / 2f;
        var toolbarX = selCenterX - totalWidth / 2;
        toolbarX = Math.Max(S(10), Math.Min(Width - totalWidth - S(10), toolbarX));

        var toolbarY = _selection.Bottom + S(16);
        // If not enough space below, put it above
        if (toolbarY + toolbarHeight > Height - S(10))
            toolbarY = _selection.Y - toolbarHeight - S(16);
        // If still not enough space, put at bottom of screen
        if (toolbarY < S(10))
            toolbarY = Height - toolbarHeight - S(20);

        var toolbarRect = new RectangleF(toolbarX, toolbarY, totalWidth, toolbarHeight);

        // Position buttons
        var buttonRects = new RectangleF[_buttons.Length];
        var x = toolbarX + toolbarPadX;
        var btnY = toolbarY + toolbarPadY;

        for (int i = 0; i < _buttons.Length; i++)
        {
            if (i == 1) x += separatorWidth - btnGap; // separator gap after Cancel
            buttonRects[i] = new RectangleF(x, btnY, buttonWidths[i], btnHeight);
            x += buttonWidths[i] + btnGap;
        }

        return (toolbarRect, buttonRects);
    }

    private int HitTestButtonIndex(Point p)
    {
        var rects = GetButtonRects();
        if (rects == null) return -1;

        for (int i = 0; i < rects.Value.buttons.Length; i++)
        {
            if (rects.Value.buttons[i].Contains(p))
                return i;
        }
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // Check if clicking on action buttons
            if (_hasSelection && !_selecting)
            {
                var idx = HitTestButtonIndex(e.Location);
                if (idx >= 0)
                {
                    var btn = _buttons[idx];
                    if (btn.Action == OverlayAction.Cancel)
                    {
                        Action = OverlayAction.Cancel;
                        DialogResult = DialogResult.Cancel;
                    }
                    else
                    {
                        Action = btn.Action;
                        DialogResult = DialogResult.OK;
                    }
                    return;
                }
            }

            // Start new selection
            _selecting = true;
            _hasSelection = false;
            _startPoint = e.Location;
            _selection = new Rectangle(e.Location, Size.Empty);
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_selecting)
        {
            _selection = GetRectangle(_startPoint, e.Location);
            _hasSelection = true;
            Invalidate();
        }
        else if (_hasSelection)
        {
            var prevHovered = _hoveredButton;
            _hoveredButton = HitTestButtonIndex(e.Location);
            if (_hoveredButton != prevHovered)
            {
                Cursor = _hoveredButton >= 0 ? Cursors.Hand : Cursors.Cross;
                Invalidate();
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_selecting)
        {
            _selecting = false;
            _selection = GetRectangle(_startPoint, e.Location);
            _hasSelection = _selection.Width > 5 && _selection.Height > 5;

            if (!_hasSelection) return;
            Invalidate();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Action = OverlayAction.Cancel;
            DialogResult = DialogResult.Cancel;
        }
        else if (e.KeyCode == Keys.Enter && _hasSelection)
        {
            Action = OverlayAction.Edit;
            DialogResult = DialogResult.OK;
        }
        else if (e.KeyCode == Keys.C && e.Control && _hasSelection)
        {
            Action = OverlayAction.Copy;
            DialogResult = DialogResult.OK;
        }
        else if (e.KeyCode == Keys.S && _hasSelection)
        {
            Action = OverlayAction.QuickShare;
            DialogResult = DialogResult.OK;
        }
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Rectangle GetRectangle(Point a, Point b)
    {
        return new Rectangle(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}

public enum OverlayAction
{
    Cancel,
    Edit,
    Copy,
    QuickShare,
    Upload,
    Record
}

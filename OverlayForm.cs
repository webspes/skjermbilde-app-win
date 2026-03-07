using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Skjermbilde;

public class OverlayForm : Form
{
    private readonly Bitmap _background;
    private Point _startPoint;
    private Rectangle _selection;
    private bool _selecting;
    private bool _hasSelection;
    private int _hoveredButton = -1;

    public Rectangle SelectedArea => _selection;
    public OverlayAction Action { get; private set; } = OverlayAction.Cancel;

    // Button layout constants
    private static readonly Color AccentBlue = Color.FromArgb(37, 99, 235);
    private static readonly Color AccentBlueHover = Color.FromArgb(59, 130, 246);
    private static readonly Color DangerRed = Color.FromArgb(240, 86, 86);
    private static readonly Color DangerBg = Color.FromArgb(200, 60, 30, 30);
    private static readonly Color BtnBg = Color.FromArgb(230, 15, 15, 20);
    private static readonly Color BtnBgHover = Color.FromArgb(240, 35, 35, 50);
    private static readonly Color BtnBorder = Color.FromArgb(80, 255, 255, 255);

    private record struct ButtonDef(string Label, OverlayAction Action, ButtonStyle Style);
    private enum ButtonStyle { Default, Primary, Danger }

    private readonly ButtonDef[] _buttons = {
        new("Avbryt", OverlayAction.Cancel, ButtonStyle.Danger),
        new("Kopier", OverlayAction.Copy, ButtonStyle.Default),
        new("Hurtigdeling", OverlayAction.QuickShare, ButtonStyle.Default),
        new("Rediger", OverlayAction.Edit, ButtonStyle.Default),
        new("Last opp", OverlayAction.Upload, ButtonStyle.Primary),
    };

    public OverlayForm(Bitmap background)
    {
        _background = background;

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
        using var dimBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
        g.FillRectangle(dimBrush, ClientRectangle);

        if (_hasSelection && _selection.Width > 0 && _selection.Height > 0)
        {
            // Draw the selected area (clear the dimming)
            g.SetClip(_selection);
            g.DrawImage(_background, ClientRectangle);
            g.ResetClip();

            // Selection border
            using var pen = new Pen(AccentBlue, 2);
            g.DrawRectangle(pen, _selection);

            // Corner handles (8x8 blue squares)
            DrawCornerHandle(g, _selection.X - 3, _selection.Y - 3);
            DrawCornerHandle(g, _selection.Right - 5, _selection.Y - 3);
            DrawCornerHandle(g, _selection.X - 3, _selection.Bottom - 5);
            DrawCornerHandle(g, _selection.Right - 5, _selection.Bottom - 5);

            // Size label above selection
            var sizeText = $"{_selection.Width} x {_selection.Height} px";
            using var sizeFont = new Font("Segoe UI", 10f, FontStyle.Regular);
            var textSize = g.MeasureString(sizeText, sizeFont);
            var labelX = _selection.X;
            var labelY = _selection.Y - textSize.Height - 10;
            if (labelY < 4) labelY = _selection.Bottom + 6;

            var labelRect = new RectangleF(labelX, labelY, textSize.Width + 16, textSize.Height + 6);
            using var labelPath = RoundedRect(labelRect, 6);
            using var labelBg = new SolidBrush(Color.FromArgb(210, 13, 13, 20));
            g.FillPath(labelBg, labelPath);
            using var labelBorder = new Pen(Color.FromArgb(40, 255, 255, 255));
            g.DrawPath(labelBorder, labelPath);
            using var textBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
            g.DrawString(sizeText, sizeFont, textBrush, labelRect.X + 8, labelRect.Y + 3);
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

    private void DrawCornerHandle(Graphics g, int x, int y)
    {
        using var brush = new SolidBrush(AccentBlue);
        g.FillRectangle(brush, x, y, 8, 8);
    }

    private void DrawHelpBar(Graphics g)
    {
        var helpText = "Dra for a velge omrade  ·  ESC avbryt  ·  Klikk for fritt valg";
        using var helpFont = new Font("Segoe UI", 11f);
        var helpSize = g.MeasureString(helpText, helpFont);
        var barWidth = helpSize.Width + 40;
        var barHeight = helpSize.Height + 16;
        var barX = (Width - barWidth) / 2;
        var barY = Height - barHeight - 30;

        var barRect = new RectangleF(barX, barY, barWidth, barHeight);
        using var barPath = RoundedRect(barRect, 20);
        using var barBg = new SolidBrush(Color.FromArgb(200, 13, 13, 20));
        g.FillPath(barBg, barPath);
        using var barBorder = new Pen(Color.FromArgb(40, 255, 255, 255));
        g.DrawPath(barBorder, barPath);

        using var helpBrush = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
        g.DrawString(helpText, helpFont, helpBrush,
            barX + 20, barY + 8);
    }

    private void DrawActionToolbar(Graphics g)
    {
        var rects = GetButtonRects();
        if (rects == null) return;

        var (toolbarRect, buttonRects) = rects.Value;

        // Toolbar background
        using var toolbarPath = RoundedRect(toolbarRect, 14);
        using var toolbarBg = new SolidBrush(Color.FromArgb(235, 15, 15, 20));
        g.FillPath(toolbarBg, toolbarPath);
        using var toolbarBorder = new Pen(Color.FromArgb(40, 255, 255, 255));
        g.DrawPath(toolbarBorder, toolbarPath);

        using var btnFont = new Font("Segoe UI", 10f, FontStyle.Regular);

        // Size info on the left
        var infoText = $"{_selection.Width} x {_selection.Height}";
        using var infoFont = new Font("Consolas", 9.5f);
        using var infoBrush = new SolidBrush(Color.FromArgb(152, 152, 184));
        g.DrawString(infoText, infoFont, infoBrush, toolbarRect.X + 14, toolbarRect.Y + 12);

        // Separator after size info
        var infoSize = g.MeasureString(infoText, infoFont);
        var sepX = toolbarRect.X + 14 + infoSize.Width + 10;
        using var sepPen = new Pen(Color.FromArgb(50, 255, 255, 255));
        g.DrawLine(sepPen, sepX, toolbarRect.Y + 8, sepX, toolbarRect.Bottom - 8);

        // Buttons
        for (int i = 0; i < _buttons.Length; i++)
        {
            var btn = _buttons[i];
            var rect = buttonRects[i];
            var hovered = i == _hoveredButton;

            Color bg, fg;
            switch (btn.Style)
            {
                case ButtonStyle.Primary:
                    bg = hovered ? AccentBlueHover : AccentBlue;
                    fg = Color.White;
                    break;
                case ButtonStyle.Danger:
                    bg = hovered ? Color.FromArgb(220, 80, 30, 30) : Color.FromArgb(200, 50, 20, 20);
                    fg = DangerRed;
                    break;
                default:
                    bg = hovered ? BtnBgHover : BtnBg;
                    fg = Color.White;
                    break;
            }

            using var btnPath = RoundedRect(rect, 8);
            using var bgBrush = new SolidBrush(bg);
            g.FillPath(bgBrush, btnPath);
            using var borderPen = new Pen(BtnBorder);
            g.DrawPath(borderPen, btnPath);

            // Draw separator before Cancel button (first button)
            if (i == 1)
            {
                var sx = rect.X - 6;
                g.DrawLine(sepPen, sx, toolbarRect.Y + 8, sx, toolbarRect.Bottom - 8);
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
        using var btnFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        using var infoFont = new Font("Consolas", 9.5f);

        var infoText = $"{_selection.Width} x {_selection.Height}";
        var infoWidth = tmpG.MeasureString(infoText, infoFont).Width;

        var btnHeight = 32f;
        var btnPadding = 20f;
        var btnGap = 8f;

        // Calculate button widths
        var buttonWidths = new float[_buttons.Length];
        for (int i = 0; i < _buttons.Length; i++)
        {
            var textWidth = tmpG.MeasureString(_buttons[i].Label, btnFont).Width;
            buttonWidths[i] = Math.Max(textWidth + btnPadding, 80);
        }

        var totalBtnWidth = 0f;
        for (int i = 0; i < buttonWidths.Length; i++)
            totalBtnWidth += buttonWidths[i] + (i > 0 ? btnGap : 0);

        // Info + separator + gap + cancel + separator + gap + rest of buttons
        var totalWidth = 14 + infoWidth + 10 + 8 + totalBtnWidth + 14;
        var toolbarHeight = btnHeight + 16;

        var toolbarX = (Width - totalWidth) / 2;
        var toolbarY = Height - toolbarHeight - 20;

        // Keep toolbar near selection if possible
        var selCenterX = _selection.X + _selection.Width / 2f;
        toolbarX = selCenterX - totalWidth / 2;
        toolbarX = Math.Max(10, Math.Min(Width - totalWidth - 10, toolbarX));

        var toolbarRect = new RectangleF(toolbarX, toolbarY, totalWidth, toolbarHeight);

        // Position buttons
        var buttonRects = new RectangleF[_buttons.Length];
        var x = toolbarX + 14 + infoWidth + 10 + 8; // after info + separator
        var btnY = toolbarY + 8;

        for (int i = 0; i < _buttons.Length; i++)
        {
            if (i == 1) x += 4; // extra gap after Cancel (separator space)
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
    Upload
}

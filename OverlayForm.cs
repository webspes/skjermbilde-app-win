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

    public Rectangle SelectedArea => _selection;
    public OverlayAction Action { get; private set; } = OverlayAction.Cancel;

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

        // Dim the entire screen
        using var dimBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
        g.FillRectangle(dimBrush, ClientRectangle);

        if (_hasSelection && _selection.Width > 0 && _selection.Height > 0)
        {
            // Draw the selected area (clear the dimming)
            g.SetClip(_selection);
            g.DrawImage(_background, ClientRectangle);
            g.ResetClip();

            // Selection border
            using var pen = new Pen(Color.FromArgb(37, 99, 235), 2);
            pen.DashStyle = DashStyle.Solid;
            g.DrawRectangle(pen, _selection);

            // Size label
            var sizeText = $"{_selection.Width} x {_selection.Height}";
            using var font = new Font("Segoe UI", 10f);
            var textSize = g.MeasureString(sizeText, font);
            var labelRect = new RectangleF(
                _selection.X, _selection.Y - textSize.Height - 6,
                textSize.Width + 12, textSize.Height + 4);
            if (labelRect.Y < 0) labelRect.Y = _selection.Bottom + 4;

            using var labelBg = new SolidBrush(Color.FromArgb(200, 13, 13, 20));
            g.FillRectangle(labelBg, labelRect);
            using var textBrush = new SolidBrush(Color.White);
            g.DrawString(sizeText, font, textBrush, labelRect.X + 6, labelRect.Y + 2);
        }

        // Help text at bottom
        if (!_selecting && !_hasSelection)
        {
            var helpText = "Dra for å velge område  |  ESC = Avbryt";
            using var helpFont = new Font("Segoe UI", 11f);
            var helpSize = g.MeasureString(helpText, helpFont);
            var helpX = (Width - helpSize.Width) / 2;
            var helpY = Height - helpSize.Height - 40;
            using var helpBg = new SolidBrush(Color.FromArgb(180, 13, 13, 20));
            g.FillRectangle(helpBg, helpX - 16, helpY - 8, helpSize.Width + 32, helpSize.Height + 16);
            using var helpBrush = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
            g.DrawString(helpText, helpFont, helpBrush, helpX, helpY);
        }

        // Action buttons when selection is done
        if (_hasSelection && !_selecting && _selection.Width > 10 && _selection.Height > 10)
        {
            DrawActionButtons(g);
        }
    }

    private void DrawActionButtons(Graphics g)
    {
        var buttons = new[] {
            ("✂️ Rediger", OverlayAction.Edit),
            ("📋 Kopier", OverlayAction.Copy),
            ("🔗 Del", OverlayAction.QuickShare)
        };

        var btnWidth = 90;
        var btnHeight = 32;
        var gap = 8;
        var totalWidth = buttons.Length * btnWidth + (buttons.Length - 1) * gap;
        var startX = _selection.X + (_selection.Width - totalWidth) / 2;
        var y = _selection.Bottom + 12;
        if (y + btnHeight > Height - 10) y = _selection.Y - btnHeight - 12;

        using var font = new Font("Segoe UI", 9f);

        for (int i = 0; i < buttons.Length; i++)
        {
            var x = startX + i * (btnWidth + gap);
            var rect = new Rectangle((int)x, y, btnWidth, btnHeight);

            using var bg = new SolidBrush(i == 0 ? Color.FromArgb(220, 37, 99, 235) : Color.FromArgb(200, 30, 30, 45));
            g.FillRectangle(bg, rect);

            using var border = new Pen(Color.FromArgb(100, 255, 255, 255));
            g.DrawRectangle(border, rect);

            using var textBrush = new SolidBrush(Color.White);
            var textSize = g.MeasureString(buttons[i].Item1, font);
            g.DrawString(buttons[i].Item1, font, textBrush,
                rect.X + (rect.Width - textSize.Width) / 2,
                rect.Y + (rect.Height - textSize.Height) / 2);
        }
    }

    private OverlayAction? HitTestButtons(Point p)
    {
        if (!_hasSelection || _selecting || _selection.Width <= 10) return null;

        var buttons = new[] { OverlayAction.Edit, OverlayAction.Copy, OverlayAction.QuickShare };
        var btnWidth = 90;
        var btnHeight = 32;
        var gap = 8;
        var totalWidth = buttons.Length * btnWidth + (buttons.Length - 1) * gap;
        var startX = _selection.X + (_selection.Width - totalWidth) / 2;
        var y = _selection.Bottom + 12;
        if (y + btnHeight > Height - 10) y = _selection.Y - btnHeight - 12;

        for (int i = 0; i < buttons.Length; i++)
        {
            var x = startX + i * (btnWidth + gap);
            var rect = new Rectangle((int)x, y, btnWidth, btnHeight);
            if (rect.Contains(p))
                return buttons[i];
        }
        return null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // Check if clicking on action buttons
            if (_hasSelection)
            {
                var action = HitTestButtons(e.Location);
                if (action.HasValue)
                {
                    Action = action.Value;
                    DialogResult = DialogResult.OK;
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
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_selecting)
        {
            _selecting = false;
            _selection = GetRectangle(_startPoint, e.Location);
            _hasSelection = _selection.Width > 5 && _selection.Height > 5;

            if (!_hasSelection)
            {
                // Too small - treat as click, cancel
                return;
            }
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
    QuickShare
}

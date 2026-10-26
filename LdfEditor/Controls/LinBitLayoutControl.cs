using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LdfEditor.Models;

namespace LdfEditor.Controls;

/// <summary>
/// Vector LDF Explorer bit layout:
/// 4 rows × (bytes×8) columns — Byte No / Rel bit / Signal / Abs bit.
/// Click selects; double-click raises SignalActivated.
/// </summary>
public sealed class LinBitLayoutControl : FrameworkElement
{
    public static readonly DependencyProperty FrameProperty =
        DependencyProperty.Register(nameof(Frame), typeof(LdfFrame), typeof(LinBitLayoutControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnFrameChanged));

    public static readonly DependencyProperty DatabaseProperty =
        DependencyProperty.Register(nameof(Database), typeof(LdfDatabase), typeof(LinBitLayoutControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnFrameChanged));

    public static readonly DependencyProperty SelectedSignalIndexProperty =
        DependencyProperty.Register(nameof(SelectedSignalIndex), typeof(int), typeof(LinBitLayoutControl),
            new FrameworkPropertyMetadata(-1,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public LdfFrame? Frame
    {
        get => (LdfFrame?)GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    public LdfDatabase? Database
    {
        get => (LdfDatabase?)GetValue(DatabaseProperty);
        set => SetValue(DatabaseProperty, value);
    }

    public int SelectedSignalIndex
    {
        get => (int)GetValue(SelectedSignalIndexProperty);
        set => SetValue(SelectedSignalIndexProperty, value);
    }

    public event Action<int>? SignalActivated;

    public LinBitLayoutControl()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    private static readonly Color[] SigColors =
    {
        Color.FromRgb(210, 220, 150), Color.FromRgb(180, 200, 130), Color.FromRgb(160, 190, 110),
        Color.FromRgb(200, 210, 140), Color.FromRgb(170, 195, 120), Color.FromRgb(190, 205, 135),
        Color.FromRgb(155, 185, 100), Color.FromRgb(220, 225, 160)
    };

    private int _bytes;
    private int[] _owner = Array.Empty<int>();
    private string[] _labels = Array.Empty<string>();
    private int _margin = 6, _gapByte = 2, _cellW = 16;
    private int _rowByteH = 42, _rowRelH = 39, _rowSigH = 72, _rowAbsH = 39;
    private int _y0, _y1, _y2, _y3, _x0;

    private static void OnFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (LinBitLayoutControl)d;
        c.RefreshMapping();
    }

    /// <summary>
    /// Rebuild bit→signal ownership from current Frame/Database (SizeBits, start bits, map list).
    /// Call after editing a signal so hatch/colors span the new length.
    /// </summary>
    public void RefreshMapping()
    {
        Rebuild();
        InvalidateVisual();
    }

    private void Rebuild()
    {
        _owner = Array.Empty<int>();
        _labels = Array.Empty<string>();
        _bytes = 0;
        var frame = Frame;
        if (frame == null) return;
        _bytes = frame.Length > 0 ? frame.Length : 8;
        _owner = Enumerable.Repeat(-1, _bytes * 8).ToArray();
        _labels = new string[frame.Signals.Count];
        for (int i = 0; i < frame.Signals.Count; i++)
        {
            var fs = frame.Signals[i];
            _labels[i] = fs.Name;
            int len = 1;
            var sg = Database?.FindSignal(fs.Name);
            if (sg != null && sg.SizeBits > 0) len = sg.SizeBits;
            for (int b = 0; b < len; b++)
            {
                int bit = fs.StartBit + b;
                if (bit >= 0 && bit < _owner.Length)
                    _owner[bit] = i;
            }
        }
    }

    private void RecalcMetrics(Size size)
    {
        _margin = 4;
        _gapByte = _bytes > 1 ? 2 : 0;
        _rowByteH = 42;
        _rowRelH = 39;
        _rowAbsH = 39;
        _rowSigH = (int)size.Height - 2 * _margin - _rowByteH - _rowRelH - _rowAbsH;
        if (_rowSigH < 72) _rowSigH = 72;

        int availW = (int)size.Width - 2 * _margin;
        int gaps = _bytes > 1 ? (_bytes - 1) * _gapByte : 0;
        int totalBits = _bytes > 0 ? _bytes * 8 : 8;
        int cw = totalBits > 0 ? (availW - gaps) / totalBits : 16;
        if (cw < 6) cw = 6;
        if (cw > 28) cw = 28;
        _cellW = cw;

        _x0 = _margin;
        _y0 = _margin;
        _y1 = _y0 + _rowByteH;
        _y2 = _y1 + _rowRelH;
        _y3 = _y2 + _rowSigH;
    }

    private int BitLeft(int absBit)
    {
        int b = absBit / 8;
        int bit = absBit % 8;
        return _x0 + b * (8 * _cellW + _gapByte) + bit * _cellW;
    }

    private static Brush BrushOf(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int bytes = Frame?.Length > 0 ? Frame!.Length : 8;
        double h = 4 + 42 + 39 + 90 + 39 + 4;
        double w = Math.Max(availableSize.Width, 4 + bytes * 8 * 12 + (bytes - 1) * 2 + 4);
        if (double.IsInfinity(w) || double.IsNaN(w)) w = 480;
        return new Size(w, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        /* Always rebuild so SizeBits / StartBit edits show up (Frame DP often unchanged). */
        Rebuild();
        var w = ActualWidth > 0 ? ActualWidth : 480;
        var h = ActualHeight > 0 ? ActualHeight : 220;
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));

        if (_bytes <= 0)
        {
            var ft = new FormattedText("No signal mapping", CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12,
                BrushOf(Color.FromRgb(120, 120, 120)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(8, 8));
            return;
        }

        RecalcMetrics(new Size(w, h));
        int totalBits = _bytes * 8;
        var typeface = new Typeface("Segoe UI");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var penLight = new Pen(BrushOf(Color.FromRgb(200, 200, 200)), 1); penLight.Freeze();
        var penOuter = new Pen(BrushOf(Color.FromRgb(170, 170, 170)), 1); penOuter.Freeze();
        var penSig = new Pen(BrushOf(Color.FromRgb(120, 120, 120)), 1); penSig.Freeze();

        void DrawText(string text, double x, double y, Brush? brush = null)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, 11, brush ?? Brushes.Black, dpi);
            dc.DrawText(ft, new Point(x, y));
        }

        /* Row 0: Byte No */
        for (int b = 0; b < _bytes; b++)
        {
            int left = BitLeft(b * 8);
            var cell = new Rect(left, _y0, 8 * _cellW, _rowByteH);
            dc.DrawRectangle(Brushes.White, penOuter, cell);
            for (int i = 1; i < 8; i++)
                dc.DrawLine(penLight, new Point(left + i * _cellW, cell.Top + 1), new Point(left + i * _cellW, cell.Bottom - 1));
            var ft = new FormattedText(b.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, 12, Brushes.Black, dpi);
            dc.DrawText(ft, new Point(cell.Left + (cell.Width - ft.Width) / 2, cell.Top + (cell.Height - ft.Height) / 2));
        }

        /* Row 1: Relative bit 0..7 (only ends labeled) */
        for (int b = 0; b < _bytes; b++)
        {
            int left = BitLeft(b * 8);
            var band = new Rect(left, _y1, 8 * _cellW, _rowRelH);
            dc.DrawRectangle(Brushes.White, penOuter, band);
            for (int i = 1; i < 8; i++)
                dc.DrawLine(penLight, new Point(left + i * _cellW, band.Top + 1), new Point(left + i * _cellW, band.Bottom - 1));
            foreach (var bit in new[] { 0, 7 })
            {
                var ft = new FormattedText(bit.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, 11, Brushes.Black, dpi);
                double cx = left + bit * _cellW + (_cellW - ft.Width) / 2;
                dc.DrawText(ft, new Point(cx, band.Top + (band.Height - ft.Height) / 2));
            }
        }

        /* Row 2: Signal colors + hatch on selection */
        int sel = SelectedSignalIndex;
        for (int absBit = 0; absBit < totalBits; absBit++)
        {
            int owner = absBit < _owner.Length ? _owner[absBit] : -1;
            int left = BitLeft(absBit);
            var cell = new Rect(left, _y2, _cellW, _rowSigH);
            var fill = owner < 0 ? Colors.White : SigColors[owner % SigColors.Length];
            dc.DrawRectangle(BrushOf(fill), null, cell);
            if (owner >= 0 && owner == sel)
                DrawCellHatch(dc, cell);
        }
        {
            int left0 = BitLeft(0);
            int rightN = BitLeft((_bytes - 1) * 8) + 8 * _cellW;
            dc.DrawRectangle(null, penSig, new Rect(left0, _y2, rightN - left0, _rowSigH));
            for (int absBit = 1; absBit < totalBits; absBit++)
            {
                int x = BitLeft(absBit);
                dc.DrawLine(penSig, new Point(x, _y2), new Point(x, _y3));
            }
        }

        /* Signal arrows + names */
        if (Frame != null)
        {
            for (int si = 0; si < Frame.Signals.Count; si++)
            {
                var fs = Frame.Signals[si];
                int len = 1;
                var sg = Database?.FindSignal(fs.Name);
                if (sg != null && sg.SizeBits > 0) len = sg.SizeBits;
                int start = fs.StartBit;
                int end = Math.Min(fs.StartBit + len - 1, totalBits - 1);
                if (start < 0 || start >= totalBits) continue;

                int xL = BitLeft(start);
                int xR = BitLeft(end) + _cellW;
                DrawText("L", xL + 1, _y3 - 16);
                DrawText("M", BitLeft(end) + 1, _y2 + 2);

                int midY = _y2 + _rowSigH / 2;
                int ax0 = xL + 2;
                int ax1 = xR - 4;
                if (ax1 > ax0 + 8)
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(ax0, midY - 3, 5, 6));
                    dc.DrawLine(new Pen(Brushes.Black, 1), new Point(ax0 + 5, midY), new Point(ax1 - 6, midY));
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        ctx.BeginFigure(new Point(ax1 - 6, midY - 4), true, true);
                        ctx.LineTo(new Point(ax1, midY), true, false);
                        ctx.LineTo(new Point(ax1 - 6, midY + 4), true, false);
                    }
                    geo.Freeze();
                    dc.DrawGeometry(Brushes.Black, null, geo);
                }

                if (si < _labels.Length && _cellW >= 8 && _rowSigH > 50)
                {
                    var name = _labels[si];
                    int spanW = xR - xL;
                    var nft = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        typeface, 11, Brushes.Black, dpi);
                    if (nft.Width + 4 < spanW)
                        dc.DrawText(nft, new Point(xL + (spanW - nft.Width) / 2, midY - nft.Height - 8));
                }
            }
        }

        /* Row 3: Absolute bit */
        for (int b = 0; b < _bytes; b++)
        {
            int left = BitLeft(b * 8);
            var band = new Rect(left, _y3, 8 * _cellW, _rowAbsH);
            dc.DrawRectangle(Brushes.White, penOuter, band);
            for (int i = 1; i < 8; i++)
                dc.DrawLine(penLight, new Point(left + i * _cellW, band.Top + 1), new Point(left + i * _cellW, band.Bottom - 1));
            foreach (var bit in new[] { 0, 7 })
            {
                int absBit = b * 8 + bit;
                var ft = new FormattedText(absBit.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, 11, Brushes.Black, dpi);
                double cellLeft = left + bit * _cellW;
                double tx = cellLeft + (_cellW - ft.Width) / 2;
                if (ft.Width > _cellW)
                    tx = bit == 0 ? cellLeft : cellLeft + _cellW - ft.Width;
                dc.DrawText(ft, new Point(tx, band.Top + (band.Height - ft.Height) / 2));
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        int hit = HitTest(e.GetPosition(this));
        SelectedSignalIndex = hit;
        if (e.ClickCount >= 2 && hit >= 0)
            SignalActivated?.Invoke(hit);
        e.Handled = true;
    }

    private int HitTest(Point pt)
    {
        Rebuild();
        if (_bytes <= 0) return -1;
        RecalcMetrics(new Size(ActualWidth, ActualHeight));
        if (pt.Y < _y2 || pt.Y >= _y3) return -1;
        int totalBits = _bytes * 8;
        for (int absBit = 0; absBit < totalBits; absBit++)
        {
            int left = BitLeft(absBit);
            if (pt.X >= left && pt.X < left + _cellW)
                return absBit < _owner.Length ? _owner[absBit] : -1;
        }
        return -1;
    }

    /** GDI HS_BDIAGONAL-style hatch, clipped to the bit cell (Vector select look). */
    private static void DrawCellHatch(DrawingContext dc, Rect cell)
    {
        dc.PushClip(new RectangleGeometry(cell));
        var hatchPen = new Pen(BrushOf(Color.FromRgb(90, 110, 50)), 1.0);
        hatchPen.Freeze();
        const double step = 5;
        /* Cover the clipped cell with 45° diagonals; clip prevents spill into neighbors. */
        for (double d = -cell.Height; d < cell.Width + cell.Height; d += step)
        {
            dc.DrawLine(hatchPen,
                new Point(cell.Left + d, cell.Top),
                new Point(cell.Left + d + cell.Height, cell.Bottom));
        }
        dc.Pop();
    }
}

using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UDM18.Client.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace UDM18.Client.Controls;

public sealed class BoardControl : FrameworkElement
{
    private const double Padding = 34;
    private INotifyCollectionChanged? _observablePieces;
    private string? _animatedPieceId;
    private Position _animationFrom;
    private Position _animationTo;
    private long _animationStarted;
    private const double AnimationMilliseconds = 190;
    private static readonly Dictionary<string, ImageSource> ClassicPieceImages = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ImageSource? OceanBoardImage = LoadOceanBoardImage();
    private readonly DispatcherTimer _riverTimer;
    private readonly long _riverStarted = Stopwatch.GetTimestamp();

    public BoardControl()
    {
        _riverTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _riverTimer.Tick += (_, _) => InvalidateVisual();
        Loaded += (_, _) => _riverTimer.Start();
        Unloaded += (_, _) => _riverTimer.Stop();
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public static readonly DependencyProperty PiecesProperty = DependencyProperty.Register(
        nameof(Pieces), typeof(IEnumerable), typeof(BoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPiecesChanged));
    public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register(
        nameof(Selected), typeof(Position?), typeof(BoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LastFromProperty = DependencyProperty.Register(
        nameof(LastFrom), typeof(Position?), typeof(BoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LastToProperty = DependencyProperty.Register(
        nameof(LastTo), typeof(Position?), typeof(BoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(BoardOrientation), typeof(BoardControl),
        new FrameworkPropertyMetadata(BoardOrientation.RedAtBottom, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CoordinateClickedCommandProperty = DependencyProperty.Register(
        nameof(CoordinateClickedCommand), typeof(ICommand), typeof(BoardControl));
    public static readonly DependencyProperty UseClassicPiecesProperty = DependencyProperty.Register(
        nameof(UseClassicPieces), typeof(bool), typeof(BoardControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Pieces { get => (IEnumerable?)GetValue(PiecesProperty); set => SetValue(PiecesProperty, value); }
    public Position? Selected { get => (Position?)GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    public Position? LastFrom { get => (Position?)GetValue(LastFromProperty); set => SetValue(LastFromProperty, value); }
    public Position? LastTo { get => (Position?)GetValue(LastToProperty); set => SetValue(LastToProperty, value); }
    public BoardOrientation Orientation { get => (BoardOrientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public ICommand? CoordinateClickedCommand { get => (ICommand?)GetValue(CoordinateClickedCommandProperty); set => SetValue(CoordinateClickedCommandProperty, value); }
    public bool UseClassicPieces { get => (bool)GetValue(UseClassicPiecesProperty); set => SetValue(UseClassicPiecesProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 720 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 780 : availableSize.Height;
        var scale = Math.Min(width / 9d, height / 10d);
        return new Size(Math.Min(width, scale * 9), Math.Min(height, scale * 10));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var geometry = GetGeometry();
        var boardRect = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
        if (OceanBoardImage is not null) dc.DrawImage(OceanBoardImage, boardRect);
        else
        {
            var boardBrush = new LinearGradientBrush(
                Color.FromRgb(248, 224, 174), Color.FromRgb(211, 166, 100), new Point(0, 0), new Point(1, 1));
            dc.DrawRoundedRectangle(boardBrush, new Pen(new SolidColorBrush(Color.FromRgb(102, 64, 35)), 2),
                new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)), 17, 17);
        }
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(135, 255, 218, 130)), 1.2),
            new Rect(7, 7, Math.Max(0, ActualWidth - 14), Math.Max(0, ActualHeight - 14)), 13, 13);
        DrawAnimatedOceanSurface(dc, geometry);
        DrawAnimatedRiver(dc, geometry);
        DrawBoardGrid(dc, geometry, new Pen(new SolidColorBrush(Color.FromArgb(175, 3, 19, 30)), 3.8));
        DrawBoardGrid(dc, geometry, new Pen(new SolidColorBrush(Color.FromArgb(235, 248, 207, 112)), 1.55));
        var riverTextLeft = new Point(geometry.Left + geometry.Cell * 1.25, geometry.Top + geometry.Cell * 4.42);
        var riverTextRight = new Point(geometry.Left + geometry.Cell * 5.45, geometry.Top + geometry.Cell * 4.42);
        DrawText(dc, "楚  河", riverTextLeft + new Vector(1.5, 2), 16, new SolidColorBrush(Color.FromArgb(180, 0, 18, 31)));
        DrawText(dc, "漢  界", riverTextRight + new Vector(1.5, 2), 16, new SolidColorBrush(Color.FromArgb(180, 0, 18, 31)));
        DrawText(dc, "楚  河", riverTextLeft, 16, new SolidColorBrush(Color.FromRgb(255, 224, 145)));
        DrawText(dc, "漢  界", riverTextRight, 16, new SolidColorBrush(Color.FromRgb(255, 224, 145)));

        DrawHighlight(dc, geometry, LastFrom, Color.FromArgb(110, 255, 193, 7));
        DrawHighlight(dc, geometry, LastTo, Color.FromArgb(145, 16, 185, 129));
        DrawHighlight(dc, geometry, Selected, Color.FromArgb(180, 59, 130, 246));

        if (Pieces is not null)
            foreach (var piece in Pieces.OfType<PieceState>().Where(p => !p.Captured)) DrawPiece(dc, geometry, piece);
    }

    private void DrawAnimatedOceanSurface(DrawingContext dc, GeometryInfo geometry)
    {
        if (geometry.Cell <= 0) return;

        // Animate the complete playable ocean surface while leaving the carved frame untouched.
        var ocean = new Rect(
            geometry.Left - geometry.Cell * .48,
            geometry.Top - geometry.Cell * .48,
            geometry.Cell * 8.96,
            geometry.Cell * 9.96);
        var seconds = Stopwatch.GetElapsedTime(_riverStarted).TotalSeconds;
        dc.PushClip(new RectangleGeometry(ocean, geometry.Cell * .14, geometry.Cell * .14));

        var tide = (Math.Sin(seconds * .72) + 1) * .5;
        var movingLight = new LinearGradientBrush
        {
            StartPoint = new Point((seconds * .055) % 1.4 - .2, 0),
            EndPoint = new Point(((seconds * .055) % 1.4) + .35, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 72, 225, 255), 0),
                new GradientStop(Color.FromArgb((byte)(18 + tide * 18), 117, 235, 255), .46),
                new GradientStop(Color.FromArgb(0, 10, 89, 146), 1)
            }
        };
        dc.DrawRectangle(movingLight, null, ocean);

        // Long rolling waves travel across both halves of the board.
        for (var layer = 0; layer < 9; layer++)
        {
            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                var baseY = ocean.Top + ocean.Height * (.08 + layer * .105);
                var amplitude = geometry.Cell * (.055 + layer % 3 * .018);
                var phase = seconds * (1.7 + layer * .13) + layer * 1.31;
                var startX = ocean.Left - geometry.Cell;
                context.BeginFigure(new Point(startX, baseY), false, false);
                for (var x = startX; x <= ocean.Right + geometry.Cell; x += Math.Max(3, geometry.Cell / 13))
                {
                    var unit = (x - ocean.Left) / geometry.Cell;
                    var y = baseY
                        + Math.Sin(unit * (1.2 + layer * .08) + phase) * amplitude
                        + Math.Sin(unit * .48 - phase * .62) * amplitude * .38;
                    context.LineTo(new Point(x, y), true, false);
                }
            }
            path.Freeze();
            var color = (layer % 3) switch
            {
                0 => Color.FromArgb(105, 221, 252, 255),
                1 => Color.FromArgb(82, 79, 218, 255),
                _ => Color.FromArgb(68, 171, 246, 255)
            };
            var pen = new Pen(new SolidColorBrush(color), Math.Max(1.2, geometry.Cell * .018))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawGeometry(null, pen, path);
        }

        // Small highlights make movement visible even on darker parts of the texture.
        for (var glint = 0; glint < 18; glint++)
        {
            var x = ocean.Left + ((seconds * (22 + glint * .9) + glint * geometry.Cell * .73) % ocean.Width);
            var y = ocean.Top + ((glint * 137) % 91) / 100d * ocean.Height
                + Math.Sin(seconds * 1.8 + glint * .8) * geometry.Cell * .08;
            var radius = geometry.Cell * (.018 + glint % 4 * .006);
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromArgb((byte)(75 + glint % 3 * 22), 225, 252, 255)),
                null,
                new Point(x, y),
                radius * (3.2 + glint % 2),
                radius);
        }

        dc.Pop();
    }

    private void DrawAnimatedRiver(DrawingContext dc, GeometryInfo geometry)
    {
        if (geometry.Cell <= 0) return;
        var top = geometry.Point(0, 4).Y + 1.5;
        var bottom = geometry.Point(0, 5).Y - 1.5;
        var river = new Rect(geometry.Left, top, geometry.Cell * 8, Math.Max(0, bottom - top));
        if (river.Height <= 0) return;

        var seconds = Stopwatch.GetElapsedTime(_riverStarted).TotalSeconds;
        var shimmer = (Math.Sin(seconds * 1.25) + 1) * .5;
        var water = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(126, 34, 150, 177), 0),
                new GradientStop(Color.FromArgb((byte)(105 + shimmer * 28), 26, 116, 158), .48),
                new GradientStop(Color.FromArgb(132, 20, 87, 132), 1)
            }
        };
        dc.PushClip(new RectangleGeometry(river));
        dc.DrawRectangle(water, null, river);

        var waveColors = new[]
        {
            Color.FromArgb(225, 230, 255, 255),
            Color.FromArgb(190, 83, 223, 255),
            Color.FromArgb(170, 184, 250, 255),
            Color.FromArgb(145, 39, 184, 232)
        };
        for (var layer = 0; layer < waveColors.Length; layer++)
        {
            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                var baseY = river.Top + river.Height * (.2 + layer * .19);
                var speed = .95 + layer * .33;
                var amplitude = geometry.Cell * (.052 + layer * .008);
                var startX = river.Left - geometry.Cell;
                context.BeginFigure(new Point(startX, baseY), false, false);
                for (var x = startX; x <= river.Right + geometry.Cell; x += Math.Max(3, geometry.Cell / 12))
                {
                    var normalized = (x - river.Left) / geometry.Cell;
                    var y = baseY + Math.Sin(normalized * (1.55 + layer * .17) + seconds * speed * 3.2 + layer * 1.7) * amplitude;
                    context.LineTo(new Point(x, y), true, false);
                }
            }
            path.Freeze();
            var wavePen = new Pen(new SolidColorBrush(waveColors[layer]), Math.Max(1.8, geometry.Cell * (.028 + layer * .003)))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawGeometry(null, wavePen, path);
        }

        for (var sparkle = 0; sparkle < 12; sparkle++)
        {
            var travel = (seconds * (34 + sparkle * 3.1) + sparkle * geometry.Cell * .83) % (river.Width + geometry.Cell * 1.4);
            var x = river.Left - geometry.Cell * .7 + travel;
            var y = river.Top + river.Height * (.12 + (sparkle % 5) * .18) + Math.Sin(seconds * 2.6 + sparkle) * 3;
            var radius = Math.Max(1.8, geometry.Cell * (.025 + (sparkle % 3) * .009));
            var foamColor = sparkle % 3 == 0
                ? Color.FromArgb(215, 240, 255, 255)
                : Color.FromArgb(160, 169, 245, 255);
            dc.DrawEllipse(new SolidColorBrush(foamColor), null, new Point(x, y), radius * (3.8 + sparkle % 2), radius);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), null,
                new Point(x - radius * 2.2, y + radius * .55), radius * 1.2, radius * .45);
        }
        dc.Pop();
    }

    private static void DrawBoardGrid(DrawingContext dc, GeometryInfo geometry, Pen pen)
    {
        pen.StartLineCap = PenLineCap.Round;
        pen.EndLineCap = PenLineCap.Round;
        for (var y = 0; y <= 9; y++) dc.DrawLine(pen, geometry.Point(0, y), geometry.Point(8, y));
        for (var x = 0; x <= 8; x++)
        {
            dc.DrawLine(pen, geometry.Point(x, 0), geometry.Point(x, 4));
            dc.DrawLine(pen, geometry.Point(x, 5), geometry.Point(x, 9));
        }
        dc.DrawLine(pen, geometry.Point(3, 0), geometry.Point(5, 2));
        dc.DrawLine(pen, geometry.Point(5, 0), geometry.Point(3, 2));
        dc.DrawLine(pen, geometry.Point(3, 7), geometry.Point(5, 9));
        dc.DrawLine(pen, geometry.Point(5, 7), geometry.Point(3, 9));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var geometry = GetGeometry();
        if (geometry.Cell <= 0) return;
        var point = e.GetPosition(this);
        var x = (int)Math.Round((point.X - geometry.Left) / geometry.Cell);
        var y = (int)Math.Round((point.Y - geometry.Top) / geometry.Cell);
        if (x is < 0 or > 8 || y is < 0 or > 9) return;
        var coordinate = BoardGeometry.ViewToCanonical(x, y, Orientation);
        if (CoordinateClickedCommand?.CanExecute(coordinate) == true) CoordinateClickedCommand.Execute(coordinate);
    }

    private void DrawPiece(DrawingContext dc, GeometryInfo geometry, PieceState piece)
    {
        var view = BoardGeometry.CanonicalToView(piece.Position, Orientation);
        Point center;
        if (piece.PieceId == _animatedPieceId)
        {
            var elapsed = Stopwatch.GetElapsedTime(_animationStarted).TotalMilliseconds;
            var progress = Math.Clamp(elapsed / AnimationMilliseconds, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            var fromView = BoardGeometry.CanonicalToView(_animationFrom, Orientation);
            var toView = BoardGeometry.CanonicalToView(_animationTo, Orientation);
            center = new Point(
                geometry.Left + (fromView.X + (toView.X - fromView.X) * eased) * geometry.Cell,
                geometry.Top + (fromView.Y + (toView.Y - fromView.Y) * eased) * geometry.Cell);
        }
        else center = geometry.Point(view.X, view.Y);
        var radius = geometry.Cell * 0.38;
        if (UseClassicPieces)
        {
            var marked = Selected == piece.Position;
            if (GetClassicPieceImage(piece, marked) is { } image)
            {
                var size = geometry.Cell * .92;
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(65, 45, 24, 12)), null,
                    new Point(center.X + 2, center.Y + 3), size * .48, size * .48);
                dc.DrawImage(image, new Rect(center.X - size / 2, center.Y - size / 2, size, size));
                return;
            }
        }
        var isRed = piece.Side == SideColor.Red;
        var seconds = Stopwatch.GetElapsedTime(_riverStarted).TotalSeconds;
        var shimmer = (Math.Sin(seconds * 1.8 + view.X * .7 + view.Y * .42) + 1) * .5;

        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(105, 0, 22, 37)), null,
            new Point(center.X + geometry.Cell * .05, center.Y + geometry.Cell * .075), radius * 1.08, radius * 1.08);
        var pieceBrush = new RadialGradientBrush
        {
            Center = new Point(.38, .32),
            GradientOrigin = new Point(.27, .2),
            RadiusX = .78,
            RadiusY = .78,
        };
        if (isRed)
        {
            pieceBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 213, 195), 0));
            pieceBrush.GradientStops.Add(new GradientStop(Color.FromRgb(239, 78, 91), .48));
            pieceBrush.GradientStops.Add(new GradientStop(Color.FromRgb(129, 16, 55), 1));
        }
        else
        {
            pieceBrush.GradientStops.Add(new GradientStop(Color.FromRgb(190, 255, 249), 0));
            pieceBrush.GradientStops.Add(new GradientStop(Color.FromRgb(18, 151, 181), .5));
            pieceBrush.GradientStops.Add(new GradientStop(Color.FromRgb(4, 38, 83), 1));
        }

        var bronze = new SolidColorBrush(Color.FromRgb(238, 190, 92));
        dc.DrawEllipse(pieceBrush, new Pen(new SolidColorBrush(Color.FromRgb(87, 51, 24)), 2.2), center, radius, radius);
        dc.DrawEllipse(null, new Pen(bronze, Math.Max(1.4, geometry.Cell * .024)), center, radius * .88, radius * .88);
        dc.DrawEllipse(null,
            new Pen(new SolidColorBrush(isRed ? Color.FromArgb(210, 255, 183, 173) : Color.FromArgb(220, 111, 245, 255)),
                Math.Max(1.1, geometry.Cell * .017)),
            center, radius * .76, radius * .76);

        // Pearl glints make the pieces feel wet and remain animated with the ocean surface.
        var glintAlpha = (byte)(125 + shimmer * 100);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(glintAlpha, 255, 255, 255)), null,
            new Point(center.X - radius * .34, center.Y - radius * .37), radius * .19, radius * .105);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(105, 209, 252, 255)), null,
            new Point(center.X + radius * .38, center.Y + radius * .31), radius * .09, radius * .09);

        var textBrush = new SolidColorBrush(isRed ? Color.FromRgb(255, 248, 222) : Color.FromRgb(236, 255, 255));
        DrawText(dc, Label(piece), new Point(center.X + 1.1, center.Y + 1.4), Math.Max(14, radius * .9),
            new SolidColorBrush(Color.FromArgb(150, 0, 25, 44)), centered: true);
        DrawText(dc, Label(piece), new Point(center.X, center.Y), Math.Max(14, radius * .9), textBrush, centered: true);
    }

    private void DrawHighlight(DrawingContext dc, GeometryInfo geometry, Position? coordinate, Color color)
    {
        if (coordinate is null) return;
        var view = BoardGeometry.CanonicalToView(coordinate.Value, Orientation);
        var center = geometry.Point(view.X, view.Y);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B)),
            new Pen(new SolidColorBrush(color), 3), center, geometry.Cell * .45, geometry.Cell * .45);
    }

    private static string Label(PieceState piece) => piece.Type switch
    {
        PieceType.General => piece.Side == SideColor.Red ? "帥" : "將",
        PieceType.Advisor => piece.Side == SideColor.Red ? "仕" : "士",
        PieceType.Elephant => piece.Side == SideColor.Red ? "相" : "象",
        PieceType.Horse => "馬",
        PieceType.Chariot => "車",
        PieceType.Cannon => "炮",
        _ => piece.Side == SideColor.Red ? "兵" : "卒"
    };

    private static void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush, bool centered = false)
    {
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("vi-VN"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size, brush, 1.0);
        if (centered) point = new Point(point.X - formatted.Width / 2, point.Y - formatted.Height / 2);
        dc.DrawText(formatted, point);
    }

    private GeometryInfo GetGeometry()
    {
        var cell = Math.Max(0d, Math.Min((ActualWidth - Padding * 2) / 8d, (ActualHeight - Padding * 2) / 9d));
        return new GeometryInfo((ActualWidth - cell * 8) / 2, (ActualHeight - cell * 9) / 2, cell);
    }

    private static void OnPiecesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BoardControl)d;
        if (control._observablePieces is not null)
            WeakEventManager<INotifyCollectionChanged, NotifyCollectionChangedEventArgs>.RemoveHandler(
                control._observablePieces, nameof(INotifyCollectionChanged.CollectionChanged), control.OnCollectionChanged);
        control._observablePieces = e.NewValue as INotifyCollectionChanged;
        if (control._observablePieces is not null)
            WeakEventManager<INotifyCollectionChanged, NotifyCollectionChangedEventArgs>.AddHandler(
                control._observablePieces, nameof(INotifyCollectionChanged.CollectionChanged), control.OnCollectionChanged);
        control.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Replace &&
            e.OldItems?.OfType<PieceState>().FirstOrDefault() is { } oldPiece &&
            e.NewItems?.OfType<PieceState>().FirstOrDefault() is { } newPiece &&
            oldPiece.PieceId == newPiece.PieceId && oldPiece.Position != newPiece.Position)
        {
            _animatedPieceId = newPiece.PieceId;
            _animationFrom = oldPiece.Position;
            _animationTo = newPiece.Position;
            _animationStarted = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering -= OnAnimationFrame;
            CompositionTarget.Rendering += OnAnimationFrame;
        }
        InvalidateVisual();
    }

    private static ImageSource? GetClassicPieceImage(PieceState piece, bool marked)
    {
        var side = piece.Side == SideColor.Red ? "1" : "2";
        var name = piece.Type switch
        {
            PieceType.General => "tuong",
            PieceType.Advisor => "sy",
            PieceType.Elephant => "tinh",
            PieceType.Horse => "ma",
            PieceType.Chariot => "xe",
            PieceType.Cannon => "phao",
            _ => "chot"
        };
        var key = $"{side}{name}{(marked ? "_marked" : string.Empty)}.png";
        if (ClassicPieceImages.TryGetValue(key, out var cached)) return cached;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/Assets/Classic/Pieces/{key}", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            ClassicPieceImages[key] = image;
            return image;
        }
        catch { return null; }
    }

    private static ImageSource? LoadOceanBoardImage()
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/Assets/Sea/ocean-board-4k.png", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    private void OnAnimationFrame(object? sender, EventArgs e)
    {
        if (Stopwatch.GetElapsedTime(_animationStarted).TotalMilliseconds >= AnimationMilliseconds)
        {
            CompositionTarget.Rendering -= OnAnimationFrame;
            _animatedPieceId = null;
        }
        InvalidateVisual();
    }
    private readonly record struct GeometryInfo(double Left, double Top, double Cell)
    {
        public Point Point(int x, int y) => new(Left + x * Cell, Top + y * Cell);
    }
}

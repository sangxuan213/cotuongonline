using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UDM18.Client.Models;
using XiangqiOnline.Shared.Contracts;

namespace UDM18.Client.Controls;

public sealed class BoardControl : FrameworkElement
{
    private const double Padding = 34;
    private INotifyCollectionChanged? _observablePieces;

    public static readonly DependencyProperty PiecesProperty = DependencyProperty.Register(
        nameof(Pieces), typeof(IEnumerable), typeof(BoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPiecesChanged));
    public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register(
        nameof(Selected), typeof(Coordinate?), typeof(BoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LastFromProperty = DependencyProperty.Register(
        nameof(LastFrom), typeof(Coordinate?), typeof(BoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LastToProperty = DependencyProperty.Register(
        nameof(LastTo), typeof(Coordinate?), typeof(BoardControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(BoardOrientation), typeof(BoardControl),
        new FrameworkPropertyMetadata(BoardOrientation.RedAtBottom, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CoordinateClickedCommandProperty = DependencyProperty.Register(
        nameof(CoordinateClickedCommand), typeof(ICommand), typeof(BoardControl));

    public IEnumerable? Pieces { get => (IEnumerable?)GetValue(PiecesProperty); set => SetValue(PiecesProperty, value); }
    public Coordinate? Selected { get => (Coordinate?)GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    public Coordinate? LastFrom { get => (Coordinate?)GetValue(LastFromProperty); set => SetValue(LastFromProperty, value); }
    public Coordinate? LastTo { get => (Coordinate?)GetValue(LastToProperty); set => SetValue(LastToProperty, value); }
    public BoardOrientation Orientation { get => (BoardOrientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public ICommand? CoordinateClickedCommand { get => (ICommand?)GetValue(CoordinateClickedCommandProperty); set => SetValue(CoordinateClickedCommandProperty, value); }

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
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(246, 226, 184)), null, new Rect(0, 0, ActualWidth, ActualHeight), 18, 18);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(92, 55, 32)), 1.6);

        for (var y = 0; y <= 9; y++)
            dc.DrawLine(pen, geometry.Point(0, y), geometry.Point(8, y));
        for (var x = 0; x <= 8; x++)
        {
            dc.DrawLine(pen, geometry.Point(x, 0), geometry.Point(x, 4));
            dc.DrawLine(pen, geometry.Point(x, 5), geometry.Point(x, 9));
        }
        dc.DrawLine(pen, geometry.Point(3, 0), geometry.Point(5, 2));
        dc.DrawLine(pen, geometry.Point(5, 0), geometry.Point(3, 2));
        dc.DrawLine(pen, geometry.Point(3, 7), geometry.Point(5, 9));
        dc.DrawLine(pen, geometry.Point(5, 7), geometry.Point(3, 9));
        DrawText(dc, "SÔNG", new Point(geometry.Left + geometry.Cell * 1.6, geometry.Top + geometry.Cell * 4.48), 16, Brushes.SaddleBrown);
        DrawText(dc, "HÀ", new Point(geometry.Left + geometry.Cell * 5.7, geometry.Top + geometry.Cell * 4.48), 16, Brushes.SaddleBrown);

        DrawHighlight(dc, geometry, LastFrom, Color.FromArgb(110, 255, 193, 7));
        DrawHighlight(dc, geometry, LastTo, Color.FromArgb(145, 16, 185, 129));
        DrawHighlight(dc, geometry, Selected, Color.FromArgb(180, 59, 130, 246));

        if (Pieces is not null)
            foreach (var piece in Pieces.OfType<PieceState>().Where(p => !p.Captured)) DrawPiece(dc, geometry, piece);
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
        var center = geometry.Point(view.X, view.Y);
        var radius = geometry.Cell * 0.38;
        var color = piece.Side == Side.RED ? Color.FromRgb(185, 28, 28) : Color.FromRgb(31, 41, 55);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 250, 235)), new Pen(new SolidColorBrush(color), 2.6), center, radius, radius);
        DrawText(dc, Label(piece), new Point(center.X, center.Y), Math.Max(13, radius * .82), new SolidColorBrush(color), centered: true);
    }

    private void DrawHighlight(DrawingContext dc, GeometryInfo geometry, Coordinate? coordinate, Color color)
    {
        if (coordinate is null) return;
        var view = BoardGeometry.CanonicalToView(coordinate.Value, Orientation);
        dc.DrawEllipse(new SolidColorBrush(color), null, geometry.Point(view.X, view.Y), geometry.Cell * .47, geometry.Cell * .47);
    }

    private static string Label(PieceState piece) => piece.Type switch
    {
        PieceType.GENERAL => piece.Side == Side.RED ? "帥" : "將",
        PieceType.ADVISOR => piece.Side == Side.RED ? "仕" : "士",
        PieceType.ELEPHANT => piece.Side == Side.RED ? "相" : "象",
        PieceType.HORSE => "馬",
        PieceType.CHARIOT => "車",
        PieceType.CANNON => "炮",
        _ => piece.Side == Side.RED ? "兵" : "卒"
    };

    private static void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush, bool centered = false)
    {
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("vi-VN"), FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), size, brush, 1.0);
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

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
    private readonly record struct GeometryInfo(double Left, double Top, double Cell)
    {
        public Point Point(int x, int y) => new(Left + x * Cell, Top + y * Cell);
    }
}

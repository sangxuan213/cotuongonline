namespace UDM18.Client.Models;

public static class BoardGeometry
{
    public static Coordinate ViewToCanonical(int viewX, int viewY, BoardOrientation orientation)
    {
        if (viewX is < 0 or > 8 || viewY is < 0 or > 9)
            throw new ArgumentOutOfRangeException(nameof(viewX), "Board coordinates must be x=0..8, y=0..9.");
        return orientation == BoardOrientation.RedAtBottom
            ? new Coordinate(viewX, viewY)
            : new Coordinate(8 - viewX, 9 - viewY);
    }

    public static Coordinate CanonicalToView(Coordinate coordinate, BoardOrientation orientation)
    {
        if (!coordinate.IsInsideBoard)
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        return orientation == BoardOrientation.RedAtBottom
            ? coordinate
            : new Coordinate(8 - coordinate.X, 9 - coordinate.Y);
    }
}

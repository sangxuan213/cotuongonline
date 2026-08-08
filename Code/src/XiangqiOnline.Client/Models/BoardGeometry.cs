namespace UDM18.Client.Models;

using XiangqiOnline.Shared.Contracts;

public static class BoardGeometry
{
    public static Coordinate ViewToCanonical(int viewX, int viewY, BoardOrientation orientation)
    {
        if (viewX is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(viewX), viewX, "Board x must be 0..8.");
        if (viewY is < 0 or > 9)
            throw new ArgumentOutOfRangeException(nameof(viewY), viewY, "Board y must be 0..9.");
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

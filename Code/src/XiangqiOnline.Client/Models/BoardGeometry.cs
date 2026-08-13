namespace UDM18.Client.Models;

using XiangqiOnline.Shared.Models;

public static class BoardGeometry
{
    public static Position ViewToCanonical(int viewX, int viewY, BoardOrientation orientation)
    {
        if (viewX is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(viewX), viewX, "Board x must be 0..8.");
        if (viewY is < 0 or > 9)
            throw new ArgumentOutOfRangeException(nameof(viewY), viewY, "Board y must be 0..9.");
        return orientation == BoardOrientation.RedAtBottom
            ? new Position(viewX, viewY)
            : new Position(8 - viewX, 9 - viewY);
    }

    public static Position CanonicalToView(Position coordinate, BoardOrientation orientation)
    {
        if (!coordinate.IsValid())
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        return orientation == BoardOrientation.RedAtBottom
            ? coordinate
            : new Position(8 - coordinate.X, 9 - coordinate.Y);
    }
}

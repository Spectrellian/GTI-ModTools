namespace GTI.ModTools.Images;

public static class Swizzle
{
    public static bool CanSwizzle(int width, int height)
        => width % 8 == 0 && height % 8 == 0;

    public static (int x, int y) GetPixelCoordinates(int linearPixelIndex, int width, int height)
    {
        var tileCountX = width / 8;
        var tileIndex = linearPixelIndex / 64;
        var subIndex = linearPixelIndex % 64;

        var tileX = tileIndex % tileCountX;
        var tileY = tileIndex / tileCountX;

        var localX = (subIndex & 1) | ((subIndex >> 1) & 2) | ((subIndex >> 2) & 4);
        var localY = ((subIndex >> 1) & 1) | ((subIndex >> 2) & 2) | ((subIndex >> 3) & 4);

        var x = tileX * 8 + localX;
        var y = tileY * 8 + localY;

        return (x, y);
    }

    public static int GetLinearPixelIndexFromCoordinates(int x, int y, int width)
    {
        var tileCountX = width / 8;
        var tileX = x / 8;
        var tileY = y / 8;
        var localX = x % 8;
        var localY = y % 8;
        var subIndex =
            (localX & 1) |
            ((localY & 1) << 1) |
            ((localX & 2) << 1) |
            ((localY & 2) << 2) |
            ((localX & 4) << 2) |
            ((localY & 4) << 3);

        var tileIndex = tileY * tileCountX + tileX;
        return tileIndex * 64 + subIndex;
    }
}

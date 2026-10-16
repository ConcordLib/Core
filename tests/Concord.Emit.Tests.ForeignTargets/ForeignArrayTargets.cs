namespace Concord.Emit.Tests.ForeignTargets;

/// <summary>
///     Targets whose bodies index a rank-2 array, so a composed body carries the array pseudo-methods
///     no metadata token can name.
/// </summary>
public static class ForeignArrayTargets {
    public static int SumCorners(int[,] grid) {
        return grid[0, 0] + grid[1, 1];
    }

    public static int FillThenRead(int rows, int columns) {
        int[,] grid = new int[rows, columns];
        grid[1, 1] = 9;
        return grid[1, 1];
    }

    public static int BumpThroughReference(int[,] grid) {
        Bump(ref grid[0, 1]);
        return grid[0, 1];
    }

    private static void Bump(ref int slot) {
        slot++;
    }
}

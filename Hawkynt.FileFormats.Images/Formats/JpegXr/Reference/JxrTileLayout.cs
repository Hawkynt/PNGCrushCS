namespace SharpAstro.Jxr;

/// <summary>
/// Tile-grid description used by the pixel-level encoder facades to produce
/// multi-tile codestreams. Each tile encodes independently — its DC/LP
/// prediction resets at the tile boundary — so other decoders can parallelise
/// or random-access individual tiles (with an index table; see also
/// <c>INDEX_TABLE_TILES</c>).
/// </summary>
/// <param name="TileWidthInMb">Width of each tile column in macroblocks,
/// EXCLUDING the last column (whose width is derived from the image width).
/// Length must equal <c>numVerTiles - 1</c>.</param>
/// <param name="TileHeightInMb">Height of each tile row in macroblocks,
/// EXCLUDING the last row. Length must equal <c>numHorTiles - 1</c>.</param>
public sealed record JxrTileLayout(int[] TileWidthInMb, int[] TileHeightInMb)
{
    /// <summary>Number of tile columns = <c>TileWidthInMb.Length + 1</c>.</summary>
    public int NumVerTiles => TileWidthInMb.Length + 1;

    /// <summary>Number of tile rows = <c>TileHeightInMb.Length + 1</c>.</summary>
    public int NumHorTiles => TileHeightInMb.Length + 1;

    /// <summary>
    /// Uniform tile grid: split the image into <paramref name="cols"/> ×
    /// <paramref name="rows"/> tiles of roughly equal size. The last column
    /// and row absorb the remainder when dimensions don't divide evenly.
    /// </summary>
    public static JxrTileLayout Uniform(int totalWidthInMb, int totalHeightInMb, int cols, int rows)
    {
        if (cols < 1 || rows < 1) throw new ArgumentOutOfRangeException(nameof(cols), "tile counts must be ≥ 1");
        if (cols > totalWidthInMb || rows > totalHeightInMb)
            throw new ArgumentException(
                $"requested {cols}×{rows} tiles exceeds image MB grid {totalWidthInMb}×{totalHeightInMb}");

        var widths = new int[cols - 1];
        var baseW = totalWidthInMb / cols;
        for (var i = 0; i < cols - 1; i++) widths[i] = baseW;

        var heights = new int[rows - 1];
        var baseH = totalHeightInMb / rows;
        for (var i = 0; i < rows - 1; i++) heights[i] = baseH;

        return new JxrTileLayout(widths, heights);
    }

    /// <summary>
    /// Build the MB-edge masks that the DC-prediction encode/decode steps use to
    /// suppress prediction across tile
    /// boundaries. <paramref name="leftEdgeMask"/>[x, y] is true iff MB
    /// column <c>x</c> is the leftmost in some tile; similar for top.
    /// </summary>
    public (bool[,] leftEdgeMask, bool[,] topEdgeMask) BuildMasks(int widthInMb, int heightInMb)
    {
        var left = new bool[widthInMb, heightInMb];
        var top = new bool[widthInMb, heightInMb];

        // First column / row are always edges (image boundary).
        for (var y = 0; y < heightInMb; y++) left[0, y] = true;
        for (var x = 0; x < widthInMb; x++) top[x, 0] = true;

        // Internal tile column boundaries — start of every tile except the first.
        var col = 0;
        foreach (var tw in TileWidthInMb)
        {
            col += tw;
            if (col < widthInMb)
                for (var y = 0; y < heightInMb; y++) left[col, y] = true;
        }

        var row = 0;
        foreach (var th in TileHeightInMb)
        {
            row += th;
            if (row < heightInMb)
                for (var x = 0; x < widthInMb; x++) top[x, row] = true;
        }

        return (left, top);
    }
}

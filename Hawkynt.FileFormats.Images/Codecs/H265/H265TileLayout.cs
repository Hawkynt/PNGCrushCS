using System;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Maps HEVC coding tree blocks between picture raster scan and tile scan — clause 6.5.1.
/// </summary>
/// <remarks>
/// Slice headers name their first CTB in raster scan, while slice-segment data advances in tile scan.
/// Keeping the conversion here lets the decoder continue to address picture arrays by ordinary x/y
/// coordinates without confusing transport order with spatial order.
/// </remarks>
internal sealed class H265TileLayout {

  private readonly int[] _rsToTs;
  private readonly int[] _tsToRs;
  private readonly int[] _tileIdByRs;
  private readonly int[] _tileColumnStartByRs;
  private readonly int[] _tileRowStartByRs;
  private readonly bool[] _tileStartByRs;

  internal H265TileLayout(H265SequenceParameterSet sps, H265PictureParameterSet pps) {
    var pictureWidth = sps.PicWidthInCtbsY;
    var pictureHeight = (sps.PicSizeInCtbsY + pictureWidth - 1) / pictureWidth;
    var columns = pps.TilesEnabled ? pps.NumTileColumns : 1;
    var rows = pps.TilesEnabled ? pps.NumTileRows : 1;

    if (columns < 1 || rows < 1 || columns > pictureWidth || rows > pictureHeight)
      throw new InvalidDataException(
        $"This H.265 PPS divides a {pictureWidth}x{pictureHeight}-CTB picture into {columns}x{rows} tiles. "
        + "Every tile must contain at least one coding tree block.");

    var columnBoundary = _Boundaries(
      pictureWidth, columns, pps.UniformTileSpacing, pps.TileColumnWidths, "column");
    var rowBoundary = _Boundaries(
      pictureHeight, rows, pps.UniformTileSpacing, pps.TileRowHeights, "row");

    this._rsToTs = new int[sps.PicSizeInCtbsY];
    this._tsToRs = new int[sps.PicSizeInCtbsY];
    this._tileIdByRs = new int[sps.PicSizeInCtbsY];
    this._tileColumnStartByRs = new int[sps.PicSizeInCtbsY];
    this._tileRowStartByRs = new int[sps.PicSizeInCtbsY];
    this._tileStartByRs = new bool[sps.PicSizeInCtbsY];

    var ts = 0;
    for (var tileRow = 0; tileRow < rows; ++tileRow)
    for (var tileColumn = 0; tileColumn < columns; ++tileColumn) {
      var tileId = tileRow * columns + tileColumn;
      var left = columnBoundary[tileColumn];
      var right = columnBoundary[tileColumn + 1];
      var top = rowBoundary[tileRow];
      var bottom = rowBoundary[tileRow + 1];

      for (var y = top; y < bottom; ++y)
      for (var x = left; x < right; ++x) {
        var rs = y * pictureWidth + x;
        if (rs >= sps.PicSizeInCtbsY)
          continue;

        this._rsToTs[rs] = ts;
        this._tsToRs[ts] = rs;
        this._tileIdByRs[rs] = tileId;
        this._tileColumnStartByRs[rs] = left;
        this._tileRowStartByRs[rs] = top;
        this._tileStartByRs[rs] = x == left && y == top;
        ++ts;
      }
    }

    if (ts != sps.PicSizeInCtbsY)
      throw new InvalidDataException(
        $"The H.265 tile layout maps {ts} coding tree blocks, but the sequence contains {sps.PicSizeInCtbsY}.");
  }

  internal int ToTileScan(int rasterAddress) => this._rsToTs[rasterAddress];
  internal int ToRasterScan(int tileAddress) => this._tsToRs[tileAddress];
  internal int TileId(int rasterAddress) => this._tileIdByRs[rasterAddress];
  internal bool IsTileStart(int rasterAddress) => this._tileStartByRs[rasterAddress];
  internal int TileColumnStart(int rasterAddress) => this._tileColumnStartByRs[rasterAddress];
  internal int TileRowStart(int rasterAddress) => this._tileRowStartByRs[rasterAddress];

  internal bool SameTile(int firstRasterAddress, int secondRasterAddress)
    => firstRasterAddress >= 0 && secondRasterAddress >= 0
       && firstRasterAddress < this._tileIdByRs.Length && secondRasterAddress < this._tileIdByRs.Length
       && this._tileIdByRs[firstRasterAddress] == this._tileIdByRs[secondRasterAddress];

  private static int[] _Boundaries(
    int pictureSize, int count, bool uniform, int[] explicitSizes, string dimension) {
    var result = new int[count + 1];
    result[0] = 0;
    result[count] = pictureSize;

    if (uniform) {
      for (var i = 1; i < count; ++i)
        result[i] = i * pictureSize / count;
      return result;
    }

    if (explicitSizes.Length != count - 1)
      throw new InvalidDataException(
        $"An H.265 non-uniform tile grid states {explicitSizes.Length} explicit {dimension} sizes for {count} tiles.");

    var at = 0;
    for (var i = 0; i < explicitSizes.Length; ++i) {
      if (explicitSizes[i] <= 0)
        throw new InvalidDataException($"An H.265 tile {dimension} has no coding tree blocks.");

      at += explicitSizes[i];
      if (at >= pictureSize)
        throw new InvalidDataException(
          $"The explicit H.265 tile {dimension} sizes consume {at} CTBs of a {pictureSize}-CTB picture before the last tile.");

      result[i + 1] = at;
    }

    return result;
  }
}

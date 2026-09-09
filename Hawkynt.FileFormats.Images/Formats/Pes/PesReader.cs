using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Pes;

/// <summary>Reads the stitches out of a Brother PES embroidery file.</summary>
/// <remarks>
/// A PES is a needle path, not a raster. The file header gives an absolute offset to an embedded
/// PEC section. PEC carries the thread-chart indices and a delta-coded stream of stitches, jumps,
/// colour changes and an end marker.
/// </remarks>
public static class PesReader {

  private const int _PecHeaderSize = 512;
  private const int _PecColorCountOffset = 48;
  private const int _PecStitchHeaderSize = 16;

  // Files written by this library before PES became a registry writer used ImageMagick's historical
  // offset arithmetic and stated a zero PEC offset. Keep them readable, but never emit that layout.
  private const int _LegacyStitchOffset = 560;

  public static PesFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PES file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName));
  }

  public static PesFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromSpan(buffer.ToArray());
  }

  public static PesFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PesFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 12)
      throw new InvalidDataException("Data too small for a PES header.");
    if (data[0] != (byte)'#' || data[1] != (byte)'P' || data[2] != (byte)'E' || data[3] != (byte)'S')
      throw new InvalidDataException("Not a PES: the file does not begin with #PES.");

    var version = System.Text.Encoding.ASCII.GetString(data.Slice(4, 4));
    var pecOffset = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
    if (pecOffset < 0)
      throw new InvalidDataException($"PES states a PEC section at {pecOffset}.");

    if (pecOffset != 0 && (pecOffset < 12 || pecOffset > data.Length - _PecColorCountOffset - 1))
      throw new InvalidDataException("PES PEC section starts outside the file.");

    var colorCountAt = pecOffset == 0 ? _PecColorCountOffset : pecOffset + _PecColorCountOffset;
    if ((uint)colorCountAt >= (uint)data.Length)
      throw new InvalidDataException("PES PEC colour table starts past the end of the file.");

    var colorCount = data[colorCountAt] + 1;
    var colorTableAt = colorCountAt + 1;
    if (colorTableAt > data.Length - colorCount)
      throw new InvalidDataException("PES colour table runs past the end of the file.");

    var threadIndices = new int[colorCount];
    for (var i = 0; i < colorCount; ++i)
      threadIndices[i] = data[colorTableAt + i];

    int at;
    int stitchEnd;

    if (pecOffset == 0) {
      if (_LegacyStitchOffset >= data.Length)
        throw new InvalidDataException("PES stitch data starts outside the file.");
      at = _LegacyStitchOffset;
      stitchEnd = data.Length;
    } else {
      if (pecOffset > data.Length - _PecHeaderSize - _PecStitchHeaderSize)
        throw new InvalidDataException("PES PEC section is too short to contain a stitch block.");

      var stitchBlockAt = pecOffset + _PecHeaderSize;
      var stitchBlockLength = _ReadUInt24LittleEndian(data.Slice(stitchBlockAt + 2, 3));
      if (stitchBlockLength < _PecStitchHeaderSize || stitchBlockLength > data.Length - stitchBlockAt)
        throw new InvalidDataException("PES PEC stitch block runs past the end of the file.");

      at = stitchBlockAt + _PecStitchHeaderSize;
      stitchEnd = stitchBlockAt + stitchBlockLength;
    }

    var blocks = new List<PesStitchBlock>();
    var points = new List<(int X, int Y)>();
    var jumpIndices = new List<int>();
    var blockIndex = 0;
    var x = 0;
    var y = 0;
    var ended = false;

    void CloseBlock() {
      if (points.Count == 0)
        return;

      var index = blockIndex < threadIndices.Length ? threadIndices[blockIndex] : 0;
      blocks.Add(new PesStitchBlock {
        ThreadIndex = index,
        Color = PesThreadChart.Colors[index & 0xFF],
        Points = points.ToArray(),
        JumpIndices = jumpIndices.ToArray(),
      });
      points = new List<(int X, int Y)>();
      jumpIndices = new List<int>();
    }

    while (at < stitchEnd) {
      if (at + 1 < data.Length && data[at] == 0xFF && data[at + 1] == 0x00) {
        at += 2;
        ended = true;
        break;
      }

      if (at + 1 < stitchEnd && data[at] == 0xFE && data[at + 1] == 0xB0) {
        if (at + 2 >= stitchEnd)
          throw new InvalidDataException("PES colour change runs past the end of the stitch block.");
        at += 3;
        CloseBlock();
        ++blockIndex;
        continue;
      }

      var flags = 0;
      var dx = _ReadAxis(data, ref at, stitchEnd, ref flags);
      var dy = _ReadAxis(data, ref at, stitchEnd, ref flags);

      var nextX = (long)x + dx;
      var nextY = (long)y + dy;
      if (nextX is < int.MinValue or > int.MaxValue || nextY is < int.MinValue or > int.MaxValue)
        throw new InvalidDataException("PES stitch coordinates overflow the supported integer range.");

      x = (int)nextX;
      y = (int)nextY;
      if ((flags & 0x30) != 0)
        jumpIndices.Add(points.Count);
      points.Add((x, y));
    }

    if (!ended)
      throw new InvalidDataException("PES stitch block has no end marker.");

    CloseBlock();
    if (blocks.Count == 0)
      throw new InvalidDataException("PES carries no stitches.");

    var minX = int.MaxValue;
    var minY = int.MaxValue;
    var maxX = int.MinValue;
    var maxY = int.MinValue;
    foreach (var block in blocks)
    foreach (var point in block.Points) {
      if (point.X < minX) minX = point.X;
      if (point.X > maxX) maxX = point.X;
      if (point.Y < minY) minY = point.Y;
      if (point.Y > maxY) maxY = point.Y;
    }

    return new PesFile {
      Version = version,
      Blocks = blocks,
      MinX = minX,
      MinY = minY,
      MaxX = maxX,
      MaxY = maxY,
    };
  }

  private static int _ReadAxis(ReadOnlySpan<byte> data, ref int at, int end, ref int flags) {
    if (at >= end)
      throw new InvalidDataException("PES stitch axis runs past the end of the stitch block.");

    var first = data[at++];
    if ((first & 0x80) == 0)
      return (first & 0x40) == 0 ? first : first - 0x80;

    if (at >= end)
      throw new InvalidDataException("PES long stitch axis runs past the end of the stitch block.");

    flags |= first & 0x30;
    var value = ((first & 0x0F) << 8) | data[at++];
    return (value & 0x800) == 0 ? value : value - 0x1000;
  }

  private static int _ReadUInt24LittleEndian(ReadOnlySpan<byte> data)
    => data[0] | (data[1] << 8) | (data[2] << 16);
}

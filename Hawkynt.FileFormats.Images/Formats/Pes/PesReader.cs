using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Pes;

/// <summary>Reads the stitches out of a Brother PES embroidery file.</summary>
/// <remarks>
/// A PES is a needle path, not a raster. What it holds is a run of moves, each a
/// delta from the last, split into blocks that each name a thread colour; the
/// picture is what those moves draw.
///
/// <para>Layout: <c>#PES</c>, a four-byte version, then a 32-bit offset to the
/// PEC section. The PEC section begins 36 bytes past that offset with a byte one
/// less than the number of colour blocks, that many thread indices, and then a
/// fixed run of bytes before the stitches themselves.</para>
///
/// <para>A move is one or two bytes per axis. With the top bit clear it is a
/// seven-bit signed step; with it set the axis takes twelve bits from this byte
/// and the next. Two pairs are not moves at all: <c>FF 00</c> ends the stitching
/// and <c>FE B0</c> begins the next colour block, which is why the block a
/// stitch belongs to cannot be known without walking the whole run.</para>
/// </remarks>
public static class PesReader {

  private const int _PecHeaderSkip = 36;
  private const int _PecFixedRun = 532;
  private const int _PecColorFieldSize = 21;

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

    // The colour count sits 36 bytes past the stated offset, which is measured
    // from the end of the twelve-byte header.
    var at = checked(12 + pecOffset + _PecHeaderSkip);
    if (at >= data.Length)
      throw new InvalidDataException("PES PEC section starts past the end of the file.");

    var colorCount = data[at++] + 1;
    if (at + colorCount > data.Length)
      throw new InvalidDataException("PES colour table runs past the end of the file.");

    var threadIndices = new int[colorCount];
    for (var i = 0; i < colorCount; ++i)
      threadIndices[i] = data[at++];

    at = checked(at + _PecFixedRun - colorCount - _PecColorFieldSize);
    if (at < 0 || at > data.Length)
      throw new InvalidDataException("PES stitch data starts outside the file.");

    var blocks = new List<PesStitchBlock>();
    var points = new List<(int X, int Y)>();
    var blockIndex = 0;
    var x = 0;
    var y = 0;

    void CloseBlock() {
      if (points.Count == 0)
        return;

      var index = blockIndex < threadIndices.Length ? threadIndices[blockIndex] : 0;
      blocks.Add(new PesStitchBlock {
        ThreadIndex = index,
        Color = PesThreadChart.Colors[index & 0xFF],
        Points = points.ToArray(),
      });
      points = new List<(int X, int Y)>();
    }

    while (at + 1 < data.Length) {
      int dx = data[at];
      int dy = data[at + 1];
      at += 2;

      if (dx == 0xFF && dy == 0x00)
        break;

      // The colour-change pair carries one byte of its own after it.
      if (dx == 0xFE && dy == 0xB0) {
        if (at >= data.Length)
          throw new InvalidDataException("PES colour change runs past the end of the file.");
        ++at;
        CloseBlock();
        ++blockIndex;
        continue;
      }

      if ((dx & 0x80) == 0) {
        if ((dx & 0x40) != 0)
          dx -= 0x80;
      } else {
        dx = ((dx & 0x0F) << 8) + dy;
        if ((dx & 0x800) != 0)
          dx -= 0x1000;
        if (at >= data.Length)
          throw new InvalidDataException("PES stitch runs past the end of the file.");
        dy = data[at++];
      }

      if ((dy & 0x80) == 0) {
        if ((dy & 0x40) != 0)
          dy -= 0x80;
      } else {
        if (at >= data.Length)
          throw new InvalidDataException("PES stitch runs past the end of the file.");
        dy = ((dy & 0x0F) << 8) + data[at++];
        if ((dy & 0x800) != 0)
          dy -= 0x1000;
      }

      x += dx;
      y += dy;
      points.Add((x, y));
    }

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
}

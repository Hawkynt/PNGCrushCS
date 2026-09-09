using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Pes;

/// <summary>Serializes Brother PES stitch data.</summary>
/// <remarks>
/// The writer emits the minimal valid PES version-1 document described by Brother-compatible
/// tooling: a 22-byte PES header followed by an embedded 512-byte PEC header, one PEC stitch block,
/// and blank 48x38 preview bitmaps. The PES object tree is intentionally absent; this representation
/// is the standard "truncated PES v1" used when the machine stitches are the authoritative data.
/// </remarks>
public static class PesWriter {

  private const int _PesHeaderSize = 22;
  private const int _PecHeaderSize = 512;
  private const int _PecStitchHeaderSize = 16;
  private const int _PecGraphicSize = 6 * 38;
  private const int _MinimumStep = -2048;
  private const int _MaximumStep = 2047;
  private const int _MaximumStitchBlockLength = 0x00FF_FFFF;

  /// <summary>Writes a canonical minimal PES v1 file.</summary>
  public static byte[] ToBytes(PesFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Blocks.Count is < 1 or > 256)
      throw new ArgumentException("PES requires between 1 and 256 colour blocks.", nameof(file));

    var (minX, minY, maxX, maxY) = _Bounds(file);
    var width = (long)maxX - minX;
    var height = (long)maxY - minY;
    if (width is < 0 or > ushort.MaxValue || height is < 0 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(file), "PES stitch extents may not exceed 65535 units per axis.");

    using var stitches = new MemoryStream();
    var currentX = 0;
    var currentY = 0;
    var colorToggle = 2;

    for (var blockIndex = 0; blockIndex < file.Blocks.Count; ++blockIndex) {
      var block = file.Blocks[blockIndex];
      if ((uint)block.ThreadIndex > byte.MaxValue)
        throw new ArgumentOutOfRangeException(nameof(file), $"PES thread index {block.ThreadIndex} is outside the byte-sized PEC palette.");
      if (block.Points.Length == 0)
        throw new ArgumentException($"PES colour block {blockIndex} has no stitch points.", nameof(file));

      var isJump = new bool[block.Points.Length];
      foreach (var jumpIndex in block.JumpIndices) {
        if ((uint)jumpIndex >= (uint)block.Points.Length)
          throw new ArgumentOutOfRangeException(nameof(file), $"PES jump index {jumpIndex} is outside block {blockIndex}.");
        isJump[jumpIndex] = true;
      }

      if (blockIndex != 0) {
        stitches.WriteByte(0xFE);
        stitches.WriteByte(0xB0);
        stitches.WriteByte((byte)colorToggle);
        colorToggle = colorToggle == 2 ? 1 : 2;
      }

      for (var pointIndex = 0; pointIndex < block.Points.Length; ++pointIndex) {
        var (x, y) = block.Points[pointIndex];
        _WriteMove(stitches, ref currentX, ref currentY, x, y, isJump[pointIndex]);
      }
    }

    // PEC defines END as FF. Writing the following zero explicitly also satisfies older readers
    // that recognize the two-byte FF 00 pair rather than the command byte alone.
    stitches.WriteByte(0xFF);
    stitches.WriteByte(0x00);

    var stitchBlockLength = checked(_PecStitchHeaderSize + (int)stitches.Length);
    if (stitchBlockLength > _MaximumStitchBlockLength)
      throw new ArgumentException("PES stitch data exceeds the 24-bit PEC block length.", nameof(file));

    using var output = new MemoryStream(
      checked(_PesHeaderSize + _PecHeaderSize + stitchBlockLength + _PecGraphicSize * (file.Blocks.Count + 1)));

    output.Write("#PES0001"u8);

    Span<byte> pesHeaderTail = stackalloc byte[_PesHeaderSize - 8];
    pesHeaderTail.Clear();
    BinaryPrimitives.WriteUInt32LittleEndian(pesHeaderTail, _PesHeaderSize);
    output.Write(pesHeaderTail);

    var pecHeader = new byte[_PecHeaderSize];
    pecHeader.AsSpan().Fill(0x20);
    "LA:Untitled        \r"u8.CopyTo(pecHeader);
    pecHeader[32] = 0xFF;
    pecHeader[33] = 0x00;
    pecHeader[34] = 0x06;
    pecHeader[35] = 0x26;
    pecHeader[48] = (byte)(file.Blocks.Count - 1);
    for (var i = 0; i < file.Blocks.Count; ++i)
      pecHeader[49 + i] = (byte)file.Blocks[i].ThreadIndex;
    output.Write(pecHeader);

    Span<byte> stitchHeader = stackalloc byte[_PecStitchHeaderSize];
    stitchHeader.Clear();
    _WriteUInt24LittleEndian(stitchHeader[2..5], stitchBlockLength);
    stitchHeader[5] = 0x31;
    stitchHeader[6] = 0xFF;
    stitchHeader[7] = 0xF0;
    BinaryPrimitives.WriteUInt16LittleEndian(stitchHeader[8..10], (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(stitchHeader[10..12], (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(stitchHeader[12..14], 0x01E0);
    BinaryPrimitives.WriteUInt16LittleEndian(stitchHeader[14..16], 0x01B0);
    output.Write(stitchHeader);

    stitches.Position = 0;
    stitches.CopyTo(output);

    // The previews are advisory. The conventional blank PEC preview is a rounded rectangular
    // frame; repeat it once for the design and once per colour block.
    var blankGraphic = _CreateBlankGraphic();
    for (var i = 0; i <= file.Blocks.Count; ++i)
      output.Write(blankGraphic);

    return output.ToArray();
  }

  private static byte[] _CreateBlankGraphic() {
    const int stride = 6;
    const int height = 38;
    var result = new byte[stride * height];

    void Mark(byte[] graphic, int x, int y)
      => graphic[y * stride + (x >> 3)] |= (byte)(1 << (x & 7));

    for (var x = 4; x <= 43; ++x) {
      Mark(result, x, 1);
      Mark(result, x, 36);
    }

    foreach (var (y, left, right) in new[] { (2, 3, 44), (3, 2, 45), (34, 2, 45), (35, 3, 44) }) {
      Mark(result, left, y);
      Mark(result, right, y);
    }

    for (var y = 4; y <= 33; ++y) {
      Mark(result, 1, y);
      Mark(result, 46, y);
    }

    return result;
  }

  private static void _WriteMove(Stream stream, ref int currentX, ref int currentY, int targetX, int targetY, bool jump) {
    var wroteMove = false;
    while (currentX != targetX || currentY != targetY) {
      var remainingX = (long)targetX - currentX;
      var remainingY = (long)targetY - currentY;
      var dx = (int)Math.Clamp(remainingX, _MinimumStep, _MaximumStep);
      var dy = (int)Math.Clamp(remainingY, _MinimumStep, _MaximumStep);

      _WriteLongAxis(stream, dx, jump);
      _WriteLongAxis(stream, dy, jump);
      currentX += dx;
      currentY += dy;
      wroteMove = true;
    }

    // A zero-length stitch is meaningful for a one-pixel run, and a zero-length jump is how an
    // invisible canvas anchor at the current position survives a serialize/parse round trip.
    if (!wroteMove) {
      _WriteLongAxis(stream, 0, jump);
      _WriteLongAxis(stream, 0, jump);
    }
  }

  private static void _WriteLongAxis(Stream stream, int value, bool jump) {
    var encoded = value & 0x0FFF;
    stream.WriteByte((byte)(0x80 | (jump ? 0x10 : 0x00) | ((encoded >> 8) & 0x0F)));
    stream.WriteByte((byte)encoded);
  }

  private static void _WriteUInt24LittleEndian(Span<byte> destination, int value) {
    destination[0] = (byte)value;
    destination[1] = (byte)(value >> 8);
    destination[2] = (byte)(value >> 16);
  }

  private static (int MinX, int MinY, int MaxX, int MaxY) _Bounds(PesFile file) {
    var havePoint = false;
    var minX = int.MaxValue;
    var minY = int.MaxValue;
    var maxX = int.MinValue;
    var maxY = int.MinValue;

    foreach (var block in file.Blocks)
    foreach (var (x, y) in block.Points) {
      havePoint = true;
      minX = Math.Min(minX, x);
      minY = Math.Min(minY, y);
      maxX = Math.Max(maxX, x);
      maxY = Math.Max(maxY, y);
    }

    if (!havePoint)
      throw new ArgumentException("PES requires at least one stitch point.", nameof(file));

    return (minX, minY, maxX, maxY);
  }
}

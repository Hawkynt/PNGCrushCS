using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Pes;

/// <summary>Writes stitches back out as a Brother PES.</summary>
/// <remarks>
/// This serialises a needle path a caller already has. It is deliberately not
/// the registry's writer: what the registry would ask for is a PES made from a
/// picture, and deciding where to put every stitch to imitate a raster is
/// needlework rather than serialisation. Given stitches, though, writing the
/// file they came from is exactly a serialiser's job, and it is what lets the
/// reader be checked against another tool — a file written here is handed to
/// ImageMagick, which reports the extent it read back.
///
/// <para>Only the part of the format the reader uses is written: the twelve-byte
/// header, then the PEC section with its colour table and stitch run. The PES
/// section proper, which carries the design's object tree for editing software,
/// is left empty, and every reader that treats a PES as a picture goes to the
/// PEC section for it.</para>
/// </remarks>
public static class PesWriter {

  private const int _PecHeaderSkip = 36;
  private const int _PecFixedRun = 532;
  private const int _PecColorFieldSize = 21;

  /// <summary>The largest step one stitch can state on an axis.</summary>
  private const int _MaxStep = 2047;

  private const int _MinStep = -2048;

  public static byte[] ToBytes(PesFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Blocks.Count == 0)
      throw new ArgumentException("A PES needs at least one block of stitches.", nameof(file));
    if (file.Blocks.Count > 256)
      throw new ArgumentException($"A PES states its blocks in one byte; {file.Blocks.Count} is too many.", nameof(file));

    using var body = new MemoryStream();
    var x = 0;
    var y = 0;
    for (var block = 0; block < file.Blocks.Count; ++block) {
      if (block > 0) {
        body.WriteByte(0xFE);
        body.WriteByte(0xB0);
        // The byte after a colour change alternates between one and two in files
        // Brother's own software writes; either is read the same way.
        body.WriteByte((byte)((block & 1) == 1 ? 1 : 2));
      }

      foreach (var (pointX, pointY) in file.Blocks[block].Points) {
        var stepX = pointX - x;
        var stepY = pointY - y;
        if (stepX is < _MinStep or > _MaxStep || stepY is < _MinStep or > _MaxStep)
          throw new ArgumentException($"A step of ({stepX}, {stepY}) is longer than one stitch can state.", nameof(file));

        // A step is always written in its long form: the short form is seven bits
        // signed, and choosing between them per axis would save two bytes a
        // stitch and cost the reader nothing it does not already handle.
        _WriteLongStep(body, stepX);
        _WriteLongStep(body, stepY);
        x = pointX;
        y = pointY;
      }
    }

    body.WriteByte(0xFF);
    body.WriteByte(0x00);

    var stitches = body.ToArray();
    var colorCount = file.Blocks.Count;

    // Everything before the colour count: the header, then the run the reader
    // steps over to reach the PEC section.
    var pecStart = 12 + _PecHeaderSkip;
    var afterColors = pecStart + 1 + colorCount;
    var stitchStart = afterColors + _PecFixedRun - colorCount - _PecColorFieldSize;

    var result = new byte[stitchStart + stitches.Length];
    result[0] = (byte)'#';
    result[1] = (byte)'P';
    result[2] = (byte)'E';
    result[3] = (byte)'S';
    var version = file.Version is { Length: 4 } ? file.Version : "0001";
    for (var i = 0; i < 4; ++i)
      result[4 + i] = (byte)version[i];

    // The stated offset is measured from the end of this header.
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), 0);

    result[pecStart] = (byte)(colorCount - 1);
    for (var i = 0; i < colorCount; ++i)
      result[pecStart + 1 + i] = (byte)file.Blocks[i].ThreadIndex;

    stitches.CopyTo(result.AsSpan(stitchStart));
    return result;
  }

  private static void _WriteLongStep(Stream body, int step) {
    var value = step & 0x0FFF;
    body.WriteByte((byte)(0x80 | (value >> 8)));
    body.WriteByte((byte)value);
  }
}

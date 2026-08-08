using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DelmPaint;

/// <summary>Assembles a DelmPaint picture from a <see cref="DelmPaintFile"/>.</summary>
public static class DelmPaintWriter {

  /// <summary>Blocks the single-quadrant form declares, the third being read as the remainder.</summary>
  private const int _DECLARED_BLOCKS = 2;

  /// <summary>
  /// Packs the three blocks and writes the table of lengths the reader walks them by.
  /// </summary>
  /// <remarks>
  /// Only the first two lengths are stored. The third block is whatever is left between the second's
  /// end and the end of the file, which is why nothing may follow it.
  /// </remarks>
  public static byte[] ToBytes(DelmPaintFile file) {
    var unpacked = file.Unpacked ?? [];
    var blocks = new byte[3][];

    for (var block = 0; block < blocks.Length; ++block) {
      var source = new byte[DelmPaintFile.BlockSize];
      var at = block * DelmPaintFile.BlockSize;
      if (at < unpacked.Length)
        unpacked.AsSpan(at, Math.Min(DelmPaintFile.BlockSize, unpacked.Length - at)).CopyTo(source);

      blocks[block] = AtariStCaRle.Pack(source);
    }

    using var output = new MemoryStream();
    for (var block = 0; block < _DECLARED_BLOCKS; ++block) {
      var length = blocks[block].Length;
      output.WriteByte((byte)(length >> 24));
      output.WriteByte((byte)(length >> 16));
      output.WriteByte((byte)(length >> 8));
      output.WriteByte((byte)length);
    }

    foreach (var block in blocks)
      output.Write(block, 0, block.Length);

    return output.ToArray();
  }
}

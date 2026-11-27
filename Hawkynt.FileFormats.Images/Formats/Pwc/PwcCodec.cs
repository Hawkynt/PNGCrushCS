using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Pwc;

/// <summary>Runs the PWC models over a whole picture of one-byte colour indices.</summary>
internal static class PwcCodec {

  /// <summary>Decodes <paramref name="height"/> rows from the coding block starting at <paramref name="offset"/>.</summary>
  /// <returns>One index byte per pixel, rows in the order they were coded.</returns>
  public static byte[] Decode(byte[] data, int offset, int width, int height, int depth, int colors, PwcCodingFlags flags, int maxLps) {
    var coder = PwcArithmeticCoder.ForDecoding(data, offset, maxLps);
    var pixels = new byte[(long)width * height];
    _Run(coder, pixels, width, height, depth, colors, flags);

    // The shipped decoder never checks this, but every stream its encoder writes ends with the code value
    // resting on the interval's lower end; anything else means the bits were not the ones encoded.
    if (!coder.EndedCleanly)
      throw new InvalidDataException("PWC arithmetic-coded data does not end where its encoder left it; the file is corrupt.");

    return pixels;
  }

  /// <summary>Encodes a picture of one-byte colour indices into a coding block (without the option word).</summary>
  public static byte[] Encode(byte[] pixels, int width, int height, int depth, int colors, PwcCodingFlags flags, int maxLps) {
    var output = new List<byte>(pixels.Length / 4 + 16);
    var coder = PwcArithmeticCoder.ForEncoding(output, maxLps);
    _Run(coder, (byte[])pixels.Clone(), width, height, depth, colors, flags);
    coder.Flush();
    return [.. output];
  }

  private static void _Run(PwcArithmeticCoder coder, byte[] pixels, int width, int height, int depth, int colors, PwcCodingFlags flags) {
    if (depth == 1) {
      var bilevel = new PwcBilevelModel(coder, width);
      for (var y = 0; y < height; ++y)
        bilevel.CodeRow(pixels.AsSpan(y * width, width));

      return;
    }

    if (depth != 4 && depth != 8)
      throw new InvalidDataException($"PWC has no model for {depth}-bit pictures.");

    // The colour coder spans the palette; a palette of fewer than two entries still gets the full byte range.
    var maximum = colors - 1 < 1 ? 255 : colors - 1;
    var model = new PwcRegionModel(coder, new PwcSymbolCoder(maximum), width, depth, flags);
    var row = new byte[width];
    for (var y = 0; y < height; ++y) {
      var slice = pixels.AsSpan(y * width, width);
      if (coder.IsEncoding)
        slice.CopyTo(row);

      model.CodeRow(row);
      row.CopyTo(slice);
    }
  }
}

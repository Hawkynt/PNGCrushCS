using System;
using System.Collections.Generic;

namespace FileFormat.TrueColorImg;

/// <summary>Assembles true-colour GEM bit image bytes from a <see cref="TrueColorImgFile"/>.</summary>
public static class TrueColorImgWriter {

  /// <summary>
  /// Writes the chunky variant, which stores whole pixels rather than twenty-four bitplanes.
  /// </summary>
  /// <remarks>
  /// The bitplane variants would have to run every plane through GEM's line coder separately, and
  /// nothing is gained by it: a photograph has no runs in any single plane, so the packed form is
  /// larger than the plain one. The chunky variant is what the format offers for exactly that case.
  /// </remarks>
  public static byte[] ToBytes(TrueColorImgFile file) {
    var pixels = file.Pixels ?? [];
    var body = new List<byte>();

    void Word(int value) {
      body.Add((byte)(value >> 8));
      body.Add((byte)value);
    }

    Word(1);

    // The header length is counted in words, and eighteen bytes is what the chunky variant has.
    Word(9);
    Word(24);

    // No pattern, this variant having no line coder to use one.
    Word(0);

    // Square pixels, in the tenths of a millimetre the format measures them in.
    Word(372);
    Word(372);
    Word(file.Width);
    Word(file.Height);
    Word(3);

    var count = file.Width * file.Height;
    for (var i = 0; i < count;) {
      var run = Math.Min(count - i, 255);
      body.Add(128);
      body.Add((byte)run);

      for (var j = 0; j < run; ++j) {
        var source = (i + j) * 3;
        body.Add(source + 2 < pixels.Length ? pixels[source + 2] : (byte)0);
        body.Add(source + 1 < pixels.Length ? pixels[source + 1] : (byte)0);
        body.Add(source < pixels.Length ? pixels[source] : (byte)0);
      }

      i += run;
    }

    return body.ToArray();
  }
}

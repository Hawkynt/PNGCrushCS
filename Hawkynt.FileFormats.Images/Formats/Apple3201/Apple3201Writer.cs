using System;
using System.Collections.Generic;

namespace FileFormat.Apple3201;

/// <summary>Assembles 3201 picture bytes from an <see cref="Apple3201File"/>.</summary>
public static class Apple3201Writer {

  /// <summary>
  /// Writes the signature, the two hundred palettes and then the bitmap packed with PackBytes.
  /// </summary>
  /// <remarks>
  /// PackBytes has three shapes of run and the encoder uses two of them: a byte repeated, and a
  /// stretch taken as it is. The four-byte pattern is left out because finding one costs more than
  /// it saves on a picture that already changes palette every line — a repeating four-byte group is
  /// a dither, and a dither here is what the per-line palette exists to avoid.
  /// </remarks>
  public static byte[] ToBytes(Apple3201File file) {
    var data = file.Data ?? [];
    var bitmap = file.Bitmap ?? [];
    var body = new List<byte>(Apple3201File.Signature.ToArray());

    // The palettes come before the bitmap and are not packed.
    for (var i = 0; i < Apple3201File.Height * Apple3201File.PaletteSize; ++i) {
      var at = Apple3201File.PalettesOffset + i;
      body.Add(at < data.Length ? data[at] : (byte)0);
    }

    for (var i = 0; i < bitmap.Length;) {
      var run = 1;
      while (run < 256 && i + run < bitmap.Length && bitmap[i + run] == bitmap[i])
        ++run;

      // A run of one repeated byte pays from three upwards; below that the literal form is shorter.
      if (run >= 3) {
        body.Add((byte)(0x80 | ((run - 1) >> 2)));
        body.Add(bitmap[i]);
        i += ((run - 1) >> 2 & 63) * 4 + 4;
        continue;
      }

      // A stretch of literals, at most 64 at a time.
      var literals = 0;
      while (literals < 64 && i + literals < bitmap.Length) {
        var same = 1;
        while (same < 3 && i + literals + same < bitmap.Length
               && bitmap[i + literals + same] == bitmap[i + literals])
          ++same;

        if (same >= 3)
          break;

        ++literals;
      }

      if (literals == 0)
        literals = 1;

      body.Add((byte)(literals - 1));
      for (var j = 0; j < literals; ++j)
        body.Add(bitmap[i + j]);

      i += literals;
    }

    return body.ToArray();
  }
}

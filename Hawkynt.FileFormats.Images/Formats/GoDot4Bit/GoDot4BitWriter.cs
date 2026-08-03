using System;
using System.Collections.Generic;

namespace FileFormat.GoDot4Bit;

/// <summary>Assembles GoDot picture or clip bytes from a <see cref="GoDot4BitFile"/>.</summary>
public static class GoDot4BitWriter {

  /// <summary>
  /// Writes the signature, a clip's size where there is one, and the packed pixels.
  /// </summary>
  /// <remarks>
  /// This used to copy 16384 raw bytes out with no signature and no packing, which is not a file any
  /// GoDot reader would take.
  /// </remarks>
  public static byte[] ToBytes(GoDot4BitFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var cells = file.PixelData ?? [];
    var output = new List<byte>(cells.Length / 2);

    foreach (var b in file.IsClip ? GoDot4BitFile.ClipMagic : GoDot4BitFile.ScreenMagic)
      output.Add(b);

    if (file.IsClip) {
      output.Add(0);
      output.Add(0);
      output.Add((byte)(file.Width / 8));
      output.Add((byte)(file.Height / 8));
    }

    for (var at = 0; at < cells.Length;) {
      var run = 1;
      while (run < 256 && at + run < cells.Length && cells[at + run] == cells[at])
        ++run;

      // A run costs three bytes, so it is worth coding from four alike upwards — and the escape can
      // never stand for itself, however short its run.
      if (run >= 4 || cells[at] == GoDot4BitFile.RunEscape) {
        output.Add(GoDot4BitFile.RunEscape);
        output.Add((byte)(run == 256 ? 0 : run));
        output.Add(cells[at]);
      } else
        for (var i = 0; i < run; ++i)
          output.Add(cells[at]);

      at += run;
    }

    return output.ToArray();
  }
}

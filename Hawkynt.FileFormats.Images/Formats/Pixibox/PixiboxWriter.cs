using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.Pixibox;

/// <summary>Writes bottom-up Pixibox RGB run-length coding.</summary>
public static class PixiboxWriter {

  public static byte[] ToBytes(PixiboxFile file) {
    if (file.Width is < 1 or > ushort.MaxValue || file.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"Pixibox dimensions must fit 16-bit fields; got {file.Width}x{file.Height}.", nameof(file));
    var expected = checked(file.Width * file.Height * 3);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"Pixibox needs {expected} RGB bytes.", nameof(file));

    var coded = new List<byte>(Math.Min(expected, 1 << 20));
    for (var storedRow = 0; storedRow < file.Height; ++storedRow) {
      var y = file.Height - 1 - storedRow;
      var x = 0;
      while (x < file.Width) {
        var p = (y * file.Width + x) * 3;
        var r = file.PixelData[p];
        var g = file.PixelData[p + 1];
        var b = file.PixelData[p + 2];
        var run = 1;
        while (run < 255 && x + run < file.Width) {
          var q = (y * file.Width + x + run) * 3;
          if (file.PixelData[q] != r || file.PixelData[q + 1] != g || file.PixelData[q + 2] != b)
            break;
          ++run;
        }

        coded.Add((byte)run);
        coded.Add(r);
        coded.Add(g);
        coded.Add(b);
        coded.Add(0); // fourth colour byte is ignored by the verified reader
        x += run;
      }
    }

    var output = new byte[checked(PixiboxFile.PixelDataOffset + coded.Count)];
    PixiboxFile.Signature.CopyTo(output);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(PixiboxFile.WidthOffset, 2), checked((ushort)file.Width));
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(PixiboxFile.HeightOffset, 2), checked((ushort)file.Height));
    coded.CopyTo(output, PixiboxFile.PixelDataOffset);
    return output;
  }
}

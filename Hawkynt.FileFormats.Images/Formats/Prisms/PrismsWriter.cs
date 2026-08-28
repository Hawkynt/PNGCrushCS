using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.Prisms;

/// <summary>Writes Prisms/LucasFilm pictures using the format's literal-run command.</summary>
public static class PrismsWriter {

  private const int _DataOffset = 0x210;

  public static byte[] ToBytes(PrismsFile file) {
    if (file.Width is < 1 or > ushort.MaxValue || file.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"Prisms dimensions must fit 16-bit fields; got {file.Width}x{file.Height}.", nameof(file));
    var expected = checked(file.Width * file.Height * 3);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"Prisms needs {expected} RGB bytes.", nameof(file));

    var coded = new List<byte>(expected + expected / 128);
    for (var storedRow = 0; storedRow < file.Height; ++storedRow) {
      var y = file.Height - 1 - storedRow;
      var x = 0;
      while (x < file.Width) {
        var run = Math.Min(256, file.Width - x);
        coded.Add((byte)(run - 1));
        coded.Add(PrismsFile.OpcodeLiteral);
        for (var i = 0; i < run; ++i) {
          var p = (y * file.Width + x + i) * 3;
          coded.Add(0); // ignored component
          coded.Add(file.PixelData[p + 2]);
          coded.Add(file.PixelData[p + 1]);
          coded.Add(file.PixelData[p]);
        }
        x += run;
      }
    }

    var output = new byte[checked(_DataOffset + coded.Count)];
    PrismsFile.Signature.CopyTo(output);
    PrismsFile.Layout.CopyTo(output.AsSpan(PrismsFile.LayoutOffset));
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(PrismsFile.HeightOffset, 2), checked((ushort)file.Height));
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(PrismsFile.WidthOffset, 2), checked((ushort)file.Width));
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(PrismsFile.DataPointerOffset, 2), _DataOffset);
    coded.CopyTo(output, _DataOffset);
    return output;
  }
}

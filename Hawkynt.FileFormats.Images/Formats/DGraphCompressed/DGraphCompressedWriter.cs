using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.DGraphCompressed;

/// <summary>Assembles a D-GRAPH picture: a length, the palette, then the two packed screens.</summary>
/// <remarks>
/// Each block's length is written as decimal digits closed by a carriage return, so the packed
/// bytes have to exist before the header that describes them.
/// </remarks>
public static class DGraphCompressedWriter {

  public static byte[] ToBytes(DGraphCompressedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var screens = file.ScreenData ?? new byte[DGraphCompressedFile.ScreenSize * 2];
    var first = AtariStCaRle.Pack(screens.AsSpan(0, DGraphCompressedFile.ScreenSize));
    var second = AtariStCaRle.Pack(screens.AsSpan(DGraphCompressedFile.ScreenSize, DGraphCompressedFile.ScreenSize));

    using var output = new MemoryStream();
    _WriteLength(output, first.Length);

    var palette = file.Palette ?? new byte[DGraphCompressedFile.PaletteSize];
    output.Write(palette, 0, Math.Min(palette.Length, DGraphCompressedFile.PaletteSize));
    for (var i = palette.Length; i < DGraphCompressedFile.PaletteSize; ++i)
      output.WriteByte(0);

    output.Write(first);
    _WriteLength(output, second.Length);
    output.Write(second);

    return output.ToArray();
  }

  private static void _WriteLength(MemoryStream output, int length) {
    output.Write(Encoding.ASCII.GetBytes(length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    output.WriteByte((byte)'\r');
    output.WriteByte((byte)'\n');
  }

  public static void ToFile(DGraphCompressedFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}

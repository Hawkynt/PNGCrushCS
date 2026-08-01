using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.DaliCompressed;

namespace FileFormat.ZzRough;

/// <summary>Assembles a ZZ_ROUGH picture: the signature, the palette, then the two packed streams.</summary>
/// <remarks>
/// The length of the run-count stream is written as decimal digits rather than as a number, which
/// is why it is built before the header rather than after it.
/// </remarks>
public static class ZzRoughWriter {

  public static byte[] ToBytes(ZzRoughFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var screen = file.ScreenData ?? new byte[AtariStGraphics.BytesPerRow(ZzRoughFile.Width, ZzRoughFile.Planes) * ZzRoughFile.Height];
    var (counts, values) = DaliCompressor.Compress(screen);

    using var output = new MemoryStream();
    output.Write(Encoding.ASCII.GetBytes(ZzRoughFile.Signature));
    output.Write(Encoding.ASCII.GetBytes(counts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    output.WriteByte((byte)'\r');
    output.WriteByte((byte)'\n');

    var palette = file.Palette ?? new byte[ZzRoughFile.PaletteSize];
    output.Write(palette, 0, Math.Min(palette.Length, ZzRoughFile.PaletteSize));
    for (var i = palette.Length; i < ZzRoughFile.PaletteSize; ++i)
      output.WriteByte(0);

    output.Write(counts);
    output.Write(values);

    return output.ToArray();
  }

  public static void ToFile(ZzRoughFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}

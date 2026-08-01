using System;
using System.IO;
using System.Text;

namespace FileFormat.Uimg;

/// <summary>Assembles a UIMG picture in its twenty-four-bit arrangement.</summary>
/// <remarks>
/// The format holds bitplanes, bytes and three widths of true colour, and its header states which
/// by way of three fields that have to agree with the file's own length. Writing the twenty-four-bit
/// one keeps every colour the source had and needs no palette, so nothing has to be chosen.
/// </remarks>
public static class UimgWriter {

  /// <summary>The arrangement byte for three bytes a pixel, which is also the bytes per pixel.</summary>
  public const byte TrueColor24 = 3;

  public static byte[] ToBytes(UimgFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixels = file.Width * file.Height;
    var data = file.Data ?? [];
    var result = new byte[UimgFile.PaletteOffset + pixels * TrueColor24];

    Encoding.ASCII.GetBytes(UimgFile.Signature).CopyTo(result.AsSpan(0));
    result[6] = 0;
    result[7] = 0; // No palette: the colours are in the pixels.
    result[8] = TrueColor24 << 3; // Depth, which the arrangement fixes at twenty-four.
    result[9] = TrueColor24;
    result[10] = (byte)(file.Width >> 8);
    result[11] = (byte)file.Width;
    result[12] = (byte)(file.Height >> 8);
    result[13] = (byte)file.Height;

    var length = Math.Min(data.Length - UimgFile.PaletteOffset, pixels * TrueColor24);
    if (length > 0)
      data.AsSpan(UimgFile.PaletteOffset, length).CopyTo(result.AsSpan(UimgFile.PaletteOffset));

    return result;
  }

  public static void ToFile(UimgFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}

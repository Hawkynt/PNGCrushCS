using System;
using System.IO;
using System.Text;

namespace FileFormat.PixelPowerCollage;

/// <summary>Assembles a Pixel Power Collage picture, name and all.</summary>
/// <remarks>
/// The header is 128 bytes of which four things are read: the name, the depth code at 0x40, and the
/// two sizes at 0x4C and 0x50. Everything else was zero in every file that was tried and is written
/// as zero here, because a value nobody reads is a value nobody can check.
/// <para/>
/// The name is the whole of the format's identity, so a file whose first thirty-two bytes do not
/// match the name it is filed under is refused — by the reader here and by XnView alike, which is
/// how the rule was confirmed: the same bytes accepted under one name and turned away under another.
/// A name longer than thirty-one characters would leave no room for the terminator, so it is cut
/// there, and such a file can then only be opened under the cut name.
/// </remarks>
public static class PixelPowerCollageWriter {

  /// <summary>Where the code saying how wide a pixel is stands.</summary>
  private const int _TYPE_AT = 0x40;

  /// <summary>Where the size stands.</summary>
  private const int _WIDTH_AT = 0x4C;

  private const int _HEIGHT_AT = 0x50;

  public static byte[] ToBytes(PixelPowerCollageFile file) {
    var stride = file.Stride;
    var raster = (long)stride * file.Height;
    var result = new byte[PixelPowerCollageFile.PixelOffset + raster];

    _WriteName(result, file.Name);
    _WriteBigEndian(result, _TYPE_AT, file.BitsPerPixel switch {
      32 => 0,
      24 => 1,
      8 => 2,
      var other => throw new InvalidDataException($"A Collage pixel is 8, 24 or 32 bits wide, not {other}."),
    });
    _WriteBigEndian(result, _WIDTH_AT, file.Width);
    _WriteBigEndian(result, _HEIGHT_AT, file.Height);

    var pixels = file.PixelData ?? [];
    pixels.AsSpan(0, (int)Math.Min(pixels.Length, raster)).CopyTo(result.AsSpan(PixelPowerCollageFile.PixelOffset));

    return result;
  }

  /// <summary>Writes the name the file must be filed under, ending it with a zero.</summary>
  private static void _WriteName(byte[] target, string? name) {
    var written = Encoding.ASCII.GetBytes(name ?? string.Empty);
    var length = Math.Min(written.Length, PixelPowerCollageFile.NameSize - 1);
    written.AsSpan(0, length).CopyTo(target);
  }

  private static void _WriteBigEndian(byte[] target, int at, int value) {
    target[at] = (byte)(value >> 24);
    target[at + 1] = (byte)(value >> 16);
    target[at + 2] = (byte)(value >> 8);
    target[at + 3] = (byte)value;
  }
}

using System;
using System.IO;
using System.Text;

namespace FileFormat.AmosBank;

/// <summary>Assembles an AMOS memory bank from an <see cref="AmosBankFile"/>.</summary>
public static class AmosBankWriter {

  /// <summary>Bytes a sprite's own header occupies before its bitplanes.</summary>
  private const int _SPRITE_HEADER = 10;

  /// <summary>
  /// Writes a bank of one sprite, which is the kind whose bytes are the picture rather than a
  /// compression of it.
  /// </summary>
  /// <remarks>
  /// A bank read as a packed screen or as several sprites comes back out as one sprite holding the
  /// same pixels. The alternative would be reproducing the layout it arrived in, which for the
  /// packed screen means re-running a three-stream packer to say what a plain bank already says.
  /// </remarks>
  public static byte[] ToBytes(AmosBankFile file) {
    var pixels = file.PixelData ?? [];
    var palette = file.Palette ?? [];
    var width = Math.Max(AmosBankFile.WidthStep, file.Width);
    var height = Math.Max(1, file.Height);
    var stride = width >> 3;
    var planeLength = stride * height;

    using var output = new MemoryStream();
    output.Write(Encoding.ASCII.GetBytes(AmosBankFile.Signature));
    output.Write(Encoding.ASCII.GetBytes("Sp"));
    output.WriteByte(0);
    output.WriteByte(1);

    // The stored width counts sixteen-pixel words; the four bytes past the plane count are the
    // sprite's hot spot, which a picture has no use for and the editor centres by default.
    var words = width / AmosBankFile.WidthStep;
    output.WriteByte((byte)(words >> 8));
    output.WriteByte((byte)words);
    output.WriteByte((byte)(height >> 8));
    output.WriteByte((byte)height);
    output.WriteByte(0);
    output.WriteByte(AmosBankFile.Planes);
    for (var i = 0; i < _SPRITE_HEADER - 6; ++i)
      output.WriteByte(0);

    var planes = new byte[AmosBankFile.Planes * planeLength];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = y * file.Width + x;
      var index = at < pixels.Length && x < file.Width ? pixels[at] : 0;

      for (var plane = 0; plane < AmosBankFile.Planes; ++plane)
        if ((index & (1 << plane)) != 0)
          planes[plane * planeLength + y * stride + (x >> 3)] |= (byte)(1 << (~x & 7));
    }

    output.Write(planes, 0, planes.Length);

    // The palette closes the bank, and where it sits is what confirms the sprites were walked right.
    for (var i = 0; i < AmosBankFile.ColorCount; ++i) {
      var entry = i * 3;
      output.WriteByte(_Nibble(palette, entry));
      output.WriteByte((byte)((_Nibble(palette, entry + 1) << 4) | _Nibble(palette, entry + 2)));
    }

    return output.ToArray();
  }

  /// <summary>A channel back down to the four bits an OCS palette stores it in.</summary>
  private static byte _Nibble(ReadOnlySpan<byte> palette, int index)
    => (byte)(index < palette.Length ? (palette[index] * 15 + 127) / 255 : 0);
}

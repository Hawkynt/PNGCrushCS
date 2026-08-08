using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.ColorStarObject;

/// <summary>Assembles a ColorSTar object from a <see cref="ColorStarObjectFile"/>.</summary>
public static class ColorStarObjectWriter {

  /// <summary>
  /// Writes whichever of the two shapes the object is, since a monochrome one is not a coloured one
  /// with a shorter palette but a different file.
  /// </summary>
  public static byte[] ToBytes(ColorStarObjectFile file) {
    var data = file.Data ?? [];
    var palette = file.Palette ?? [];
    var stride = (file.Width + 15) >> 4 << (file.Bitplanes > 1 ? 3 : 1);
    var bitmap = new byte[stride * file.Height];
    var available = Math.Max(0, Math.Min(bitmap.Length, data.Length - file.BitmapOffset));
    if (available > 0)
      data.AsSpan(file.BitmapOffset, available).CopyTo(bitmap);

    using var output = new MemoryStream();

    if (file.Bitplanes > 1) {
      // Each entry is a number on a line of its own, and its three digits are the three channels.
      for (var i = 0; i < ColorStarObjectFile.ColorCount; ++i) {
        var value = (_Reduce(palette, i * 3) << 8) | (_Reduce(palette, i * 3 + 1) << 4) | _Reduce(palette, i * 3 + 2);
        output.Write(Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture)));
        output.WriteByte((byte)'\r');
        output.WriteByte((byte)'\n');
      }
    }

    // Both shapes store their dimensions one less than they are, so a one-pixel object is not empty.
    output.WriteByte((byte)((file.Width - 1) >> 8));
    output.WriteByte((byte)(file.Width - 1));

    if (file.Bitplanes > 1) {
      output.WriteByte(0);
      output.WriteByte((byte)(file.Height - 1));
      output.WriteByte(0);
      output.WriteByte(4);
    } else {
      output.WriteByte((byte)((file.Height - 1) >> 8));
      output.WriteByte((byte)(file.Height - 1));
      output.WriteByte(0);
      output.WriteByte(1);
    }

    output.Write(bitmap, 0, bitmap.Length);

    return output.ToArray();
  }

  /// <summary>A channel back down to the three bits the file states it in.</summary>
  private static int _Reduce(ReadOnlySpan<byte> palette, int index)
    => index < palette.Length ? (palette[index] * 7 + 127) / 255 : 0;
}

using System;
using System.Buffers.Binary;

namespace FileFormat.SpeccyExtended;

/// <summary>Assembles Speccy eXtended Graphics (SXG) picture bytes.</summary>
/// <remarks>
/// What lies between the palette and the picture is not established, so it is written as nought.
/// The reference tool does not read it, and neither does this.
/// </remarks>
public static class SpeccyExtendedWriter {

  public static byte[] ToBytes(SpeccyExtendedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[SpeccyExtendedFile.PixelOffset + (file.Width * file.Height + 1) / 2];
    SpeccyExtendedReader.Magic.CopyTo(result);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 3);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(SpeccyExtendedFile.WidthOffset), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(SpeccyExtendedFile.WidthOffset + 2), (ushort)file.Height);

    var palette = file.Palette ?? [];
    for (var i = 0; i < SpeccyExtendedFile.PaletteCount && i * 3 + 2 < palette.Length; ++i) {
      var value = (ushort)((_Channel(palette[i * 3]) << 10) | (_Channel(palette[i * 3 + 1]) << 5) | _Channel(palette[i * 3 + 2]));
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(SpeccyExtendedFile.PaletteOffset + i * 2), value);
    }

    var pixels = file.PixelData ?? [];
    for (var i = 0; i < pixels.Length && i < file.Width * file.Height; ++i) {
      var at = SpeccyExtendedFile.PixelOffset + i / 2;
      result[at] |= (byte)(i % 2 == 0 ? (pixels[i] & 0x0F) << 4 : pixels[i] & 0x0F);
    }

    return result;
  }

  /// <summary>A channel of 0..255 as the five bits the file holds.</summary>
  /// <remarks>
  /// Rounded rather than truncated. Full scale is 24 of the 31 five bits can express, so truncating
  /// loses a step where the reading side gains one — a colour written and read back would drift a
  /// shade darker each time.
  /// </remarks>
  private static int _Channel(byte value)
    => Math.Min(31, (value * SpeccyExtendedFile.ChannelFullScale + 127) / 255);
}

using System;
using System.Buffers.Binary;

namespace FileFormat.QdvImage;

/// <summary>Assembles a QDV picture: the size, the palette, then a byte a pixel.</summary>
public static class QdvImageWriter {

  public static byte[] ToBytes(QdvImageFile file) {
    var pixels = file.PixelData ?? [];
    var result = new byte[QdvImageFile.PixelOffset + file.Width * file.Height];

    BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2), (ushort)file.Height);
    result[4] = file.HighestIndex;

    (file.Palette ?? []).AsSpan(0, Math.Min((file.Palette ?? []).Length, QdvImageFile.PaletteSize))
      .CopyTo(result.AsSpan(QdvImageFile.HeaderSize));
    pixels.AsSpan(0, Math.Min(pixels.Length, file.Width * file.Height))
      .CopyTo(result.AsSpan(QdvImageFile.PixelOffset));

    return result;
  }
}

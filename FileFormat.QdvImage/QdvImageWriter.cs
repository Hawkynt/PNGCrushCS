using System;

namespace FileFormat.QdvImage;

/// <summary>Assembles QDV image file bytes from a QdvImageFile.</summary>
public static class QdvImageWriter {

  public static byte[] ToBytes(QdvImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[QdvImageFile.HeaderSize + pixelDataSize];

    result[0] = QdvImageFile.Magic[0];
    result[1] = QdvImageFile.Magic[1];
    result[2] = QdvImageFile.Magic[2];
    result[3] = QdvImageFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Bpp);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Flags);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(QdvImageFile.HeaderSize));

    return result;
  }
}

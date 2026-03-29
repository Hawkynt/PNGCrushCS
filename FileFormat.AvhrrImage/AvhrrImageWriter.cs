using System;

namespace FileFormat.AvhrrImage;

/// <summary>Assembles AVHRR satellite image file bytes from an AvhrrImageFile.</summary>
public static class AvhrrImageWriter {

  public static byte[] ToBytes(AvhrrImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[AvhrrImageFile.HeaderSize + pixelDataSize];

    result[0] = AvhrrImageFile.Magic[0];
    result[1] = AvhrrImageFile.Magic[1];
    result[2] = AvhrrImageFile.Magic[2];
    result[3] = AvhrrImageFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Bands);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.DataType);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(AvhrrImageFile.HeaderSize));

    return result;
  }
}

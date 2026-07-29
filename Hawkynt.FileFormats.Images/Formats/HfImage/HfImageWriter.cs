using System;

namespace FileFormat.HfImage;

/// <summary>Assembles HF height field image file bytes from an HfImageFile.</summary>
public static class HfImageWriter {

  public static byte[] ToBytes(HfImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[HfImageFile.HeaderSize + pixelDataSize];

    result[0] = HfImageFile.Magic[0];
    result[1] = HfImageFile.Magic[1];
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), file.DataType);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(HfImageFile.HeaderSize));

    return result;
  }
}

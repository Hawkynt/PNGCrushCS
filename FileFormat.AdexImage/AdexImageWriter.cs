using System;

namespace FileFormat.AdexImage;

/// <summary>Assembles ADEX image file bytes from an AdexImageFile.</summary>
public static class AdexImageWriter {

  public static byte[] ToBytes(AdexImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[AdexImageFile.HeaderSize + pixelDataSize];

    result[0] = AdexImageFile.Magic[0];
    result[1] = AdexImageFile.Magic[1];
    result[2] = AdexImageFile.Magic[2];
    result[3] = AdexImageFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Bpp);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Compression);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(AdexImageFile.HeaderSize));

    return result;
  }
}

using System;

namespace FileFormat.AimGrayScale;

/// <summary>Assembles AIM grayscale image file bytes from an AimGrayScaleFile.</summary>
public static class AimGrayScaleWriter {

  public static byte[] ToBytes(AimGrayScaleFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[AimGrayScaleFile.HeaderSize + pixelDataSize];

    result[0] = AimGrayScaleFile.Magic[0];
    result[1] = AimGrayScaleFile.Magic[1];
    result[2] = AimGrayScaleFile.Magic[2];
    result[3] = AimGrayScaleFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(AimGrayScaleFile.HeaderSize));

    return result;
  }
}

using System;

namespace FileFormat.YJKImage;

/// <summary>Assembles YJK image file bytes from a <see cref="YJKImageFile"/>.</summary>
public static class YJKImageWriter {

  public static byte[] ToBytes(YJKImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[YJKImageFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, YJKImageFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}

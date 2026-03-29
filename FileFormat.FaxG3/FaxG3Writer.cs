using System;

namespace FileFormat.FaxG3;

/// <summary>Assembles Raw Group 3 fax image file bytes.</summary>
public static class FaxG3Writer {

  public static byte[] ToBytes(FaxG3File file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixelBytes = file.PixelData.Length;
    var fileSize = FaxG3File.HeaderSize + pixelBytes;
    var result = new byte[fileSize];



    file.PixelData.AsSpan(0, Math.Min(pixelBytes, file.PixelData.Length)).CopyTo(result.AsSpan(FaxG3File.HeaderSize));
    return result;
  }
}

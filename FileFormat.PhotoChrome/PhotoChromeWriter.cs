using System;

namespace FileFormat.PhotoChrome;

/// <summary>Assembles PhotoChrome screen dump bytes from pixel data.</summary>
public static class PhotoChromeWriter {

  /// <summary>The exact file size of a valid PhotoChrome screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = PhotoChromeFile.ExpectedFileSize;

  public static byte[] ToBytes(PhotoChromeFile file) => Assemble(file.PixelData);

  internal static byte[] Assemble(byte[] pixelData) {
    var result = new byte[_EXPECTED_SIZE];
    pixelData.AsSpan(0, Math.Min(pixelData.Length, _EXPECTED_SIZE)).CopyTo(result);
    return result;
  }
}

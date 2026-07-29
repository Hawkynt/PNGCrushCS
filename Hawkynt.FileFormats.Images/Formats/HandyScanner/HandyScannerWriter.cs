using System;

namespace FileFormat.HandyScanner;

/// <summary>Assembles Handy Scanner scan bytes.</summary>
public static class HandyScannerWriter {

  public static byte[] ToBytes(HandyScannerFile file) {
    var data = file.BitmapData ?? [];
    var result = new byte[data.Length];
    data.CopyTo(result.AsSpan());

    return result;
  }
}

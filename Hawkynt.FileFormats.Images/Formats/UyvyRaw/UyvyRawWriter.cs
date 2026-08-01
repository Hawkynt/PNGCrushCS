using System;

namespace FileFormat.UyvyRaw;

/// <summary>Assembles raw UYVY 4:2:2 bytes.</summary>
public static class UyvyRawWriter {

  public static byte[] ToBytes(UyvyRawFile file) {
    var expected = file.Width * file.Height * UyvyRawFile.BytesPerPixel;
    var result = new byte[expected];

    var data = file.PixelData ?? [];
    data.AsSpan(0, Math.Min(data.Length, expected)).CopyTo(result);
    return result;
  }
}

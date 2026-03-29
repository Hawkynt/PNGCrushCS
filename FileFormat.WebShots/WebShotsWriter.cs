using System;

namespace FileFormat.WebShots;

/// <summary>Assembles WebShots image file bytes from a WebShotsFile.</summary>
public static class WebShotsWriter {

  public static byte[] ToBytes(WebShotsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[WebShotsFile.HeaderSize + pixelDataSize];

    result[0] = WebShotsFile.Magic[0];
    result[1] = WebShotsFile.Magic[1];
    result[2] = WebShotsFile.Magic[2];
    result[3] = WebShotsFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Bpp);
    // bytes 12-15 are reserved (zero)

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(WebShotsFile.HeaderSize));

    return result;
  }
}

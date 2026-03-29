using System;

namespace FileFormat.RedStormRsb;

/// <summary>Assembles Red Storm RSB file bytes from a RedStormRsbFile.</summary>
public static class RedStormRsbWriter {

  public static byte[] ToBytes(RedStormRsbFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[RedStormRsbFile.HeaderSize + pixelDataSize];

    result[0] = RedStormRsbFile.Magic[0];
    result[1] = RedStormRsbFile.Magic[1];
    result[2] = RedStormRsbFile.Magic[2];
    result[3] = RedStormRsbFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Bpp);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(RedStormRsbFile.HeaderSize));

    return result;
  }
}

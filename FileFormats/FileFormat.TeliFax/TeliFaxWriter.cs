using System;

namespace FileFormat.TeliFax;

/// <summary>Assembles TeliFax MH file bytes from a TeliFaxFile.</summary>
public static class TeliFaxWriter {

  public static byte[] ToBytes(TeliFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[TeliFaxFile.HeaderSize + pixelDataSize];

    result[0] = TeliFaxFile.Magic[0];
    result[1] = TeliFaxFile.Magic[1];
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(TeliFaxFile.HeaderSize));

    return result;
  }
}

using System;

namespace FileFormat.SciFax;

/// <summary>Assembles SciFax SCF file bytes from a SciFaxFile.</summary>
public static class SciFaxWriter {

  public static byte[] ToBytes(SciFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[SciFaxFile.HeaderSize + pixelDataSize];

    result[0] = SciFaxFile.Magic[0];
    result[1] = SciFaxFile.Magic[1];
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(SciFaxFile.HeaderSize));

    return result;
  }
}

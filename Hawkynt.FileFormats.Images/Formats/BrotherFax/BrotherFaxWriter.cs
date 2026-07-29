using System;

namespace FileFormat.BrotherFax;

/// <summary>Assembles Brother fax UNI file bytes from a BrotherFaxFile.</summary>
public static class BrotherFaxWriter {

  public static byte[] ToBytes(BrotherFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[BrotherFaxFile.HeaderSize + pixelDataSize];

    result[0] = BrotherFaxFile.Magic[0];
    result[1] = BrotherFaxFile.Magic[1];
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Compression);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(BrotherFaxFile.HeaderSize));

    return result;
  }
}

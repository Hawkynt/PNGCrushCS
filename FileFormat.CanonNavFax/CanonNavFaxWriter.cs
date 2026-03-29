using System;

namespace FileFormat.CanonNavFax;

/// <summary>Assembles Canon Navigator Fax CAN file bytes from a CanonNavFaxFile.</summary>
public static class CanonNavFaxWriter {

  public static byte[] ToBytes(CanonNavFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[CanonNavFaxFile.HeaderSize + pixelDataSize];

    result[0] = CanonNavFaxFile.Magic[0];
    result[1] = CanonNavFaxFile.Magic[1];
    result[2] = CanonNavFaxFile.Magic[2];
    result[3] = CanonNavFaxFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Resolution);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Encoding);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(CanonNavFaxFile.HeaderSize));

    return result;
  }
}

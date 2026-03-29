using System;

namespace FileFormat.RicohFax;

/// <summary>Assembles RicohFax RIC file bytes from a RicohFaxFile.</summary>
public static class RicohFaxWriter {

  public static byte[] ToBytes(RicohFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[RicohFaxFile.HeaderSize + pixelDataSize];

    result[0] = RicohFaxFile.Magic[0];
    result[1] = RicohFaxFile.Magic[1];
    result[2] = RicohFaxFile.Magic[2];
    result[3] = RicohFaxFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Resolution);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Compression);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(RicohFaxFile.HeaderSize));

    return result;
  }
}

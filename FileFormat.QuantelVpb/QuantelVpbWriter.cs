using System;

namespace FileFormat.QuantelVpb;

/// <summary>Assembles Quantel VPB file bytes from a QuantelVpbFile.</summary>
public static class QuantelVpbWriter {

  public static byte[] ToBytes(QuantelVpbFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[QuantelVpbFile.HeaderSize + pixelDataSize];

    result[0] = QuantelVpbFile.Magic[0];
    result[1] = QuantelVpbFile.Magic[1];
    result[2] = QuantelVpbFile.Magic[2];
    result[3] = QuantelVpbFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Bpp);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Fields);
    BitConverter.TryWriteBytes(new Span<byte>(result, 12, 4), file.Reserved);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(QuantelVpbFile.HeaderSize));

    return result;
  }
}

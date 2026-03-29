using System;

namespace FileFormat.NistIHead;

/// <summary>Assembles NIST IHead file bytes from a NistIHeadFile.</summary>
public static class NistIHeadWriter {

  public static byte[] ToBytes(NistIHeadFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[NistIHeadFile.HeaderSize + pixelDataSize];

    result[0] = NistIHeadFile.Magic[0];
    result[1] = NistIHeadFile.Magic[1];
    result[2] = NistIHeadFile.Magic[2];
    result[3] = NistIHeadFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Bpp);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Compression);
    BitConverter.TryWriteBytes(new Span<byte>(result, 12, 4), file.Reserved);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(NistIHeadFile.HeaderSize));

    return result;
  }
}

using System;

namespace FileFormat.BfxBitware;

/// <summary>Assembles Bitware BFX fax file bytes from a BfxBitwareFile.</summary>
public static class BfxBitwareWriter {

  public static byte[] ToBytes(BfxBitwareFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[BfxBitwareFile.HeaderSize + pixelDataSize];

    result[0] = BfxBitwareFile.Magic[0];
    result[1] = BfxBitwareFile.Magic[1];
    result[2] = BfxBitwareFile.Magic[2];
    result[3] = BfxBitwareFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Compression);
    // bytes 12-15 are reserved (remain zero)

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(BfxBitwareFile.HeaderSize));

    return result;
  }
}

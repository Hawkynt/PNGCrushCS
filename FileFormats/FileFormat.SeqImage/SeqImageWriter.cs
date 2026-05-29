using System;

namespace FileFormat.SeqImage;

/// <summary>Assembles SEQ image file bytes from a SeqImageFile.</summary>
public static class SeqImageWriter {

  public static byte[] ToBytes(SeqImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[SeqImageFile.HeaderSize + pixelDataSize];

    result[0] = SeqImageFile.Magic[0];
    result[1] = SeqImageFile.Magic[1];
    result[2] = SeqImageFile.Magic[2];
    result[3] = SeqImageFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.FrameCount);
    BitConverter.TryWriteBytes(new Span<byte>(result, 12, 2), file.Bpp);
    // bytes 14-15 are reserved (zero)

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(SeqImageFile.HeaderSize));

    return result;
  }
}

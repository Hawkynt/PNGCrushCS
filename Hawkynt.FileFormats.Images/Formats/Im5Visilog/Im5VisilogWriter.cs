using System;

namespace FileFormat.Im5Visilog;

/// <summary>Assembles IM5 Visilog grayscale file bytes from an Im5VisilogFile.</summary>
public static class Im5VisilogWriter {

  public static byte[] ToBytes(Im5VisilogFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[Im5VisilogFile.HeaderSize + pixelDataSize];

    BitConverter.TryWriteBytes(new Span<byte>(result, 0, 4), file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 4), file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 4), file.Depth);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(Im5VisilogFile.HeaderSize));

    return result;
  }
}

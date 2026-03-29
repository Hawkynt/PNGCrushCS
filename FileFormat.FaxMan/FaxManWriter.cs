using System;

namespace FileFormat.FaxMan;

/// <summary>Assembles FaxMan FMF file bytes from a FaxManFile.</summary>
public static class FaxManWriter {

  public static byte[] ToBytes(FaxManFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[FaxManFile.HeaderSize + pixelDataSize];

    result[0] = FaxManFile.Magic[0];
    result[1] = FaxManFile.Magic[1];
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Flags);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(FaxManFile.HeaderSize));

    return result;
  }
}

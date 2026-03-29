using System;

namespace FileFormat.Pco16Bit;

/// <summary>Assembles PCO 16-bit grayscale file bytes from a Pco16BitFile.</summary>
public static class Pco16BitWriter {

  public static byte[] ToBytes(Pco16BitFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[Pco16BitFile.HeaderSize + pixelDataSize];

    BitConverter.TryWriteBytes(new Span<byte>(result, 0, 4), file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 4), file.Height);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(Pco16BitFile.HeaderSize));

    return result;
  }
}

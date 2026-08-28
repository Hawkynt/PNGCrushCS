using System;
using System.Buffers.Binary;
using FileFormat.Ccitt;

namespace FileFormat.Skantek;

/// <summary>Writes Skantek bilevel pages using the verified reversed-fill-order Group 4 layout.</summary>
public static class SkantekWriter {

  public static byte[] ToBytes(SkantekFile file) {
    if (file.Width is < 1 or > 65535 || file.Height is < 1 or > 65535)
      throw new ArgumentException($"Skantek dimensions must be between 1 and 65535; got {file.Width}x{file.Height}.", nameof(file));

    var bytesPerRow = checked((file.Width + 7) / 8);
    var expected = checked(bytesPerRow * file.Height);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"Skantek needs {expected} packed 1bpp bytes.", nameof(file));

    var coded = CcittG4Encoder.Encode(file.PixelData, file.Width, file.Height);
    var reversed = CcittFillOrder.Reverse(coded);
    var output = new byte[checked(SkantekFile.HeaderSize + reversed.Length)];

    SkantekFile.Signature.CopyTo(output);
    SkantekFile.Stamp.CopyTo(output.AsSpan(SkantekFile.StampOffset));
    BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(SkantekFile.HeightOffset, 4), file.Height);
    BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(SkantekFile.WidthOffset, 4), file.Width);
    reversed.CopyTo(output, SkantekFile.HeaderSize);
    return output;
  }
}

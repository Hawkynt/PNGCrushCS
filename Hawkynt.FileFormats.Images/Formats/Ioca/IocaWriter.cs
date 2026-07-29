using System;
using System.Collections.Generic;

namespace FileFormat.Ioca;

/// <summary>Assembles minimal IOCA container bytes from an <see cref="IocaFile"/>.</summary>
public static class IocaWriter {

  public static byte[] ToBytes(IocaFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var output = new List<byte>();

    // Write dimensions as 4-byte big-endian header (width, height)
    output.Add((byte)(file.Width >> 8));
    output.Add((byte)(file.Width & 0xFF));
    output.Add((byte)(file.Height >> 8));
    output.Add((byte)(file.Height & 0xFF));

    // Write pixel data
    var bytesPerRow = (file.Width + 7) / 8;
    var expectedSize = bytesPerRow * file.Height;
    var pixelData = new byte[expectedSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, expectedSize)).CopyTo(pixelData);
    output.AddRange(pixelData);

    return output.ToArray();
  }
}

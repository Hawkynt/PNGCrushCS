using System;
using System.Buffers.Binary;

namespace FileFormat.Avs;

/// <summary>Assembles AVS file bytes from pixel data.</summary>
public static class AvsWriter {

  private const int _HEADER_SIZE = 8;

  public static byte[] ToBytes(AvsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var expectedPixelBytes = width * height * 4;
    var result = new byte[_HEADER_SIZE + expectedPixelBytes];
    var span = result.AsSpan();

    BinaryPrimitives.WriteUInt32BigEndian(span, (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)height);

    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(_HEADER_SIZE));

    return result;
  }
}

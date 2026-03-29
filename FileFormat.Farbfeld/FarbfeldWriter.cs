using System;

namespace FileFormat.Farbfeld;

/// <summary>Assembles Farbfeld file bytes from pixel data.</summary>
public static class FarbfeldWriter {

  public static byte[] ToBytes(FarbfeldFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var pixelDataLength = width * height * 8;
    var fileSize = FarbfeldHeader.StructSize + pixelDataLength;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new FarbfeldHeader(
      (byte)'f', (byte)'a', (byte)'r', (byte)'b',
      (byte)'f', (byte)'e', (byte)'l', (byte)'d',
      width,
      height
    );
    header.WriteTo(span);

    pixelData.AsSpan(0, Math.Min(pixelDataLength, pixelData.Length)).CopyTo(result.AsSpan(FarbfeldHeader.StructSize));

    return result;
  }
}

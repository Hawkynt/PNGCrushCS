using System;
using System.Buffers.Text;

namespace FileFormat.Mtv;

/// <summary>Assembles canonical MTV/PRT file bytes from RGB pixel data.</summary>
public static class MtvWriter {

  public static byte[] ToBytes(MtvFile file) {
    MtvFile.Validate(file, nameof(file));
    return _Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var file = new MtvFile { Width = width, Height = height, PixelData = pixelData };
    MtvFile.Validate(file, nameof(pixelData));
    return _Assemble(pixelData, width, height);
  }

  private static byte[] _Assemble(byte[] pixelData, int width, int height) {
    Span<byte> header = stackalloc byte[32];
    if (!Utf8Formatter.TryFormat(width, header, out var widthLength))
      throw new InvalidOperationException("Could not format MTV width.");

    header[widthLength] = (byte)' ';
    if (!Utf8Formatter.TryFormat(height, header[(widthLength + 1)..], out var heightLength))
      throw new InvalidOperationException("Could not format MTV height.");

    var headerLength = widthLength + 1 + heightLength;
    header[headerLength++] = (byte)'\n';

    var result = new byte[checked(headerLength + pixelData.Length)];
    header[..headerLength].CopyTo(result);
    pixelData.CopyTo(result.AsSpan(headerLength));
    return result;
  }
}

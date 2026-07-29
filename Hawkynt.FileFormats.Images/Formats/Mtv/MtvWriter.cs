using System;
using System.IO;
using System.Text;

namespace FileFormat.Mtv;

/// <summary>Assembles MTV Ray Tracer file bytes from pixel data.</summary>
public static class MtvWriter {

  public static byte[] ToBytes(MtvFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var header = Encoding.ASCII.GetBytes($"{width} {height}\n");
    var expectedPixelBytes = width * height * 3;

    var result = new byte[header.Length + expectedPixelBytes];
    header.AsSpan(0, header.Length).CopyTo(result.AsSpan(0));

    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(header.Length));

    return result;
  }
}

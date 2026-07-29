using System;
using System.Text;

namespace FileFormat.XvThumbnail;

/// <summary>Assembles XV thumbnail file bytes from pixel data.</summary>
public static class XvThumbnailWriter {

  public static byte[] ToBytes(XvThumbnailFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var magic = "P7 332\n"u8;
    var dimLine = Encoding.ASCII.GetBytes($"{width} {height} 255\n");
    var expectedPixelBytes = width * height;

    var result = new byte[magic.Length + dimLine.Length + expectedPixelBytes];
    magic.CopyTo(result);
    dimLine.AsSpan(0, dimLine.Length).CopyTo(result.AsSpan(magic.Length));

    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(magic.Length + dimLine.Length));

    return result;
  }
}

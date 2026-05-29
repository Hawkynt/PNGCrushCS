using System;
using System.IO;
using System.Text;

namespace FileFormat.MgrBitmap;

/// <summary>Assembles MGR bitmap file bytes from a <see cref="MgrBitmapFile"/>.</summary>
public static class MgrBitmapWriter {

  public static byte[] ToBytes(MgrBitmapFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var header = Encoding.ASCII.GetBytes($"{file.Width}x{file.Height}\n");
    var result = new byte[header.Length + file.PixelData.Length];
    header.AsSpan(0, header.Length).CopyTo(result.AsSpan(0));
    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(header.Length));
    return result;
  }
}

using System;

namespace FileFormat.DbwRender;

/// <summary>Assembles DBW Render file bytes from a <see cref="DbwRenderFile"/>.</summary>
public static class DbwRenderWriter {

  public static byte[] ToBytes(DbwRenderFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[DbwRenderFile.HeaderSize + file.PixelData.Length];
    result[0] = (byte)(file.Width & 0xFF);
    result[1] = (byte)((file.Width >> 8) & 0xFF);
    result[2] = (byte)(file.Height & 0xFF);
    result[3] = (byte)((file.Height >> 8) & 0xFF);
    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(DbwRenderFile.HeaderSize));
    return result;
  }
}

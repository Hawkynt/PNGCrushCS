using System;

namespace FileFormat.PmBitmap;

/// <summary>Assembles PM bitmap file bytes from a <see cref="PmBitmapFile"/>.</summary>
public static class PmBitmapWriter {

  public static byte[] ToBytes(PmBitmapFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[PmBitmapFile.HeaderSize + file.PixelData.Length];
    result[0] = (byte)'P';
    result[1] = (byte)'M';
    result[2] = 0;
    result[3] = file.Version;
    result[4] = (byte)(file.Width & 0xFF);
    result[5] = (byte)((file.Width >> 8) & 0xFF);
    result[6] = (byte)(file.Height & 0xFF);
    result[7] = (byte)((file.Height >> 8) & 0xFF);
    result[8] = (byte)(file.Depth & 0xFF);
    result[9] = (byte)((file.Depth >> 8) & 0xFF);
    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(PmBitmapFile.HeaderSize));
    return result;
  }
}

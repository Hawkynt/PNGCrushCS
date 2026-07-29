using System;

namespace FileFormat.MonoStar;

/// <summary>Assembles Atari ST MonoSTar object bytes.</summary>
public static class MonoStarWriter {

  public static byte[] ToBytes(MonoStarFile file) {
    var result = new byte[MonoStarFile.FileSizeFor(file.Width, file.Height)];

    // Both dimensions go in one less than they are.
    result[0] = (byte)((file.Width - 1) >> 8);
    result[1] = (byte)((file.Width - 1) & 0xFF);
    result[2] = (byte)((file.Height - 1) >> 8);
    result[3] = (byte)((file.Height - 1) & 0xFF);
    MonoStarFile.MonochromeMarker.CopyTo(result.AsSpan(4));

    var data = file.BitmapData ?? [];
    var length = result.Length - MonoStarFile.HeaderSize;
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(result.AsSpan(MonoStarFile.HeaderSize));

    return result;
  }
}

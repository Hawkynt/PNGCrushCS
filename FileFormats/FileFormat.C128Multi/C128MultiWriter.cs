using System;

namespace FileFormat.C128Multi;

/// <summary>Assembles C128 multicolor image bytes from a <see cref="C128MultiFile"/>.</summary>
public static class C128MultiWriter {

  public static byte[] ToBytes(C128MultiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[C128MultiFile.ExpectedFileSize];
    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, C128MultiFile.BitmapDataSize)).CopyTo(result.AsSpan(0));
    file.ScreenData.AsSpan(0, Math.Min(file.ScreenData.Length, C128MultiFile.ScreenDataSize)).CopyTo(result.AsSpan(C128MultiFile.BitmapDataSize));
    file.ColorData.AsSpan(0, Math.Min(file.ColorData.Length, C128MultiFile.ColorDataSize)).CopyTo(result.AsSpan(C128MultiFile.BitmapDataSize + C128MultiFile.ScreenDataSize));
    result[C128MultiFile.BitmapDataSize + C128MultiFile.ScreenDataSize + C128MultiFile.ColorDataSize] = file.BackgroundColor;

    return result;
  }
}

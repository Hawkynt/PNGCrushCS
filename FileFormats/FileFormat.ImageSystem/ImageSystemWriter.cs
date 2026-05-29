using System;

namespace FileFormat.ImageSystem;

/// <summary>Assembles C64 Image System bytes from an <see cref="ImageSystemFile"/>.</summary>
public static class ImageSystemWriter {

  public static byte[] ToBytes(ImageSystemFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.IsHires ? _WriteHires(file) : _WriteMulticolor(file);
  }

  private static byte[] _WriteHires(ImageSystemFile file) {
    var result = new byte[ImageSystemFile.HiresFileSize];
    var pos = 0;

    // Load address (LE)
    result[pos++] = (byte)(file.LoadAddress & 0xFF);
    result[pos++] = (byte)(file.LoadAddress >> 8);

    // Bitmap data
    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, ImageSystemFile.BitmapDataSize)).CopyTo(result.AsSpan(pos));
    pos += ImageSystemFile.BitmapDataSize;

    // Screen data
    file.ScreenData.AsSpan(0, Math.Min(file.ScreenData.Length, ImageSystemFile.ScreenDataSize)).CopyTo(result.AsSpan(pos));
    pos += ImageSystemFile.ScreenDataSize;

    // Background/border color
    result[pos] = file.BackgroundColor;

    return result;
  }

  private static byte[] _WriteMulticolor(ImageSystemFile file) {
    var result = new byte[ImageSystemFile.MulticolorFileSize];
    var pos = 0;

    // Load address (LE)
    result[pos++] = (byte)(file.LoadAddress & 0xFF);
    result[pos++] = (byte)(file.LoadAddress >> 8);

    // Bitmap data
    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, ImageSystemFile.BitmapDataSize)).CopyTo(result.AsSpan(pos));
    pos += ImageSystemFile.BitmapDataSize;

    // Screen data
    file.ScreenData.AsSpan(0, Math.Min(file.ScreenData.Length, ImageSystemFile.ScreenDataSize)).CopyTo(result.AsSpan(pos));
    pos += ImageSystemFile.ScreenDataSize;

    // Color data
    if (file.ColorData != null)
      file.ColorData.AsSpan(0, Math.Min(file.ColorData.Length, ImageSystemFile.ColorDataSize)).CopyTo(result.AsSpan(pos));
    pos += ImageSystemFile.ColorDataSize;

    // Background color
    result[pos] = file.BackgroundColor;

    return result;
  }
}

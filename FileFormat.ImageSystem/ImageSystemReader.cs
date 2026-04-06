using System;
using System.IO;

namespace FileFormat.ImageSystem;

/// <summary>Reads C64 Image System images from bytes, streams, or file paths.</summary>
public static class ImageSystemReader {

  public static ImageSystemFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Image System file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ImageSystemFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static ImageSystemFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static ImageSystemFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length == ImageSystemFile.HiresFileSize)
      return _ParseHires(data);

    if (data.Length == ImageSystemFile.MulticolorFileSize)
      return _ParseMulticolor(data);

    throw new InvalidDataException(
      $"Invalid Image System data size: expected {ImageSystemFile.HiresFileSize} (hires) or {ImageSystemFile.MulticolorFileSize} (multicolor) bytes, got {data.Length}.");
  }

  private static ImageSystemFile _ParseHires(byte[] data) {
    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var bitmapData = new byte[ImageSystemFile.BitmapDataSize];
    data.AsSpan(ImageSystemFile.LoadAddressSize, ImageSystemFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));

    var screenData = new byte[ImageSystemFile.ScreenDataSize];
    data.AsSpan(ImageSystemFile.LoadAddressSize + ImageSystemFile.BitmapDataSize, ImageSystemFile.ScreenDataSize).CopyTo(screenData.AsSpan(0));

    // Border color byte follows screen data
    var borderColorOffset = ImageSystemFile.LoadAddressSize + ImageSystemFile.BitmapDataSize + ImageSystemFile.ScreenDataSize;
    var bgColor = borderColorOffset < data.Length ? data[borderColorOffset] : (byte)0;

    return new() {
      Width = 320,
      Height = 200,
      IsHires = true,
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = null,
      BackgroundColor = bgColor,
    };
  }

  private static ImageSystemFile _ParseMulticolor(byte[] data) {
    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var bitmapData = new byte[ImageSystemFile.BitmapDataSize];
    data.AsSpan(ImageSystemFile.LoadAddressSize, ImageSystemFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));

    var screenData = new byte[ImageSystemFile.ScreenDataSize];
    data.AsSpan(ImageSystemFile.LoadAddressSize + ImageSystemFile.BitmapDataSize, ImageSystemFile.ScreenDataSize).CopyTo(screenData.AsSpan(0));

    var colorData = new byte[ImageSystemFile.ColorDataSize];
    data.AsSpan(ImageSystemFile.LoadAddressSize + ImageSystemFile.BitmapDataSize + ImageSystemFile.ScreenDataSize, ImageSystemFile.ColorDataSize).CopyTo(colorData.AsSpan(0));

    var bgColorOffset = ImageSystemFile.LoadAddressSize + ImageSystemFile.BitmapDataSize + ImageSystemFile.ScreenDataSize + ImageSystemFile.ColorDataSize;
    var bgColor = bgColorOffset < data.Length ? data[bgColorOffset] : (byte)0;

    return new() {
      Width = 160,
      Height = 200,
      IsHires = false,
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      BackgroundColor = bgColor,
    };
  }
}

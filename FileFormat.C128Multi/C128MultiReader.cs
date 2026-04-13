using System;
using System.IO;

namespace FileFormat.C128Multi;

/// <summary>Reads C128 multicolor images from bytes, streams, or file paths.</summary>
public static class C128MultiReader {

  public static C128MultiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("C128 multicolor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static C128MultiFile FromStream(Stream stream) {
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

  public static C128MultiFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != C128MultiFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid C128 multicolor data size: expected exactly {C128MultiFile.ExpectedFileSize} bytes, got {data.Length}.");

    var bitmapData = new byte[C128MultiFile.BitmapDataSize];
    var screenData = new byte[C128MultiFile.ScreenDataSize];
    var colorData = new byte[C128MultiFile.ColorDataSize];

    data.Slice(0, C128MultiFile.BitmapDataSize).CopyTo(bitmapData);
    data.Slice(C128MultiFile.BitmapDataSize, C128MultiFile.ScreenDataSize).CopyTo(screenData);
    data.Slice(C128MultiFile.BitmapDataSize + C128MultiFile.ScreenDataSize, C128MultiFile.ColorDataSize).CopyTo(colorData);

    var bgColor = data[C128MultiFile.BitmapDataSize + C128MultiFile.ScreenDataSize + C128MultiFile.ColorDataSize];

    return new C128MultiFile {
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      BackgroundColor = bgColor,
    };
  }

  public static C128MultiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}

using System;
using System.IO;

namespace FileFormat.ColorStar;

/// <summary>Reads ColorSTar pictures from bytes, streams, or file paths.</summary>
public static class ColorStarReader {

  public static ColorStarFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ColorSTar picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ColorStarFile FromStream(Stream stream) {
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

  public static ColorStarFile FromSpan(ReadOnlySpan<byte> data) {
    // Two sizes, differing only by a pair of leading zero bytes before the palette.
    var start = data.Length switch {
      _ when data.Length == ColorStarFile.PlainFileSize => 0,
      _ when data.Length == ColorStarFile.PrefixedFileSize && data[0] == 0 && data[1] == 0 => 2,
      _ => throw new InvalidDataException(
        $"A ColorSTar picture is {ColorStarFile.PlainFileSize} or {ColorStarFile.PrefixedFileSize} bytes, got {data.Length}."),
    };

    var palette = new byte[ColorStarFile.PaletteSize];
    data.Slice(start, ColorStarFile.PaletteSize).CopyTo(palette);

    var bitmap = new byte[ColorStarFile.BitmapSize];
    data.Slice(start + ColorStarFile.PaletteSize, ColorStarFile.BitmapSize).CopyTo(bitmap);

    return new() { Palette = palette, BitmapData = bitmap };
  }

  public static ColorStarFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}

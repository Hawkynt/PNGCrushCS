using System;
using System.IO;

namespace FileFormat.ChampionsInterlace;

/// <summary>Reads Champions Interlace (.cin) files from bytes, streams, or file paths.</summary>
public static class ChampionsInterlaceReader {

  public static ChampionsInterlaceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Champions Interlace file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ChampionsInterlaceFile FromStream(Stream stream) {
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

  public static ChampionsInterlaceFile FromSpan(ReadOnlySpan<byte> data) {


    if (data.Length < ChampionsInterlaceFile.FileSize)
      throw new InvalidDataException($"File too small for Champions Interlace format (got {data.Length} bytes, need at least {ChampionsInterlaceFile.FileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += ChampionsInterlaceFile.LoadAddressSize;

    // Bitmap1 (8000 bytes)
    var bitmap1 = new byte[ChampionsInterlaceFile.BitmapDataSize];
    data.Slice(offset, ChampionsInterlaceFile.BitmapDataSize).CopyTo(bitmap1);
    offset += ChampionsInterlaceFile.BitmapDataSize;

    // Screen1 (1000 bytes)
    var screen1 = new byte[ChampionsInterlaceFile.ScreenDataSize];
    data.Slice(offset, ChampionsInterlaceFile.ScreenDataSize).CopyTo(screen1);
    offset += ChampionsInterlaceFile.ScreenDataSize;

    // ColorData (1000 bytes)
    var colorData = new byte[ChampionsInterlaceFile.ColorDataSize];
    data.Slice(offset, ChampionsInterlaceFile.ColorDataSize).CopyTo(colorData);
    offset += ChampionsInterlaceFile.ColorDataSize;

    // Bitmap2 (8000 bytes)
    var bitmap2 = new byte[ChampionsInterlaceFile.BitmapDataSize];
    data.Slice(offset, ChampionsInterlaceFile.BitmapDataSize).CopyTo(bitmap2);
    offset += ChampionsInterlaceFile.BitmapDataSize;

    // Screen2 (1000 bytes)
    var screen2 = new byte[ChampionsInterlaceFile.ScreenDataSize];
    data.Slice(offset, ChampionsInterlaceFile.ScreenDataSize).CopyTo(screen2);
    offset += ChampionsInterlaceFile.ScreenDataSize;

    // BackgroundColor (1 byte)
    var backgroundColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      ColorData = colorData,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
      BackgroundColor = backgroundColor,
    };
    }

  public static ChampionsInterlaceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < ChampionsInterlaceFile.FileSize)
      throw new InvalidDataException($"File too small for Champions Interlace format (got {data.Length} bytes, need at least {ChampionsInterlaceFile.FileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += ChampionsInterlaceFile.LoadAddressSize;

    // Bitmap1 (8000 bytes)
    var bitmap1 = new byte[ChampionsInterlaceFile.BitmapDataSize];
    data.AsSpan(offset, ChampionsInterlaceFile.BitmapDataSize).CopyTo(bitmap1);
    offset += ChampionsInterlaceFile.BitmapDataSize;

    // Screen1 (1000 bytes)
    var screen1 = new byte[ChampionsInterlaceFile.ScreenDataSize];
    data.AsSpan(offset, ChampionsInterlaceFile.ScreenDataSize).CopyTo(screen1);
    offset += ChampionsInterlaceFile.ScreenDataSize;

    // ColorData (1000 bytes)
    var colorData = new byte[ChampionsInterlaceFile.ColorDataSize];
    data.AsSpan(offset, ChampionsInterlaceFile.ColorDataSize).CopyTo(colorData);
    offset += ChampionsInterlaceFile.ColorDataSize;

    // Bitmap2 (8000 bytes)
    var bitmap2 = new byte[ChampionsInterlaceFile.BitmapDataSize];
    data.AsSpan(offset, ChampionsInterlaceFile.BitmapDataSize).CopyTo(bitmap2);
    offset += ChampionsInterlaceFile.BitmapDataSize;

    // Screen2 (1000 bytes)
    var screen2 = new byte[ChampionsInterlaceFile.ScreenDataSize];
    data.AsSpan(offset, ChampionsInterlaceFile.ScreenDataSize).CopyTo(screen2);
    offset += ChampionsInterlaceFile.ScreenDataSize;

    // BackgroundColor (1 byte)
    var backgroundColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      ColorData = colorData,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
      BackgroundColor = backgroundColor,
    };
  }
}

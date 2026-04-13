using System;
using System.IO;

namespace FileFormat.InterlaceStudio;

/// <summary>Reads Interlace Studio (.ist) files from bytes, streams, or file paths.</summary>
public static class InterlaceStudioReader {

  public static InterlaceStudioFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Interlace Studio file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterlaceStudioFile FromStream(Stream stream) {
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

  public static InterlaceStudioFile FromSpan(ReadOnlySpan<byte> data) {


    if (data.Length < InterlaceStudioFile.FileSize)
      throw new InvalidDataException($"File too small for Interlace Studio format (got {data.Length} bytes, need at least {InterlaceStudioFile.FileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += InterlaceStudioFile.LoadAddressSize;

    // Bitmap1 (8000 bytes)
    var bitmap1 = new byte[InterlaceStudioFile.BitmapDataSize];
    data.Slice(offset, InterlaceStudioFile.BitmapDataSize).CopyTo(bitmap1);
    offset += InterlaceStudioFile.BitmapDataSize;

    // Screen1 (1000 bytes)
    var screen1 = new byte[InterlaceStudioFile.ScreenDataSize];
    data.Slice(offset, InterlaceStudioFile.ScreenDataSize).CopyTo(screen1);
    offset += InterlaceStudioFile.ScreenDataSize;

    // ColorData (1000 bytes)
    var colorData = new byte[InterlaceStudioFile.ColorDataSize];
    data.Slice(offset, InterlaceStudioFile.ColorDataSize).CopyTo(colorData);
    offset += InterlaceStudioFile.ColorDataSize;

    // Bitmap2 (8000 bytes)
    var bitmap2 = new byte[InterlaceStudioFile.BitmapDataSize];
    data.Slice(offset, InterlaceStudioFile.BitmapDataSize).CopyTo(bitmap2);
    offset += InterlaceStudioFile.BitmapDataSize;

    // Screen2 (1000 bytes)
    var screen2 = new byte[InterlaceStudioFile.ScreenDataSize];
    data.Slice(offset, InterlaceStudioFile.ScreenDataSize).CopyTo(screen2);
    offset += InterlaceStudioFile.ScreenDataSize;

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

  public static InterlaceStudioFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < InterlaceStudioFile.FileSize)
      throw new InvalidDataException($"File too small for Interlace Studio format (got {data.Length} bytes, need at least {InterlaceStudioFile.FileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += InterlaceStudioFile.LoadAddressSize;

    // Bitmap1 (8000 bytes)
    var bitmap1 = new byte[InterlaceStudioFile.BitmapDataSize];
    data.AsSpan(offset, InterlaceStudioFile.BitmapDataSize).CopyTo(bitmap1);
    offset += InterlaceStudioFile.BitmapDataSize;

    // Screen1 (1000 bytes)
    var screen1 = new byte[InterlaceStudioFile.ScreenDataSize];
    data.AsSpan(offset, InterlaceStudioFile.ScreenDataSize).CopyTo(screen1);
    offset += InterlaceStudioFile.ScreenDataSize;

    // ColorData (1000 bytes)
    var colorData = new byte[InterlaceStudioFile.ColorDataSize];
    data.AsSpan(offset, InterlaceStudioFile.ColorDataSize).CopyTo(colorData);
    offset += InterlaceStudioFile.ColorDataSize;

    // Bitmap2 (8000 bytes)
    var bitmap2 = new byte[InterlaceStudioFile.BitmapDataSize];
    data.AsSpan(offset, InterlaceStudioFile.BitmapDataSize).CopyTo(bitmap2);
    offset += InterlaceStudioFile.BitmapDataSize;

    // Screen2 (1000 bytes)
    var screen2 = new byte[InterlaceStudioFile.ScreenDataSize];
    data.AsSpan(offset, InterlaceStudioFile.ScreenDataSize).CopyTo(screen2);
    offset += InterlaceStudioFile.ScreenDataSize;

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

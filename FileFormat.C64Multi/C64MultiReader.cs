using System;
using System.IO;

namespace FileFormat.C64Multi;

/// <summary>Reads C64 multiformat art files from bytes, streams, or file paths.</summary>
public static class C64MultiReader {

  public static C64MultiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("C64 multi file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static C64MultiFile FromStream(Stream stream) {
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

  public static C64MultiFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static C64MultiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    var format = _DetectFormat(data.Length);

    return format switch {
      C64MultiFormat.ArtStudioHires => _ParseArtStudioHires(data),
      C64MultiFormat.ArtStudioMulti => _ParseArtStudioMulti(data),
      _ => throw new InvalidDataException($"Unsupported C64 multi format for file size {data.Length}.")
    };
  }

  private static C64MultiFormat _DetectFormat(int fileSize) => fileSize switch {
    C64MultiFile.ArtStudioHiresFileSize => C64MultiFormat.ArtStudioHires,
    C64MultiFile.ArtStudioMultiFileSize => C64MultiFormat.ArtStudioMulti,
    _ => throw new InvalidDataException($"Unrecognized C64 multi file size (got {fileSize} bytes, expected {C64MultiFile.ArtStudioHiresFileSize} or {C64MultiFile.ArtStudioMultiFileSize}).")
  };

  private static C64MultiFile _ParseArtStudioHires(byte[] data) {
    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += C64MultiFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[C64MultiFile.BitmapDataSize];
    data.AsSpan(offset, C64MultiFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += C64MultiFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    var screenData = new byte[C64MultiFile.ScreenDataSize];
    data.AsSpan(offset, C64MultiFile.ScreenDataSize).CopyTo(screenData.AsSpan(0));
    offset += C64MultiFile.ScreenDataSize;

    // Border color (1 byte)
    var borderColor = data[offset];

    return new() {
      Width = 320,
      Height = 200,
      Format = C64MultiFormat.ArtStudioHires,
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = null,
      BackgroundColor = borderColor
    };
  }

  private static C64MultiFile _ParseArtStudioMulti(byte[] data) {
    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += C64MultiFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[C64MultiFile.BitmapDataSize];
    data.AsSpan(offset, C64MultiFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += C64MultiFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    var screenData = new byte[C64MultiFile.ScreenDataSize];
    data.AsSpan(offset, C64MultiFile.ScreenDataSize).CopyTo(screenData.AsSpan(0));
    offset += C64MultiFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    var colorData = new byte[C64MultiFile.ColorDataSize];
    data.AsSpan(offset, C64MultiFile.ColorDataSize).CopyTo(colorData.AsSpan(0));
    offset += C64MultiFile.ColorDataSize;

    // Background color (1 byte)
    var backgroundColor = data[offset];

    return new() {
      Width = 160,
      Height = 200,
      Format = C64MultiFormat.ArtStudioMulti,
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      BackgroundColor = backgroundColor
    };
  }
}

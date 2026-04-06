using System;
using System.IO;

namespace FileFormat.SuperHires;

/// <summary>Reads Super Hires (C64 interlace hires) files from bytes, streams, or file paths.</summary>
public static class SuperHiresReader {

  public static SuperHiresFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Super Hires file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SuperHiresFile FromStream(Stream stream) {
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

  public static SuperHiresFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SuperHiresFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SuperHiresFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for Super Hires file (got {data.Length} bytes, expected {SuperHiresFile.ExpectedFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += SuperHiresFile.LoadAddressSize;

    // Frame 1: Bitmap data (8000 bytes)
    var bitmapData1 = new byte[SuperHiresFile.BitmapDataSize];
    data.AsSpan(offset, SuperHiresFile.BitmapDataSize).CopyTo(bitmapData1.AsSpan(0));
    offset += SuperHiresFile.BitmapDataSize;

    // Frame 1: Screen RAM (1000 bytes)
    var screenData1 = new byte[SuperHiresFile.ScreenDataSize];
    data.AsSpan(offset, SuperHiresFile.ScreenDataSize).CopyTo(screenData1.AsSpan(0));
    offset += SuperHiresFile.ScreenDataSize;

    // Frame 2: Bitmap data (8000 bytes)
    var bitmapData2 = new byte[SuperHiresFile.BitmapDataSize];
    data.AsSpan(offset, SuperHiresFile.BitmapDataSize).CopyTo(bitmapData2.AsSpan(0));
    offset += SuperHiresFile.BitmapDataSize;

    // Frame 2: Screen RAM (1000 bytes)
    var screenData2 = new byte[SuperHiresFile.ScreenDataSize];
    data.AsSpan(offset, SuperHiresFile.ScreenDataSize).CopyTo(screenData2.AsSpan(0));
    offset += SuperHiresFile.ScreenDataSize;

    // Padding/extra data (remaining bytes)
    var paddingLength = data.Length - offset;
    var padding = new byte[paddingLength];
    if (paddingLength > 0)
      data.AsSpan(offset, paddingLength).CopyTo(padding.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData1 = bitmapData1,
      ScreenData1 = screenData1,
      BitmapData2 = bitmapData2,
      ScreenData2 = screenData2,
      Padding = padding,
    };
  }
}

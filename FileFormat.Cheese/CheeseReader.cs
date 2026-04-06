using System;
using System.IO;

namespace FileFormat.Cheese;

/// <summary>Reads Commodore 64 Cheese paint files from bytes, streams, or file paths.</summary>
public static class CheeseReader {

  public static CheeseFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Cheese file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CheeseFile FromStream(Stream stream) {
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

  public static CheeseFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static CheeseFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CheeseFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Cheese file (expected {CheeseFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != CheeseFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Cheese file size (expected {CheeseFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += CheeseFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[CheeseFile.BitmapDataSize];
    data.AsSpan(offset, CheeseFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += CheeseFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    var videoMatrix = new byte[CheeseFile.VideoMatrixSize];
    data.AsSpan(offset, CheeseFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += CheeseFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[CheeseFile.ColorRamSize];
    data.AsSpan(offset, CheeseFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += CheeseFile.ColorRamSize;

    // Border color (1 byte)
    var borderColor = data[offset];
    ++offset;

    // Background color (1 byte)
    var backgroundColor = data[offset];
    ++offset;

    // Padding (14 bytes)
    var padding = new byte[CheeseFile.PaddingSize];
    data.AsSpan(offset, CheeseFile.PaddingSize).CopyTo(padding.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BorderColor = borderColor,
      BackgroundColor = backgroundColor,
      Padding = padding,
    };
  }
}

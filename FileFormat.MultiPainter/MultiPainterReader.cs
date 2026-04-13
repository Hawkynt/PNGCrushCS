using System;
using System.IO;

namespace FileFormat.MultiPainter;

/// <summary>Reads Commodore 64 Multi Painter files from bytes, streams, or file paths.</summary>
public static class MultiPainterReader {

  public static MultiPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Multi Painter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MultiPainterFile FromStream(Stream stream) {
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

  public static MultiPainterFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < MultiPainterFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Multi Painter file (expected {MultiPainterFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != MultiPainterFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Multi Painter file size (expected {MultiPainterFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += MultiPainterFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[MultiPainterFile.BitmapDataSize];
    data.Slice(offset, MultiPainterFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += MultiPainterFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    var videoMatrix = new byte[MultiPainterFile.VideoMatrixSize];
    data.Slice(offset, MultiPainterFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += MultiPainterFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[MultiPainterFile.ColorRamSize];
    data.Slice(offset, MultiPainterFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += MultiPainterFile.ColorRamSize;

    // Border color (1 byte)
    var borderColor = data[offset];
    ++offset;

    // Background color (1 byte)
    var backgroundColor = data[offset];
    ++offset;

    // Padding (14 bytes)
    var padding = new byte[MultiPainterFile.PaddingSize];
    data.Slice(offset, MultiPainterFile.PaddingSize).CopyTo(padding.AsSpan(0));

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

  public static MultiPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MultiPainterFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Multi Painter file (expected {MultiPainterFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != MultiPainterFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Multi Painter file size (expected {MultiPainterFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += MultiPainterFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[MultiPainterFile.BitmapDataSize];
    data.AsSpan(offset, MultiPainterFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += MultiPainterFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    var videoMatrix = new byte[MultiPainterFile.VideoMatrixSize];
    data.AsSpan(offset, MultiPainterFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += MultiPainterFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[MultiPainterFile.ColorRamSize];
    data.AsSpan(offset, MultiPainterFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += MultiPainterFile.ColorRamSize;

    // Border color (1 byte)
    var borderColor = data[offset];
    ++offset;

    // Background color (1 byte)
    var backgroundColor = data[offset];
    ++offset;

    // Padding (14 bytes)
    var padding = new byte[MultiPainterFile.PaddingSize];
    data.AsSpan(offset, MultiPainterFile.PaddingSize).CopyTo(padding.AsSpan(0));

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

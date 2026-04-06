using System;
using System.IO;

namespace FileFormat.BugBitmap;

/// <summary>Reads Commodore 64 Bug Bitmap files from bytes, streams, or file paths.</summary>
public static class BugBitmapReader {

  public static BugBitmapFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Bug Bitmap file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BugBitmapFile FromStream(Stream stream) {
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

  public static BugBitmapFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static BugBitmapFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < BugBitmapFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Bug Bitmap file (expected {BugBitmapFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != BugBitmapFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Bug Bitmap file size (expected {BugBitmapFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += BugBitmapFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[BugBitmapFile.BitmapDataSize];
    data.AsSpan(offset, BugBitmapFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += BugBitmapFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    var videoMatrix = new byte[BugBitmapFile.VideoMatrixSize];
    data.AsSpan(offset, BugBitmapFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += BugBitmapFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[BugBitmapFile.ColorRamSize];
    data.AsSpan(offset, BugBitmapFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += BugBitmapFile.ColorRamSize;

    // Border color (1 byte)
    var borderColor = data[offset];
    ++offset;

    // Background color (1 byte)
    var backgroundColor = data[offset];
    ++offset;

    // Padding (14 bytes)
    var padding = new byte[BugBitmapFile.PaddingSize];
    data.AsSpan(offset, BugBitmapFile.PaddingSize).CopyTo(padding.AsSpan(0));

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

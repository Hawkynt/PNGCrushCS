using System;
using System.IO;

namespace FileFormat.Koala;

/// <summary>Reads Commodore 64 Koala Painter files from bytes, streams, or file paths.</summary>
public static class KoalaReader {

  public static KoalaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Koala file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static KoalaFile FromStream(Stream stream) {
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

  public static KoalaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < KoalaFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Koala file (expected {KoalaFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != KoalaFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Koala file size (expected {KoalaFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += KoalaFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[KoalaFile.BitmapDataSize];
    data.AsSpan(offset, KoalaFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += KoalaFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    var videoMatrix = new byte[KoalaFile.VideoMatrixSize];
    data.AsSpan(offset, KoalaFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += KoalaFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[KoalaFile.ColorRamSize];
    data.AsSpan(offset, KoalaFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += KoalaFile.ColorRamSize;

    // Background color (1 byte)
    var backgroundColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor
    };
  }
}

using System;
using System.IO;

namespace FileFormat.Centauri;

/// <summary>Reads Commodore 64 Centauri paint files from bytes, streams, or file paths.</summary>
public static class CentauriReader {

  public static CentauriFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Centauri file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CentauriFile FromStream(Stream stream) {
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

  public static CentauriFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < CentauriFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Centauri file (expected {CentauriFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != CentauriFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Centauri file size (expected {CentauriFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += CentauriFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[CentauriFile.BitmapDataSize];
    data.Slice(offset, CentauriFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += CentauriFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    var videoMatrix = new byte[CentauriFile.VideoMatrixSize];
    data.Slice(offset, CentauriFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += CentauriFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[CentauriFile.ColorRamSize];
    data.Slice(offset, CentauriFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += CentauriFile.ColorRamSize;

    // Border color (1 byte)
    var borderColor = data[offset];
    ++offset;

    // Background color (1 byte)
    var backgroundColor = data[offset];
    ++offset;

    // Padding (14 bytes)
    var padding = new byte[CentauriFile.PaddingSize];
    data.Slice(offset, CentauriFile.PaddingSize).CopyTo(padding.AsSpan(0));

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

  public static CentauriFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CentauriFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Centauri file (expected {CentauriFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != CentauriFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Centauri file size (expected {CentauriFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += CentauriFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[CentauriFile.BitmapDataSize];
    data.AsSpan(offset, CentauriFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += CentauriFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    var videoMatrix = new byte[CentauriFile.VideoMatrixSize];
    data.AsSpan(offset, CentauriFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += CentauriFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[CentauriFile.ColorRamSize];
    data.AsSpan(offset, CentauriFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += CentauriFile.ColorRamSize;

    // Border color (1 byte)
    var borderColor = data[offset];
    ++offset;

    // Background color (1 byte)
    var backgroundColor = data[offset];
    ++offset;

    // Padding (14 bytes)
    var padding = new byte[CentauriFile.PaddingSize];
    data.AsSpan(offset, CentauriFile.PaddingSize).CopyTo(padding.AsSpan(0));

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

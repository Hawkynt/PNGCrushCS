using System;
using System.IO;

namespace FileFormat.Pixel64;

/// <summary>Reads Commodore 64 Pixel Perfect paint files from bytes, streams, or file paths.</summary>
public static class Pixel64Reader {

  public static Pixel64File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Pixel64 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Pixel64File FromStream(Stream stream) {
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

  public static Pixel64File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < Pixel64File.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Pixel64 file (expected {Pixel64File.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != Pixel64File.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Pixel64 file size (expected {Pixel64File.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += Pixel64File.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[Pixel64File.BitmapDataSize];
    data.Slice(offset, Pixel64File.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += Pixel64File.BitmapDataSize;

    // Video matrix (1000 bytes)
    var videoMatrix = new byte[Pixel64File.VideoMatrixSize];
    data.Slice(offset, Pixel64File.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += Pixel64File.VideoMatrixSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[Pixel64File.ColorRamSize];
    data.Slice(offset, Pixel64File.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += Pixel64File.ColorRamSize;

    // Border color (1 byte)
    var borderColor = data[offset];
    ++offset;

    // Background color (1 byte)
    var backgroundColor = data[offset];
    ++offset;

    // Padding (14 bytes)
    var padding = new byte[Pixel64File.PaddingSize];
    data.Slice(offset, Pixel64File.PaddingSize).CopyTo(padding.AsSpan(0));

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

  public static Pixel64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Pixel64File.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Pixel64 file (expected {Pixel64File.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != Pixel64File.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Pixel64 file size (expected {Pixel64File.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += Pixel64File.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[Pixel64File.BitmapDataSize];
    data.AsSpan(offset, Pixel64File.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += Pixel64File.BitmapDataSize;

    // Video matrix (1000 bytes)
    var videoMatrix = new byte[Pixel64File.VideoMatrixSize];
    data.AsSpan(offset, Pixel64File.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += Pixel64File.VideoMatrixSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[Pixel64File.ColorRamSize];
    data.AsSpan(offset, Pixel64File.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += Pixel64File.ColorRamSize;

    // Border color (1 byte)
    var borderColor = data[offset];
    ++offset;

    // Background color (1 byte)
    var backgroundColor = data[offset];
    ++offset;

    // Padding (14 bytes)
    var padding = new byte[Pixel64File.PaddingSize];
    data.AsSpan(offset, Pixel64File.PaddingSize).CopyTo(padding.AsSpan(0));

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

using System;
using System.IO;

namespace FileFormat.ImageSysC64;

/// <summary>Reads Commodore 64 Image System C64 files from bytes, streams, or file paths.</summary>
public static class ImageSysC64Reader {

  public static ImageSysC64File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Image System C64 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ImageSysC64File FromStream(Stream stream) {
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

  public static ImageSysC64File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static ImageSysC64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ImageSysC64File.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Image System C64 file (expected {ImageSysC64File.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != ImageSysC64File.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Image System C64 file size (expected {ImageSysC64File.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += ImageSysC64File.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[ImageSysC64File.BitmapDataSize];
    data.AsSpan(offset, ImageSysC64File.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += ImageSysC64File.BitmapDataSize;

    // Video matrix (1000 bytes)
    var videoMatrix = new byte[ImageSysC64File.VideoMatrixSize];
    data.AsSpan(offset, ImageSysC64File.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += ImageSysC64File.VideoMatrixSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[ImageSysC64File.ColorRamSize];
    data.AsSpan(offset, ImageSysC64File.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += ImageSysC64File.ColorRamSize;

    // Border color (1 byte)
    var borderColor = data[offset];
    ++offset;

    // Background color (1 byte)
    var backgroundColor = data[offset];
    ++offset;

    // Padding (14 bytes)
    var padding = new byte[ImageSysC64File.PaddingSize];
    data.AsSpan(offset, ImageSysC64File.PaddingSize).CopyTo(padding.AsSpan(0));

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

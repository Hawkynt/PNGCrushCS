using System;
using System.IO;

namespace FileFormat.WebShots;

/// <summary>Reads WebShots image files from bytes, streams, or file paths.</summary>
public static class WebShotsReader {

  public static WebShotsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WebShots file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WebShotsFile FromStream(Stream stream) {
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

  public static WebShotsFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < WebShotsFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid WebShots file (need at least {WebShotsFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != WebShotsFile.Magic[0] || data[1] != WebShotsFile.Magic[1] || data[2] != WebShotsFile.Magic[2] || data[3] != WebShotsFile.Magic[3])
      throw new InvalidDataException("Invalid WebShots magic bytes.");

    var header = WebShotsHeader.ReadFrom(data);
    var version = header.Version;
    var width = header.Width;
    var height = header.Height;
    var bpp = header.Bpp;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid WebShots dimensions: {width}x{height}.");

    var pixelDataSize = data.Length - WebShotsFile.HeaderSize;
    var pixelData = new byte[pixelDataSize];
    if (pixelDataSize > 0)
      data.Slice(WebShotsFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      Bpp = bpp,
      PixelData = pixelData,
    };
  }

  public static WebShotsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}

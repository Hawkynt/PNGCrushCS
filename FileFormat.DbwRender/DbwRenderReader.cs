using System;
using System.IO;

namespace FileFormat.DbwRender;

/// <summary>Reads DBW Render image files from bytes, streams, or file paths.</summary>
public static class DbwRenderReader {

  public static DbwRenderFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DBW file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DbwRenderFile FromStream(Stream stream) {
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

  public static DbwRenderFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static DbwRenderFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < DbwRenderFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid DBW file: expected at least {DbwRenderFile.HeaderSize} bytes, got {data.Length}.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);

    if (width <= 0)
      throw new InvalidDataException($"Invalid DBW width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid DBW height: {height}.");

    var expectedPixelBytes = width * height * 3;
    if (data.Length < DbwRenderFile.HeaderSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {DbwRenderFile.HeaderSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(DbwRenderFile.HeaderSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new DbwRenderFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}

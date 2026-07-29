using System;
using System.IO;

namespace FileFormat.SbigCcd;

/// <summary>Reads SBIG CCD camera image files from bytes, streams, or file paths.</summary>
public static class SbigCcdReader {

  public static SbigCcdFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SBIG CCD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SbigCcdFile FromStream(Stream stream) {
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

  public static SbigCcdFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < SbigCcdFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid SBIG CCD file: expected at least {SbigCcdFile.HeaderSize} bytes, got {data.Length}.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);

    if (width <= 0)
      throw new InvalidDataException($"Invalid SBIG CCD width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid SBIG CCD height: {height}.");

    var expectedPixelBytes = width * height * 2;
    if (data.Length < SbigCcdFile.HeaderSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {SbigCcdFile.HeaderSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(SbigCcdFile.HeaderSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new SbigCcdFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static SbigCcdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}

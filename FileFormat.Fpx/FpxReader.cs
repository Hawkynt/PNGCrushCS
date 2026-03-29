using System;
using System.IO;

namespace FileFormat.Fpx;

/// <summary>Reads FPX files from bytes, streams, or file paths.</summary>
public static class FpxReader {

  public static FpxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FPX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FpxFile FromStream(Stream stream) {
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

  public static FpxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FpxHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid FPX file.");

    if (data[0] != FpxHeader.Magic[0] || data[1] != FpxHeader.Magic[1] || data[2] != FpxHeader.Magic[2] || data[3] != FpxHeader.Magic[3])
      throw new InvalidDataException("Invalid FPX magic bytes (expected 'FPX\\0').");

    var header = FpxHeader.ReadFrom(data.AsSpan());
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width <= 0)
      throw new InvalidDataException($"Invalid FPX width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid FPX height: {height}.");

    var expectedPixelBytes = width * height * 3;
    var available = data.Length - FpxHeader.StructSize;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(FpxHeader.StructSize, copyLen).CopyTo(pixelData.AsSpan(0));

    return new FpxFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}

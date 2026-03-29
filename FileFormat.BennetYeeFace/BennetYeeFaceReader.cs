using System;
using System.IO;

namespace FileFormat.BennetYeeFace;

/// <summary>Reads Bennet Yee Face (.ybm) files from bytes, streams, or file paths.</summary>
public static class BennetYeeFaceReader {

  public static BennetYeeFaceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("YBM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BennetYeeFaceFile FromStream(Stream stream) {
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

  public static BennetYeeFaceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < BennetYeeFaceHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid YBM file.");

    var header = BennetYeeFaceHeader.ReadFrom(data.AsSpan());
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width <= 0)
      throw new InvalidDataException($"Invalid YBM width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid YBM height: {height}.");

    var stride = BennetYeeFaceFile.ComputeStride(width);
    var expectedPixelBytes = stride * height;

    if (data.Length < BennetYeeFaceHeader.StructSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {BennetYeeFaceHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(BennetYeeFaceHeader.StructSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new BennetYeeFaceFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}

using System;
using System.IO;

namespace FileFormat.Ingr;

/// <summary>Reads Intergraph Raster (INGR) files from bytes, streams, or file paths.</summary>
public static class IngrReader {

  /// <summary>Minimum size of an INGR file (512-byte header block).</summary>
  internal const int HeaderSize = 512;

  public static IngrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("INGR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IngrFile FromStream(Stream stream) {
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

  public static IngrFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < HeaderSize)
      throw new InvalidDataException($"INGR data too small: {data.Length} bytes, need at least {HeaderSize}.");

    var header = IngrHeader.ReadFrom(data);

    if (!Enum.IsDefined(typeof(IngrDataType), header.DataTypeCode))
      throw new InvalidDataException($"Unsupported INGR data type code: {header.DataTypeCode}.");

    var dataType = (IngrDataType)header.DataTypeCode;
    var width = header.PixelsPerLine;
    var height = header.NumberOfLines;

    if (width <= 0) {
      var xExtent = Math.Abs(header.XExtent);
      width = xExtent > 0 ? xExtent : throw new InvalidDataException("INGR image width must be positive.");
    }

    if (height <= 0) {
      var yExtent = Math.Abs(header.YExtent);
      height = yExtent > 0 ? yExtent : throw new InvalidDataException("INGR image height must be positive.");
    }

    var bytesPerPixel = dataType == IngrDataType.ByteData ? 1 : 3;
    var expectedPixelBytes = width * height * bytesPerPixel;
    var available = data.Length - HeaderSize;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(HeaderSize, copyLen).CopyTo(pixelData.AsSpan(0));

    return new IngrFile {
      Width = width,
      Height = height,
      DataType = dataType,
      PixelData = pixelData,
    };
  }

  public static IngrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}

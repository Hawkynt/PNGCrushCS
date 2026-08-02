using System;
using System.IO;

namespace FileFormat.Degas;

/// <summary>Reads DEGAS/DEGAS Elite files from bytes, streams, or file paths.</summary>
public static class DegasReader {

  private const int _UNCOMPRESSED_PIXEL_DATA_SIZE = 32000;
  private const int _COMPRESSION_FLAG = unchecked((short)0x8000);

  public static DegasFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DEGAS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DegasFile FromStream(Stream stream) {
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

  public static DegasFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < DegasHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid DEGAS file.");

    var span = data;
    var header = DegasHeader.ReadFrom(span);

    var rawResolution = header.Resolution;
    var isCompressed = (rawResolution & _COMPRESSION_FLAG) != 0;
    var resolutionValue = rawResolution & 0x7FFF;

    if (resolutionValue is < 0 or > 2)
      throw new InvalidDataException($"Invalid DEGAS resolution value: {resolutionValue}.");

    var resolution = (DegasResolution)resolutionValue;
    var (width, height) = _GetDimensions(resolution);

    byte[] pixelData;
    if (isCompressed) {
      var compressedData = new byte[data.Length - DegasHeader.StructSize];
      data.Slice(DegasHeader.StructSize, compressedData.Length).CopyTo(compressedData.AsSpan(0));
      pixelData = PackBitsCompressor.Decompress(compressedData, _UNCOMPRESSED_PIXEL_DATA_SIZE);
      pixelData = _InterleavePlaneRows(pixelData, width, resolution);
    } else {
      if (data.Length < DegasHeader.StructSize + _UNCOMPRESSED_PIXEL_DATA_SIZE)
        throw new InvalidDataException("Data too small for uncompressed DEGAS file.");

      pixelData = new byte[_UNCOMPRESSED_PIXEL_DATA_SIZE];
      data.Slice(DegasHeader.StructSize, _UNCOMPRESSED_PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));
    }

    return new DegasFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      IsCompressed = isCompressed,
      Palette = header.Palette,
      PixelData = pixelData
    };
    }

  public static DegasFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

/// <summary>
  /// Turns a packed picture's plane-row-major scanlines into the word-interleaved screen.
  /// </summary>
  /// <remarks>
  /// An uncompressed DEGAS holds the machine's screen as it stands, four planes interleaved a word
  /// at a time. A packed one does not: it stores each scanline as one whole plane row after another,
  /// which unpacks to the same number of bytes in a different arrangement. Using it as it comes
  /// leaves the picture in roughly the right colours with every group of sixteen pixels drawn from
  /// four unrelated places.
  /// </remarks>
  private static byte[] _InterleavePlaneRows(byte[] data, int width, DegasResolution resolution) {
    var planes = resolution switch {
      DegasResolution.Low => 4,
      DegasResolution.Medium => 2,
      _ => 1,
    };

    if (planes == 1)
      return data;

    var wordsPerPlaneRow = (width + 15) / 16;
    var bytesPerRow = wordsPerPlaneRow * 2 * planes;
    var rows = data.Length / bytesPerRow;
    var result = new byte[data.Length];

    for (var row = 0; row < rows; ++row)
    for (var plane = 0; plane < planes; ++plane)
    for (var word = 0; word < wordsPerPlaneRow; ++word) {
      var from = row * bytesPerRow + plane * wordsPerPlaneRow * 2 + word * 2;
      var to = row * bytesPerRow + (word * planes + plane) * 2;
      result[to] = data[from];
      result[to + 1] = data[from + 1];
    }

    return result;
  }

    private static (int Width, int Height) _GetDimensions(DegasResolution resolution) => resolution switch {
    DegasResolution.Low => (320, 200),
    DegasResolution.Medium => (640, 200),
    DegasResolution.High => (640, 400),
    _ => throw new InvalidDataException($"Unknown DEGAS resolution: {resolution}.")
  };
}

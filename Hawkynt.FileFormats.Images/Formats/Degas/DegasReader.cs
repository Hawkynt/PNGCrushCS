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

  private static (int Width, int Height) _GetDimensions(DegasResolution resolution) => resolution switch {
    DegasResolution.Low => (320, 200),
    DegasResolution.Medium => (640, 200),
    DegasResolution.High => (640, 400),
    _ => throw new InvalidDataException($"Unknown DEGAS resolution: {resolution}.")
  };
}

using System;
using System.IO;

namespace FileFormat.CrackArt;

/// <summary>Reads CrackArt files from bytes, streams, or file paths.</summary>
public static class CrackArtReader {

  private const int _DECOMPRESSED_PIXEL_DATA_SIZE = 32000;

  public static CrackArtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CrackArt file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CrackArtFile FromStream(Stream stream) {
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

  public static CrackArtFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static CrackArtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CrackArtHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid CrackArt file.");

    var span = data.AsSpan();
    var header = CrackArtHeader.ReadFrom(span);

    var resolutionValue = header.Resolution;
    if (resolutionValue > 2)
      throw new InvalidDataException($"Invalid CrackArt resolution value: {resolutionValue}.");

    var resolution = (CrackArtResolution)resolutionValue;
    var (width, height) = _GetDimensions(resolution);

    var compressedData = new byte[data.Length - CrackArtHeader.StructSize];
    data.AsSpan(CrackArtHeader.StructSize, compressedData.Length).CopyTo(compressedData.AsSpan(0));
    var pixelData = CrackArtCompressor.Decompress(compressedData, _DECOMPRESSED_PIXEL_DATA_SIZE);

    return new CrackArtFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.GetPaletteArray(),
      PixelData = pixelData
    };
  }

  private static (int Width, int Height) _GetDimensions(CrackArtResolution resolution) => resolution switch {
    CrackArtResolution.Low => (320, 200),
    CrackArtResolution.Medium => (640, 200),
    CrackArtResolution.High => (640, 400),
    _ => throw new InvalidDataException($"Unknown CrackArt resolution: {resolution}.")
  };
}

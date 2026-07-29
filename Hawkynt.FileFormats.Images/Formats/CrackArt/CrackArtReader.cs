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

  public static CrackArtFile FromSpan(ReadOnlySpan<byte> data) {

    if (!CrackArtHeader.TryRead(data, out var isCompressed, out var resolution))
      throw new InvalidDataException("Not a CrackArt file: missing the 'CA' tag.");

    var header = new { Palette = CrackArtHeader.ReadPalette(data, resolution) };
    var dataOffset = CrackArtHeader.GetDataOffset(resolution);
    var (width, height) = _GetDimensions(resolution);

    var compressedData = new byte[data.Length - dataOffset];
    data.Slice(dataOffset, compressedData.Length).CopyTo(compressedData.AsSpan(0));
    var pixelData = CrackArtCompressor.Decompress(compressedData, _DECOMPRESSED_PIXEL_DATA_SIZE);

    return new CrackArtFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.Palette,
      PixelData = pixelData
    };
    }

  public static CrackArtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (!CrackArtHeader.TryRead(data, out var isCompressed, out var resolution))
      throw new InvalidDataException("Not a CrackArt file: missing the 'CA' tag.");

    var header = new { Palette = CrackArtHeader.ReadPalette(data, resolution) };
    var dataOffset = CrackArtHeader.GetDataOffset(resolution);
    var (width, height) = _GetDimensions(resolution);

    var compressedData = new byte[data.Length - dataOffset];
    data.AsSpan(dataOffset, compressedData.Length).CopyTo(compressedData.AsSpan(0));
    var pixelData = CrackArtCompressor.Decompress(compressedData, _DECOMPRESSED_PIXEL_DATA_SIZE);

    return new CrackArtFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.Palette,
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

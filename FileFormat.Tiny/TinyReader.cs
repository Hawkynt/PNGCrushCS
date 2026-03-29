using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Tiny;

/// <summary>Reads Tiny (compressed DEGAS) files from bytes, streams, or file paths.</summary>
public static class TinyReader {

  private const int _HEADER_SIZE = 1 + 16 * 2; // 1 byte resolution + 16 palette words = 33

  public static TinyFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Tiny file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TinyFile FromStream(Stream stream) {
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

  public static TinyFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid Tiny file.");

    var resolutionByte = data[0];
    if (resolutionByte > 2)
      throw new InvalidDataException($"Invalid Tiny resolution value: {resolutionByte}.");

    var resolution = (TinyResolution)resolutionByte;
    var (width, height, planeCount, wordsPerPlane) = _GetFormatInfo(resolution);

    var span = data.AsSpan();
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(span[(1 + i * 2)..]);

    var compressedData = new byte[data.Length - _HEADER_SIZE];
    data.AsSpan(_HEADER_SIZE, compressedData.Length).CopyTo(compressedData.AsSpan(0));

    var pixelData = TinyCompressor.Decompress(compressedData, planeCount, wordsPerPlane);

    return new TinyFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = palette,
      PixelData = pixelData
    };
  }

  private static (int Width, int Height, int PlaneCount, int WordsPerPlane) _GetFormatInfo(TinyResolution resolution) => resolution switch {
    TinyResolution.Low => (320, 200, 4, 4000),
    TinyResolution.Medium => (640, 200, 2, 8000),
    TinyResolution.High => (640, 400, 1, 16000),
    _ => throw new InvalidDataException($"Unknown Tiny resolution: {resolution}.")
  };
}

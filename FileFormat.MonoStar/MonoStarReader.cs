using System;
using System.IO;

namespace FileFormat.MonoStar;

/// <summary>Reads MonoStar files from bytes, streams, or file paths.</summary>
public static class MonoStarReader {

  private const int _HEADER_SIZE = 34;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _EXPECTED_FILE_SIZE = _HEADER_SIZE + _PIXEL_DATA_SIZE;

  public static MonoStarFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MonoStar file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MonoStarFile FromStream(Stream stream) {
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

  public static MonoStarFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MonoStarFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid MonoStar file.");

    if (data.Length < _EXPECTED_FILE_SIZE)
      throw new InvalidDataException($"Data too small for the expected {_EXPECTED_FILE_SIZE}-byte MonoStar file.");

    var span = data.AsSpan();

    var resolution = (short)((span[0] << 8) | span[1]);
    if (resolution != 2)
      throw new InvalidDataException($"Invalid MonoStar resolution value: {resolution}; expected 2 (high-res).");

    var palette = new short[16];
    for (var i = 0; i < 16; ++i) {
      var offset = 2 + i * 2;
      palette[i] = (short)((span[offset] << 8) | span[offset + 1]);
    }

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.AsSpan(_HEADER_SIZE, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new MonoStarFile {
      Width = 640,
      Height = 400,
      Palette = palette,
      PixelData = pixelData,
    };
  }
}

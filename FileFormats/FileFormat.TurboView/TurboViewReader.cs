using System;
using System.IO;

namespace FileFormat.TurboView;

/// <summary>Reads Turbo View files from bytes, streams, or file paths.</summary>
public static class TurboViewReader {

  private const int _HEADER_SIZE = 34;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _EXPECTED_FILE_SIZE = _HEADER_SIZE + _PIXEL_DATA_SIZE;

  public static TurboViewFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Turbo View file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TurboViewFile FromStream(Stream stream) {
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

  public static TurboViewFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid Turbo View file.");

    if (data.Length < _EXPECTED_FILE_SIZE)
      throw new InvalidDataException($"Data too small for the expected {_EXPECTED_FILE_SIZE}-byte Turbo View file.");

    var span = data;

    var resolution = (short)((span[0] << 8) | span[1]);
    if (resolution != 0)
      throw new InvalidDataException($"Invalid Turbo View resolution value: {resolution}; expected 0 (low-res).");

    var palette = new short[16];
    for (var i = 0; i < 16; ++i) {
      var offset = 2 + i * 2;
      palette[i] = (short)((span[offset] << 8) | span[offset + 1]);
    }

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.Slice(_HEADER_SIZE, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new TurboViewFile {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = pixelData,
    };
    }

  public static TurboViewFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid Turbo View file.");

    if (data.Length < _EXPECTED_FILE_SIZE)
      throw new InvalidDataException($"Data too small for the expected {_EXPECTED_FILE_SIZE}-byte Turbo View file.");

    var span = data.AsSpan();

    var resolution = (short)((span[0] << 8) | span[1]);
    if (resolution != 0)
      throw new InvalidDataException($"Invalid Turbo View resolution value: {resolution}; expected 0 (low-res).");

    var palette = new short[16];
    for (var i = 0; i < 16; ++i) {
      var offset = 2 + i * 2;
      palette[i] = (short)((span[offset] << 8) | span[offset + 1]);
    }

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.AsSpan(_HEADER_SIZE, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new TurboViewFile {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = pixelData,
    };
  }
}

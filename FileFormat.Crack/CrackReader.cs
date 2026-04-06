using System;
using System.IO;

namespace FileFormat.Crack;

/// <summary>Reads Crack Art 2 files from bytes, streams, or file paths.</summary>
public static class CrackReader {

  private const int _HEADER_SIZE = 34;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _EXPECTED_FILE_SIZE = _HEADER_SIZE + _PIXEL_DATA_SIZE;

  public static CrackFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Crack Art 2 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CrackFile FromStream(Stream stream) {
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

  public static CrackFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static CrackFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid Crack Art 2 file.");

    if (data.Length < _EXPECTED_FILE_SIZE)
      throw new InvalidDataException($"Data too small for the expected {_EXPECTED_FILE_SIZE}-byte Crack Art 2 file.");

    var span = data.AsSpan();

    // 2-byte resolution (big-endian) - must be 0 for low-res
    var resolution = (short)((span[0] << 8) | span[1]);
    if (resolution != 0)
      throw new InvalidDataException($"Invalid Crack Art 2 resolution value: {resolution}; expected 0 (low-res).");

    // 16 palette entries (big-endian shorts)
    var palette = new short[16];
    for (var i = 0; i < 16; ++i) {
      var offset = 2 + i * 2;
      palette[i] = (short)((span[offset] << 8) | span[offset + 1]);
    }

    // 32000 bytes planar data
    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.AsSpan(_HEADER_SIZE, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new CrackFile {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = pixelData,
    };
  }
}

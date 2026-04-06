using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Spectrum512;

/// <summary>Reads Spectrum 512 (SPU) files from bytes, streams, or file paths.</summary>
public static class Spectrum512Reader {

  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _SCANLINE_COUNT = 199;
  private const int _PALETTE_ENTRIES_PER_LINE = 48;
  private const int _PALETTE_DATA_SIZE = _SCANLINE_COUNT * _PALETTE_ENTRIES_PER_LINE * 2; // 19104
  private const int _EXPECTED_FILE_SIZE = _PIXEL_DATA_SIZE + _PALETTE_DATA_SIZE; // 51104

  public static Spectrum512File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Spectrum 512 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Spectrum512File FromStream(Stream stream) {
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

  public static Spectrum512File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Spectrum512File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != _EXPECTED_FILE_SIZE)
      throw new InvalidDataException($"SPU file must be exactly {_EXPECTED_FILE_SIZE} bytes, got {data.Length}.");

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.AsSpan(0, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    var palettes = new short[_SCANLINE_COUNT][];
    var span = data.AsSpan();
    var paletteOffset = _PIXEL_DATA_SIZE;

    for (var line = 0; line < _SCANLINE_COUNT; ++line) {
      var palette = new short[_PALETTE_ENTRIES_PER_LINE];
      for (var entry = 0; entry < _PALETTE_ENTRIES_PER_LINE; ++entry) {
        var offset = paletteOffset + (line * _PALETTE_ENTRIES_PER_LINE + entry) * 2;
        palette[entry] = BinaryPrimitives.ReadInt16BigEndian(span[offset..]);
      }
      palettes[line] = palette;
    }

    return new Spectrum512File {
      Width = 320,
      Height = _SCANLINE_COUNT,
      Variant = Spectrum512Variant.Uncompressed,
      PixelData = pixelData,
      Palettes = palettes
    };
  }
}

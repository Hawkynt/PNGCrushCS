using System;
using System.Buffers.Binary;

namespace FileFormat.Spectrum512;

/// <summary>Assembles Spectrum 512 (SPU) file bytes from a Spectrum512File.</summary>
public static class Spectrum512Writer {

  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _SCANLINE_COUNT = 199;
  private const int _PALETTE_ENTRIES_PER_LINE = 48;
  private const int _PALETTE_DATA_SIZE = _SCANLINE_COUNT * _PALETTE_ENTRIES_PER_LINE * 2; // 19104
  private const int _FILE_SIZE = _PIXEL_DATA_SIZE + _PALETTE_DATA_SIZE; // 51104

  public static byte[] ToBytes(Spectrum512File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[_FILE_SIZE];
    file.PixelData.AsSpan(0, Math.Min(_PIXEL_DATA_SIZE, file.PixelData.Length)).CopyTo(result.AsSpan(0));

    var span = result.AsSpan();
    var paletteOffset = _PIXEL_DATA_SIZE;

    for (var line = 0; line < _SCANLINE_COUNT; ++line) {
      var palette = file.Palettes[line];
      for (var entry = 0; entry < _PALETTE_ENTRIES_PER_LINE; ++entry) {
        var offset = paletteOffset + (line * _PALETTE_ENTRIES_PER_LINE + entry) * 2;
        BinaryPrimitives.WriteInt16BigEndian(span[offset..], palette[entry]);
      }
    }

    return result;
  }
}

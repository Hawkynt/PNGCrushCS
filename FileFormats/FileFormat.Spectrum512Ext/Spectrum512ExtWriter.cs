using System;
using System.Buffers.Binary;

namespace FileFormat.Spectrum512Ext;

/// <summary>Assembles Spectrum 512 Extended (.spx) file bytes from a Spectrum512ExtFile.</summary>
public static class Spectrum512ExtWriter {

  private const int _PIXEL_DATA_SIZE = 32000;

  public static byte[] ToBytes(Spectrum512ExtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Spectrum512ExtFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(_PIXEL_DATA_SIZE, file.PixelData.Length)).CopyTo(result.AsSpan(0));

    var span = result.AsSpan();
    var paletteOffset = _PIXEL_DATA_SIZE;

    for (var line = 0; line < Spectrum512ExtFile.ScanlineCount; ++line) {
      var palette = file.Palettes[line];
      for (var entry = 0; entry < Spectrum512ExtFile.PaletteEntriesPerLine; ++entry) {
        var offset = paletteOffset + (line * Spectrum512ExtFile.PaletteEntriesPerLine + entry) * 2;
        BinaryPrimitives.WriteInt16BigEndian(span[offset..], palette[entry]);
      }
    }

    return result;
  }
}

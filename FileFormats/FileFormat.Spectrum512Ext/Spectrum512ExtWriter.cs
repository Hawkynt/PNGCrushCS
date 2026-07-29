using System;
using System.Buffers.Binary;

namespace FileFormat.Spectrum512Ext;

/// <summary>Assembles Spectrum 512 Extended (.spx) file bytes from a Spectrum512ExtFile.</summary>
/// <remarks>
/// The header is the ASCII tag "SPX", three method bytes (all zero here: no ICE packing on either
/// the bitmap or the palette), a title field terminated by two zero bytes, and then big-endian
/// lengths for the bitmap and palette blocks. The bitmap block opens with one unused scanline,
/// which readers skip before the picture proper.
/// </remarks>
public static class Spectrum512ExtWriter {

  public static byte[] ToBytes(Spectrum512ExtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Spectrum512ExtFile.FileSize];
    var span = result.AsSpan();

    Spectrum512ExtFile.Signature.CopyTo(span);
    // span[3..5] stay zero: no ICE packing, bitmap stored, palette stored.
    // span[10] and span[11] are the two zero bytes that close the title field.

    BinaryPrimitives.WriteInt32BigEndian(span[Spectrum512ExtFile.LengthFieldsOffset..], Spectrum512ExtFile.PixelDataSize);
    BinaryPrimitives.WriteInt32BigEndian(span[(Spectrum512ExtFile.LengthFieldsOffset + 4)..], Spectrum512ExtFile.PaletteDataSize);

    var picture = Spectrum512ExtFile.PixelDataSize - Spectrum512ExtFile.BitmapLeadIn;
    file.PixelData.AsSpan(0, Math.Min(picture, file.PixelData.Length))
      .CopyTo(span[(Spectrum512ExtFile.BitmapOffset + Spectrum512ExtFile.BitmapLeadIn)..]);

    for (var line = 0; line < Spectrum512ExtFile.ScanlineCount; ++line) {
      var palette = file.Palettes[line];
      for (var entry = 0; entry < Spectrum512ExtFile.PaletteEntriesPerLine; ++entry) {
        var offset = Spectrum512ExtFile.PaletteOffset
                   + (line * Spectrum512ExtFile.PaletteEntriesPerLine + entry) * 2;
        BinaryPrimitives.WriteInt16BigEndian(span[offset..], palette[entry]);
      }
    }

    return result;
  }
}

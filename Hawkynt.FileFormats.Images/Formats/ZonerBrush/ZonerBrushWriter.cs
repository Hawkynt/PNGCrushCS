using System;

namespace FileFormat.ZonerBrush;

/// <summary>Writes back the preview and whatever header it came with.</summary>
/// <remarks>
/// The drawing itself is not reproduced — it was never read — so this emits the preview alone. A
/// file written here is a preview that other readers can draw and not a brush anything can paint
/// with, which is the honest limit of reading only the picture out of a vector file.
/// </remarks>
public static class ZonerBrushWriter {

  public static byte[] ToBytes(ZonerBrushFile file) {
    var result = new byte[ZonerBrushFile.MinimumFileSize];

    (file.Header ?? []).AsSpan(0, Math.Min((file.Header ?? []).Length, ZonerBrushFile.PaletteOffset)).CopyTo(result);

    // A file built from a picture rather than read from one has no header to keep, and the reader
    // now requires the signature — so it is written whether one came in or not.
    if (!ZonerBrushFile.HasSignature(result))
      ZonerBrushFile.Signature.CopyTo(result.AsSpan(0));
    (file.Palette ?? []).AsSpan(0, Math.Min((file.Palette ?? []).Length, ZonerBrushFile.PaletteCount * ZonerBrushFile.PaletteEntrySize))
      .CopyTo(result.AsSpan(ZonerBrushFile.PaletteOffset));
    (file.PixelData ?? []).AsSpan(0, Math.Min((file.PixelData ?? []).Length, ZonerBrushFile.BytesPerRow * ZonerBrushFile.Height))
      .CopyTo(result.AsSpan(ZonerBrushFile.PixelOffset));

    return result;
  }
}

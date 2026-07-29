using System;

namespace FileFormat.QuantumPaint;

/// <summary>Assembles Atari ST QuantumPaint file bytes from a <see cref="QuantumPaintFile"/>.</summary>
public static class QuantumPaintWriter {

  public static byte[] ToBytes(QuantumPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[QuantumPaintFile.MinFileSize];

    // Bytes 0..3 are the mode header: three zeros then the resolution (0 = 320x200 low res).
    // Bytes 4..5 would be 128,1 for a PackBits-compressed file; leaving them zero marks this one
    // uncompressed, which is what the fixed MinFileSize length declares.
    // A single palette block at scanline 0 covers the whole screen.
    new QuantumPaintHeader(file.Palette).WriteTo(result.AsSpan(QuantumPaintFile.PaletteOffset));
    result[QuantumPaintFile.PaletteOffset + QuantumPaintFile.PaletteScanlineOffset] = 0;

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, QuantumPaintFile.PixelDataSize))
      .CopyTo(result.AsSpan(QuantumPaintFile.PixelDataOffset));

    return result;
  }
}

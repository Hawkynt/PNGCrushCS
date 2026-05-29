using System;

namespace FileFormat.QuantumPaint;

/// <summary>Assembles Atari ST QuantumPaint file bytes from a <see cref="QuantumPaintFile"/>.</summary>
public static class QuantumPaintWriter {

  public static byte[] ToBytes(QuantumPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[QuantumPaintFile.MinFileSize];

    new QuantumPaintHeader(file.Palette).WriteTo(result);

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, QuantumPaintFile.PixelDataSize))
      .CopyTo(result.AsSpan(QuantumPaintFile.PaletteSize));

    return result;
  }
}

using System;
using System.Buffers.Binary;

namespace FileFormat.QuantumPaint;

/// <summary>Assembles Atari ST QuantumPaint file bytes from a <see cref="QuantumPaintFile"/>.</summary>
public static class QuantumPaintWriter {

  public static byte[] ToBytes(QuantumPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[QuantumPaintFile.MinFileSize];
    var span = result.AsSpan();

    for (var i = 0; i < 16; ++i)
      BinaryPrimitives.WriteInt16BigEndian(span.Slice(i * 2, 2), i < file.Palette.Length ? file.Palette[i] : (short)0);

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, QuantumPaintFile.PixelDataSize))
      .CopyTo(span.Slice(QuantumPaintFile.PaletteSize));

    return result;
  }
}

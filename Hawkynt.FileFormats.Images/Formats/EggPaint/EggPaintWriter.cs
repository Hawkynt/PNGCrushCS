using System;
using System.Buffers.Binary;

namespace FileFormat.EggPaint;

/// <summary>Assembles EggPaint / TruePaint picture bytes from an <see cref="EggPaintFile"/>.</summary>
public static class EggPaintWriter {

  /// <summary>
  /// Writes the magic, the two sizes and one sixteen-bit colour per pixel.
  /// </summary>
  /// <remarks>
  /// This used to write a Commodore 64 screen, which is what the reader beside it used to expect and
  /// what no .trp has ever been.
  /// </remarks>
  public static byte[] ToBytes(EggPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixels = file.PixelData ?? [];
    var wanted = file.Width * file.Height * 2;
    var result = new byte[EggPaintFile.HeaderSize + wanted];

    EggPaintFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(6), (ushort)file.Height);
    pixels.AsSpan(0, Math.Min(pixels.Length, wanted)).CopyTo(result.AsSpan(EggPaintFile.HeaderSize));

    return result;
  }
}

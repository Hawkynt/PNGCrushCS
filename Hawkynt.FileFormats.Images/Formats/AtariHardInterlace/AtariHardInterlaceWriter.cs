using System;

namespace FileFormat.AtariHardInterlace;

/// <summary>Assembles a Hard Interlace Picture from an <see cref="AtariHardInterlaceFile"/>.</summary>
public static class AtariHardInterlaceWriter {

  /// <summary>
  /// Writes the two fields one after the other and the colour registers behind them.
  /// </summary>
  /// <remarks>
  /// Most files in the wild omit the registers and take a plain luminance ramp instead, which is the
  /// one thing a picture chosen for its colours cannot do — so they are always written. Their
  /// presence is what the reader tells the two forms apart by: nine bytes past a whole number of
  /// row pairs.
  /// </remarks>
  public static byte[] ToBytes(AtariHardInterlaceFile file) {
    var fieldSize = file.Height * AtariHardInterlaceFile.RowStride;
    var data = new byte[fieldSize * 2 + AtariHardInterlaceFile.RegisterBlockSize];

    _Copy(file.Luminances, data, 0, fieldSize);
    _Copy(file.Colors, data, fieldSize, fieldSize);
    _Copy(file.Registers, data, fieldSize * 2, AtariHardInterlaceFile.RegisterBlockSize);

    return data;
  }

  private static void _Copy(byte[]? source, byte[] target, int offset, int length) {
    if (source == null)
      return;

    source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(target.AsSpan(offset));
  }
}

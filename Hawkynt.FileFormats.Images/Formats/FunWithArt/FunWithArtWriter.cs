using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FunWithArt;

/// <summary>Assembles a Fun with Art picture from a <see cref="FunWithArtFile"/>.</summary>
public static class FunWithArtWriter {

  /// <summary>ANTIC's mode 14, which every line of the picture is drawn in.</summary>
  private const int _ANTIC_MODE = 14;

  /// <summary>The same mode with a load-scan-counter address following it.</summary>
  private const int _ANTIC_MODE_WITH_ADDRESS = 78;

  /// <summary>Set in a display list instruction when the line is to raise an interrupt.</summary>
  private const int _INTERRUPT = 128;

  /// <summary>The chip addresses the four registers are poked at, in the order the frame holds them.</summary>
  private static ReadOnlySpan<byte> _RegisterAddresses => [26, 22, 23, 24];

  /// <summary>Writes the file, which is already whole because the program saved its workspace.</summary>
  public static byte[] ToBytes(FunWithArtFile file) => (byte[])(file.Data ?? []).Clone();

  /// <summary>
  /// Builds the saved workspace around a bitmap and one set of colour registers per scanline.
  /// </summary>
  /// <remarks>
  /// The colour changes are not a table but the 6502 routines that perform them, so a row whose
  /// colours differ from the row above raises an interrupt and the routine pokes what changed. A
  /// row that changes nothing raises none, which is why the interrupt area is as long as the picture
  /// needs rather than 192 copies of the same twenty-eight bytes.
  /// <para/>
  /// The display list is in two halves because the scan counter cannot cross the boundary the
  /// picture straddles, and the bitmap skips sixteen bytes at that row for the same reason.
  /// </remarks>
  public static byte[] Assemble(ReadOnlySpan<byte> registers, ReadOnlySpan<byte> bitmap) {
    const int count = Atari8BitGraphics.Gr15RegisterCount;
    var routines = new List<byte[]>();
    var interrupts = new bool[FunWithArtFile.Height];

    for (var y = 0; y + 1 < FunWithArtFile.Height; ++y) {
      using var routine = new MemoryStream();

      for (var i = 0; i < count; ++i) {
        var value = (byte)(registers[(y + 1) * count + i] & 254);
        if (value == (registers[y * count + i] & 254))
          continue;

        // LDA #value / STA $D0nn.
        routine.WriteByte(169);
        routine.WriteByte(value);
        routine.WriteByte(141);
        routine.WriteByte(_RegisterAddresses[i]);
        routine.WriteByte(208);
      }

      if (routine.Length == 0)
        continue;

      interrupts[y] = true;

      using var whole = new MemoryStream();

      // PHA / TXA / PHA / LDA #0 / STA $D40A: save two registers, then wait for the beam.
      whole.Write([72, 138, 72, 169, 0, 141, 10, 212]);
      routine.WriteTo(whole);

      // JSR to the shared exit, which the program always assembles at the same place.
      whole.Write([32, 202, 6]);
      routines.Add(whole.ToArray());
    }

    var interruptLength = 0;
    foreach (var routine in routines)
      interruptLength += routine.Length;

    var data = new byte[FunWithArtFile.InterruptOffset + interruptLength];
    data[0] = 254;
    data[1] = 254;
    for (var i = 0; i < count; ++i)
      data[2 + i] = (byte)(registers[i] & 254);

    data[6] = 112;
    data[7] = 112;
    data[8] = 112;

    for (var y = 0; y < FunWithArtFile.Height; ++y) {
      var at = _DisplayListOffset(y);
      var loads = at is FunWithArtFile.DisplayListOffset or 113;
      data[at] = (byte)((loads ? _ANTIC_MODE_WITH_ADDRESS : _ANTIC_MODE) | (interrupts[y] ? _INTERRUPT : 0));
    }

    // The two halves each name the address they draw from; the low byte is zero in both.
    data[11] = 80;
    data[115] = 96;
    data[205] = 65;

    for (var y = 0; y < FunWithArtFile.Height; ++y) {
      var target = FunWithArtFile.BitmapOffset + FunWithArtFile.BytesPerRow * y
                   + (y >= FunWithArtFile.SplitRow ? FunWithArtFile.SplitGap : 0);
      bitmap.Slice(y * FunWithArtFile.BytesPerRow, FunWithArtFile.BytesPerRow).CopyTo(data.AsSpan(target));
    }

    data[7958] = (byte)interruptLength;
    data[7959] = (byte)(interruptLength >> 8);

    var out2 = FunWithArtFile.InterruptOffset;
    foreach (var routine in routines) {
      routine.CopyTo(data, out2);
      out2 += routine.Length;
    }

    return data;
  }

  /// <summary>Where a scanline's display list instruction sits, past the two load-address ones.</summary>
  private static int _DisplayListOffset(int y) => y switch {
    0 => FunWithArtFile.DisplayListOffset,
    < FunWithArtFile.SplitRow => 11 + y,
    FunWithArtFile.SplitRow => 113,
    _ => 13 + y,
  };
}

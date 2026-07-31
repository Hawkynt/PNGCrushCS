using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FunWithArt;

/// <summary>Reads Fun with Art pictures from bytes, streams, or file paths.</summary>
public static class FunWithArtReader {

  /// <summary>ANTIC's mode 14, which the display list uses for every line of the picture.</summary>
  private const int _ANTIC_MODE = 14;

  /// <summary>The same mode with a load-scan-counter address following it.</summary>
  private const int _ANTIC_MODE_WITH_ADDRESS = 78;

  /// <summary>Set in a display list instruction when the line is to raise an interrupt.</summary>
  private const int _INTERRUPT = 128;

  public static FunWithArtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FunWithArtFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static FunWithArtFile FromSpan(ReadOnlySpan<byte> data) {
    // The saved state has no signature, so what identifies it is that a handful of bytes of the
    // program's own workspace are always the same.
    if (data.Length < FunWithArtFile.InterruptOffset
        || data[0] != 254 || data[1] != 254
        || data[6] != 112 || data[7] != 112 || data[8] != 112
        || data[11] != 80 || data[115] != 96 || data[205] != 65
        || FunWithArtFile.InterruptOffset + data[7958] + (data[7959] << 8) != data.Length)
      throw new InvalidDataException("Not a Fun with Art picture.");

    const int registerCount = Atari8BitGraphics.Gr15RegisterCount;
    var registers = new byte[FunWithArtFile.Height * registerCount];

    // Stored background first, then the three playfield registers, which is the order the
    // Graphics 15 helpers take.
    var current = new byte[registerCount];
    for (var i = 0; i < registerCount; ++i)
      current[i] = (byte)(data[2 + i] & 254);

    var displayList = FunWithArtFile.DisplayListOffset;
    var interrupt = FunWithArtFile.InterruptOffset;

    for (var y = 0; y < FunWithArtFile.Height; ++y) {
      // The row is drawn with the colours as they stand; an interrupt on this line changes them
      // for the next.
      current.CopyTo(registers, y * registerCount);

      var instruction = data[displayList];

      // The list is in two halves, each beginning with the address of the bitmap it draws from,
      // because the picture straddles a boundary the scan counter cannot cross.
      if (displayList is FunWithArtFile.DisplayListOffset or 113) {
        if ((instruction & 127) != _ANTIC_MODE_WITH_ADDRESS || data[displayList + 1] != 0)
          throw new InvalidDataException($"Display list entry {y} does not load a bitmap address.");

        displayList += 3;
      } else {
        if ((instruction & 127) != _ANTIC_MODE)
          throw new InvalidDataException($"Display list entry {y} is not a four-colour line.");

        ++displayList;
      }

      if (instruction < _INTERRUPT)
        continue;

      interrupt = _RunInterrupt(data, interrupt, current);
    }

    return new() { Data = data.ToArray(), Registers = registers };
  }

  /// <summary>
  /// Reads one interrupt routine, applying the colours it writes, and returns where the next one
  /// starts.
  /// </summary>
  /// <remarks>
  /// The routine is fixed at both ends and free only in the middle. It opens by saving the
  /// accumulator and the X register and waiting for the beam to reach the end of the line — writing
  /// anything to WSYNC does that — and it closes by calling the program's own exit routine. In
  /// between it may only load an immediate value or store one to a colour register.
  /// </remarks>
  private static int _RunInterrupt(ReadOnlySpan<byte> data, int at, byte[] registers) {
    // PHA / TXA / PHA / LDA #n / STA $D40A — save two registers, then wait for the beam.
    if (at + 14 > data.Length
        || data[at] != 72 || data[at + 1] != 138 || data[at + 2] != 72 || data[at + 3] != 169
        || data[at + 5] != 141 || data[at + 6] != 10 || data[at + 7] != 212)
      throw new InvalidDataException("An interrupt routine does not begin the way the program writes them.");

    var accumulator = data[at + 4];
    at += 8;

    // 32 is JSR, which is how every one of these routines ends.
    while (data[at] != 32) {
      switch (data[at]) {
        // LDA #n
        case 169:
          accumulator = data[at + 1];
          at += 2;
          break;

        // STA $D0nn, the only page the routine is allowed to write.
        case 141:
          if (data[at + 2] != 208)
            throw new InvalidDataException("An interrupt routine writes outside the colour registers.");

          // 22 to 24 are PF0, PF1 and PF2 and 26 is the background; 25 is PF3, which this mode
          // cannot show, so a routine writing it is not one of the program's.
          registers[data[at + 1] switch {
            22 => 1,
            23 => 2,
            24 => 3,
            26 => 0,
            _ => throw new InvalidDataException($"An interrupt routine writes register {data[at + 1]}."),
          }] = (byte)(accumulator & 254);

          at += 3;
          break;

        default:
          throw new InvalidDataException($"An interrupt routine contains opcode {data[at]}.");
      }

      if (at + 3 > data.Length)
        throw new InvalidDataException("An interrupt routine runs past the end of the file.");
    }

    // JSR to the shared exit, whose address the program always assembles at the same place.
    if (data[at + 1] != 202 || data[at + 2] != 6)
      throw new InvalidDataException("An interrupt routine does not end at the program's exit.");

    return at + 3;
  }

  public static FunWithArtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}

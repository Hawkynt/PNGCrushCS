using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Tiny;

/// <summary>
/// The run-length coding a Tiny file uses, and the order it keeps its words in.
/// </summary>
/// <remarks>
/// What was here before invented both halves and agreed with itself: counts and values were read
/// from one interleaved stream, and no real file is arranged that way. A Tiny file keeps its control
/// bytes and its data words in two separate blocks whose lengths the header states, and the control
/// block is walked one byte at a time to say how the next words are taken from the other.
/// <para/>
/// The order is the second half of it. Tiny stores the Atari's screen memory as it stands, which is
/// always sixteen thousand words of four interleaved bitplanes whatever resolution the picture is —
/// so even a monochrome picture, which has one plane, is stored as though it had four. Within that
/// it runs down each column before moving across, so the words arrive in neither screen order nor
/// plane order.
/// <para/>
/// Both halves were settled against RECOIL on real files: all sixteen thousand words come back
/// identical to what it decodes.
/// </remarks>
internal static class TinyCompressor {

  /// <summary>Words the Atari's screen holds, which is the same 32000 bytes in every resolution.</summary>
  internal const int ScreenWords = 16000;

  /// <summary>Scanlines the screen memory is divided into.</summary>
  private const int _ROWS = 200;

  /// <summary>Words one of those lines holds.</summary>
  private const int _WORDS_PER_ROW = 80;

  /// <summary>Bitplanes the screen memory interleaves, whatever the picture's own plane count.</summary>
  private const int _PLANES = 4;

  /// <summary>Groups of interleaved planes across one line.</summary>
  private const int _GROUPS_PER_ROW = _WORDS_PER_ROW / _PLANES;

  /// <summary>Words one plane contributes to the whole screen.</summary>
  private const int _WORDS_PER_PLANE = ScreenWords / _PLANES;

  /// <summary>Where the word stored at the given position belongs on screen.</summary>
  private static int _ScreenIndexOf(int stored)
    => stored % _ROWS * _WORDS_PER_ROW
     + stored / _ROWS % _GROUPS_PER_ROW * _PLANES
     + stored / _WORDS_PER_PLANE;

  /// <summary>Expands the two blocks into the screen memory they describe.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> control, ReadOnlySpan<byte> data) {
    var stored = new short[ScreenWords];
    int controlAt = 0, dataAt = 0, at = 0;

    while (at < ScreenWords && controlAt < control.Length) {
      var code = (sbyte)control[controlAt++];

      // Zero and one say the count did not fit in a byte and follows as a word of its own;
      // a negative code is a count of words to take one after another, anything else a repeat.
      int count;
      var repeats = false;
      switch (code) {
        case < 0:
          count = -code;
          break;
        case 0:
          if (controlAt + 1 >= control.Length)
            return _ToScreen(stored);

          count = (control[controlAt] << 8) | control[controlAt + 1];
          controlAt += 2;
          repeats = true;
          break;
        case 1:
          if (controlAt + 1 >= control.Length)
            return _ToScreen(stored);

          count = (control[controlAt] << 8) | control[controlAt + 1];
          controlAt += 2;
          break;
        default:
          count = code;
          repeats = true;
          break;
      }

      if (repeats) {
        if (dataAt + 1 >= data.Length)
          break;

        var value = BinaryPrimitives.ReadInt16BigEndian(data[dataAt..]);
        dataAt += 2;
        for (var i = 0; i < count && at < ScreenWords; ++i)
          stored[at++] = value;

        continue;
      }

      for (var i = 0; i < count && at < ScreenWords; ++i) {
        if (dataAt + 1 >= data.Length)
          break;

        stored[at++] = BinaryPrimitives.ReadInt16BigEndian(data[dataAt..]);
        dataAt += 2;
      }
    }

    return _ToScreen(stored);
  }

  /// <summary>Puts the stored words where they belong on screen.</summary>
  private static byte[] _ToScreen(short[] stored) {
    var screen = new byte[ScreenWords * 2];
    for (var i = 0; i < ScreenWords; ++i)
      BinaryPrimitives.WriteInt16BigEndian(screen.AsSpan(_ScreenIndexOf(i) * 2), stored[i]);

    return screen;
  }

  /// <summary>Packs screen memory into the control and data blocks a Tiny file carries.</summary>
  public static (byte[] Control, byte[] Data) Compress(ReadOnlySpan<byte> screen) {
    var stored = new short[ScreenWords];
    for (var i = 0; i < ScreenWords; ++i) {
      var at = _ScreenIndexOf(i) * 2;
      stored[i] = at + 1 < screen.Length ? BinaryPrimitives.ReadInt16BigEndian(screen[at..]) : (short)0;
    }

    var control = new List<byte>();
    var data = new MemoryStream();

    void WriteWord(short value) {
      Span<byte> buffer = stackalloc byte[2];
      BinaryPrimitives.WriteInt16BigEndian(buffer, value);
      data.Write(buffer);
    }

    void WriteCount(int count) {
      control.Add((byte)(count >> 8));
      control.Add((byte)count);
    }

    var index = 0;
    while (index < ScreenWords) {
      var value = stored[index];
      var run = 1;
      while (index + run < ScreenWords && stored[index + run] == value)
        ++run;

      if (run >= 2) {
        // A run of two already pays for itself, one control byte against two bytes of word.
        if (run <= sbyte.MaxValue)
          control.Add((byte)run);
        else {
          control.Add(0);
          WriteCount(run);
        }

        WriteWord(value);
        index += run;
        continue;
      }

      // Otherwise gather everything up to the next run worth coding.
      var start = index;
      while (index < ScreenWords) {
        if (index + 1 < ScreenWords && stored[index] == stored[index + 1])
          break;

        ++index;
      }

      var literals = index - start;
      if (literals <= 128)
        control.Add((byte)(sbyte)-literals);
      else {
        control.Add(1);
        WriteCount(literals);
      }

      for (var i = 0; i < literals; ++i)
        WriteWord(stored[start + i]);
    }

    return (control.ToArray(), data.ToArray());
  }
}

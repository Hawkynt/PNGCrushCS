using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Tiny;

/// <summary>The Tiny Stuff two-stream run-length codec and vertical word-column transform.</summary>
internal static class TinyCompressor {

  internal const int ScreenWords = TinyFile.ScreenWordCount;
  private const int _Rows = 200;
  private const int _WordsPerRow = 80;
  private const int _Groups = 4;
  private const int _ColumnsPerGroup = _WordsPerRow / _Groups;
  private const int _WordsPerGroup = ScreenWords / _Groups;
  private const int _ExtendedMinimum = 128;
  private const int _ExtendedMaximum = 32_767;

  private static int _ScreenIndexOf(int stored)
    => stored % _Rows * _WordsPerRow
     + stored / _Rows % _ColumnsPerGroup * _Groups
     + stored / _WordsPerGroup;

  /// <summary>Strictly expands the separate Tiny Stuff control and data blocks into screen memory.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> control, ReadOnlySpan<byte> data) {
    if (control.Length is < TinyFile.MinimumControlBytes or > TinyFile.MaximumControlBytes)
      throw new InvalidDataException($"Tiny Stuff control block must contain {TinyFile.MinimumControlBytes}..{TinyFile.MaximumControlBytes} bytes.");
    if ((data.Length & 1) != 0 || data.Length is < 2 or > TinyFile.ScreenDataSize)
      throw new InvalidDataException("Tiny Stuff data block must contain 1..16000 complete big-endian words.");

    var stored = new short[ScreenWords];
    int controlAt = 0, dataAt = 0, outputAt = 0;

    while (outputAt < ScreenWords) {
      if (controlAt >= control.Length)
        throw new InvalidDataException($"Tiny Stuff control stream ended after {outputAt} of {ScreenWords} expanded words.");

      var code = unchecked((sbyte)control[controlAt++]);
      var repeat = code >= 0;
      int count;

      switch (code) {
        case < 0:
          count = -code;
          break;
        case 0:
          count = _ReadExtendedCount(control, ref controlAt, "repeat");
          break;
        case 1:
          repeat = false;
          count = _ReadExtendedCount(control, ref controlAt, "literal");
          break;
        default:
          count = code;
          break;
      }

      if (count > ScreenWords - outputAt)
        throw new InvalidDataException($"Tiny Stuff command expands past the {ScreenWords}-word screen buffer.");

      if (repeat) {
        if (dataAt > data.Length - 2)
          throw new InvalidDataException("Tiny Stuff repeat command has no data word.");

        var value = BinaryPrimitives.ReadInt16BigEndian(data[dataAt..]);
        dataAt += 2;
        stored.AsSpan(outputAt, count).Fill(value);
        outputAt += count;
        continue;
      }

      var bytes = checked(count * 2);
      if (dataAt > data.Length - bytes)
        throw new InvalidDataException($"Tiny Stuff literal command requests {count} words beyond the data block.");

      for (var i = 0; i < count; ++i) {
        stored[outputAt++] = BinaryPrimitives.ReadInt16BigEndian(data[dataAt..]);
        dataAt += 2;
      }
    }

    if (controlAt != control.Length)
      throw new InvalidDataException($"Tiny Stuff control block has {control.Length - controlAt} trailing byte(s) after the screen is complete.");
    if (dataAt != data.Length)
      throw new InvalidDataException($"Tiny Stuff data block has {(data.Length - dataAt) / 2} trailing word(s) after the screen is complete.");

    return _ToScreen(stored);
  }

  private static int _ReadExtendedCount(ReadOnlySpan<byte> control, ref int at, string kind) {
    if (at > control.Length - 2)
      throw new InvalidDataException($"Tiny Stuff extended {kind} command is truncated.");

    var count = BinaryPrimitives.ReadUInt16BigEndian(control[at..]);
    at += 2;
    if (count is < _ExtendedMinimum or > _ExtendedMaximum)
      throw new InvalidDataException($"Tiny Stuff extended {kind} count {count} is outside {_ExtendedMinimum}..{_ExtendedMaximum}.");

    return count;
  }

  private static byte[] _ToScreen(ReadOnlySpan<short> stored) {
    var screen = new byte[TinyFile.ScreenDataSize];
    for (var i = 0; i < ScreenWords; ++i)
      BinaryPrimitives.WriteInt16BigEndian(screen.AsSpan(_ScreenIndexOf(i) * 2), stored[i]);

    return screen;
  }

  /// <summary>Compresses exactly one Atari ST screen into separate control-byte and data-word blocks.</summary>
  public static (byte[] Control, byte[] Data) Compress(ReadOnlySpan<byte> screen) {
    if (screen.Length != TinyFile.ScreenDataSize)
      throw new ArgumentException($"Tiny Stuff compression requires exactly {TinyFile.ScreenDataSize} screen bytes.", nameof(screen));

    var stored = new short[ScreenWords];
    for (var i = 0; i < ScreenWords; ++i)
      stored[i] = BinaryPrimitives.ReadInt16BigEndian(screen[(_ScreenIndexOf(i) * 2)..]);

    var control = new List<byte>();
    using var data = new MemoryStream(TinyFile.ScreenDataSize);

    void WriteWord(short value) {
      Span<byte> buffer = stackalloc byte[2];
      BinaryPrimitives.WriteInt16BigEndian(buffer, value);
      data.Write(buffer);
    }

    void WriteExtendedCount(int count) {
      control.Add((byte)(count >> 8));
      control.Add((byte)count);
    }

    var index = 0;
    while (index < ScreenWords) {
      var run = 1;
      while (index + run < ScreenWords && stored[index + run] == stored[index])
        ++run;

      if (run >= 2) {
        if (run <= sbyte.MaxValue)
          control.Add((byte)run);
        else {
          control.Add(0);
          WriteExtendedCount(run);
        }

        WriteWord(stored[index]);
        index += run;
        continue;
      }

      var start = index++;
      while (index < ScreenWords) {
        if (index + 1 < ScreenWords && stored[index] == stored[index + 1])
          break;
        ++index;
      }

      var literals = index - start;
      if (literals <= 128)
        control.Add(unchecked((byte)(sbyte)-literals));
      else {
        control.Add(1);
        WriteExtendedCount(literals);
      }

      for (var i = 0; i < literals; ++i)
        WriteWord(stored[start + i]);
    }

    if (control.Count is < TinyFile.MinimumControlBytes or > TinyFile.MaximumControlBytes)
      throw new InvalidOperationException($"Tiny Stuff encoder produced an invalid {control.Count}-byte control block.");
    if ((data.Length & 1) != 0 || data.Length is < 2 or > TinyFile.ScreenDataSize)
      throw new InvalidOperationException($"Tiny Stuff encoder produced an invalid {data.Length}-byte data block.");

    return (control.ToArray(), data.ToArray());
  }
}

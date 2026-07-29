using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Imagic;

/// <summary>
/// The run-length codec Imagic wraps its screens in: a byte-oriented scheme built around one
/// escape byte chosen per file, run over the bitmap column by column.
/// </summary>
/// <remarks>
/// Anything that is not the escape byte stands for itself. The escape byte introduces a run, and
/// the byte after it says how the length is spelled — a doubled escape is the literal escape byte,
/// small values encode the length directly, and a few reserved values reach the longer forms and
/// the "run of zeros" shorthand that costs no value byte.
/// </remarks>
public static class ImagicCompressor {

  /// <summary>Bytes per row of an Atari ST screen.</summary>
  public const int BytesPerRow = 160;

  /// <summary>Size of an Atari ST screen in every resolution.</summary>
  public const int ScreenSize = 32000;

  /// <summary>Longest run the length-byte form can spell.</summary>
  private const int _MaxRun = 256;

  /// <summary>Escape sub-command introducing a length byte.</summary>
  private const int _LengthByte = 0;

  /// <summary>Escape sub-command introducing the extended length form.</summary>
  private const int _LengthExtended = 1;

  /// <summary>Escape sub-command introducing a run of zeros.</summary>
  private const int _ZeroRun = 2;

  /// <summary>
  /// Walks the screen the way the codec does: down column 0 first, then column 1, and so on.
  /// </summary>
  private static IEnumerable<int> _TraversalOrder() {
    for (var x = 0; x < BytesPerRow; ++x)
    for (var offset = x; offset < ScreenSize; offset += BytesPerRow)
      yield return offset;
  }

  /// <summary>Compresses a full screen, returning the stream and the escape byte it uses.</summary>
  public static (byte[] Data, byte Escape) Compress(ReadOnlySpan<byte> screen) {
    if (screen.Length != ScreenSize)
      throw new ArgumentException($"An Atari ST screen is {ScreenSize} bytes, got {screen.Length}.", nameof(screen));

    // Any byte works as the escape, but one the picture never uses spares us doubling it.
    var escape = _PickEscape(screen);

    var result = new List<byte>(ScreenSize / 2);
    var order = new List<int>(ScreenSize);
    order.AddRange(_TraversalOrder());

    for (var i = 0; i < order.Count;) {
      var value = screen[order[i]];
      var run = 1;
      while (i + run < order.Count && screen[order[i + run]] == value && run < _MaxRun)
        ++run;

      // A run costs four bytes to spell, so short ones stay literal — unless the value is the
      // escape byte itself, which always needs escaping.
      if (run < 4 && value != escape)
        for (var repeat = 0; repeat < run; ++repeat)
          result.Add(value);
      else if (run == 1)
        result.AddRange([escape, escape]);
      else
        result.AddRange([escape, _LengthByte, (byte)(run - 1), value]);

      i += run;
    }

    return (result.ToArray(), escape);
  }

  /// <summary>Decompresses a stream back into a full screen.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> data, byte escape) {
    var screen = new byte[ScreenSize];
    var position = 0;
    var pending = 0;
    var pendingValue = 0;

    foreach (var offset in _TraversalOrder()) {
      while (pending == 0)
        (pending, pendingValue) = _ReadCommand(data, ref position, escape);

      --pending;
      screen[offset] = (byte)pendingValue;
    }

    return screen;
  }

  private static (int Count, int Value) _ReadCommand(ReadOnlySpan<byte> data, ref int position, byte escape) {
    var b = _ReadByte(data, ref position);
    if (b != escape)
      return (1, b);

    b = _ReadByte(data, ref position);
    if (b == escape)
      return (1, b);

    switch (b) {
      case _LengthByte:
        return (_ReadByte(data, ref position) + 1, _ReadByte(data, ref position));
      case _LengthExtended:
        return (_ReadExtendedCount(data, ref position), _ReadByte(data, ref position));
      case _ZeroRun:
        return (_ReadZeroRunCount(data, ref position), 0);
      default:
        return (b + 1, _ReadByte(data, ref position));
    }
  }

  /// <summary>
  /// Reads the extended length: a chain of 0x01 bytes each worth 256, then a final byte, on top of
  /// a base of 257.
  /// </summary>
  private static int _ReadExtendedCount(ReadOnlySpan<byte> data, ref int position) {
    var count = 257;
    while (_ReadByte(data, ref position) == 1)
      count += 256;

    return count + _ReadByte(data, ref position);
  }

  private static int _ReadZeroRunCount(ReadOnlySpan<byte> data, ref int position) {
    var b = _ReadByte(data, ref position);
    switch (b) {
      case 0:
        return ScreenSize;
      case _LengthExtended:
        return _ReadExtendedCount(data, ref position);
      case _ZeroRun:
        // A terminator: everything up to the next zero byte is skipped and nothing is emitted.
        while (_ReadByte(data, ref position) > 0) {
        }

        return 0;
      default:
        return b + 1;
    }
  }

  private static byte _ReadByte(ReadOnlySpan<byte> data, ref int position) {
    if (position >= data.Length)
      throw new InvalidDataException("Imagic stream ended before the screen was complete.");

    return data[position++];
  }

  /// <summary>Picks an escape byte, preferring one the screen never contains.</summary>
  private static byte _PickEscape(ReadOnlySpan<byte> screen) {
    Span<bool> present = stackalloc bool[256];
    foreach (var b in screen)
      present[b] = true;

    // Zero, one and two are the escape sub-commands, so an escape equal to one of them would make
    // those commands unreachable.
    for (var candidate = 3; candidate < 256; ++candidate)
      if (!present[candidate])
        return (byte)candidate;

    return 0xFF;
  }
}

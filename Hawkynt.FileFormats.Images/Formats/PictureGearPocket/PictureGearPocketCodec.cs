using System;
using System.IO;

namespace FileFormat.PictureGearPocket;

/// <summary>The tile run-length coding, the system palette and the Palm calendar the format leans on.</summary>
internal static class PictureGearPocketCodec {

  /// <summary>The value the format stores in the compression field of a run-length coded image.</summary>
  internal const ushort RunLengthCoded = 1;

  /// <summary>The value Sony's software stores in the compression field of an image kept as plain pixels.</summary>
  internal const ushort Uncompressed = 0xFF;

  /// <summary>
  /// The Palm OS 8-bit system palette, as far as the format uses it: the 215 non-black colours of the
  /// six-level cube in Palm's own order, ten greys, five extras and black at 230. Copied from the
  /// table inside Sony's libprcim.dll, which is entry for entry the Palm OS default 8-bit colour
  /// table. The remaining 25 entries of the Palm table are black, and a thumbnail never names them.
  /// </summary>
  private static readonly int[] _SystemPalette = [
    0xFFFFFF, 0xFFCCFF, 0xFF99FF, 0xFF66FF, 0xFF33FF, 0xFF00FF, 0xFFFFCC, 0xFFCCCC, 0xFF99CC, 0xFF66CC, 0xFF33CC, 0xFF00CC,
    0xFFFF99, 0xFFCC99, 0xFF9999, 0xFF6699, 0xFF3399, 0xFF0099, 0xCCFFFF, 0xCCCCFF, 0xCC99FF, 0xCC66FF, 0xCC33FF, 0xCC00FF,
    0xCCFFCC, 0xCCCCCC, 0xCC99CC, 0xCC66CC, 0xCC33CC, 0xCC00CC, 0xCCFF99, 0xCCCC99, 0xCC9999, 0xCC6699, 0xCC3399, 0xCC0099,
    0x99FFFF, 0x99CCFF, 0x9999FF, 0x9966FF, 0x9933FF, 0x9900FF, 0x99FFCC, 0x99CCCC, 0x9999CC, 0x9966CC, 0x9933CC, 0x9900CC,
    0x99FF99, 0x99CC99, 0x999999, 0x996699, 0x993399, 0x990099, 0x66FFFF, 0x66CCFF, 0x6699FF, 0x6666FF, 0x6633FF, 0x6600FF,
    0x66FFCC, 0x66CCCC, 0x6699CC, 0x6666CC, 0x6633CC, 0x6600CC, 0x66FF99, 0x66CC99, 0x669999, 0x666699, 0x663399, 0x660099,
    0x33FFFF, 0x33CCFF, 0x3399FF, 0x3366FF, 0x3333FF, 0x3300FF, 0x33FFCC, 0x33CCCC, 0x3399CC, 0x3366CC, 0x3333CC, 0x3300CC,
    0x33FF99, 0x33CC99, 0x339999, 0x336699, 0x333399, 0x330099, 0x00FFFF, 0x00CCFF, 0x0099FF, 0x0066FF, 0x0033FF, 0x0000FF,
    0x00FFCC, 0x00CCCC, 0x0099CC, 0x0066CC, 0x0033CC, 0x0000CC, 0x00FF99, 0x00CC99, 0x009999, 0x006699, 0x003399, 0x000099,
    0xFFFF66, 0xFFCC66, 0xFF9966, 0xFF6666, 0xFF3366, 0xFF0066, 0xFFFF33, 0xFFCC33, 0xFF9933, 0xFF6633, 0xFF3333, 0xFF0033,
    0xFFFF00, 0xFFCC00, 0xFF9900, 0xFF6600, 0xFF3300, 0xFF0000, 0xCCFF66, 0xCCCC66, 0xCC9966, 0xCC6666, 0xCC3366, 0xCC0066,
    0xCCFF33, 0xCCCC33, 0xCC9933, 0xCC6633, 0xCC3333, 0xCC0033, 0xCCFF00, 0xCCCC00, 0xCC9900, 0xCC6600, 0xCC3300, 0xCC0000,
    0x99FF66, 0x99CC66, 0x999966, 0x996666, 0x993366, 0x990066, 0x99FF33, 0x99CC33, 0x999933, 0x996633, 0x993333, 0x990033,
    0x99FF00, 0x99CC00, 0x999900, 0x996600, 0x993300, 0x990000, 0x66FF66, 0x66CC66, 0x669966, 0x666666, 0x663366, 0x660066,
    0x66FF33, 0x66CC33, 0x669933, 0x666633, 0x663333, 0x660033, 0x66FF00, 0x66CC00, 0x669900, 0x666600, 0x663300, 0x660000,
    0x33FF66, 0x33CC66, 0x339966, 0x336666, 0x333366, 0x330066, 0x33FF33, 0x33CC33, 0x339933, 0x336633, 0x333333, 0x330033,
    0x33FF00, 0x33CC00, 0x339900, 0x336600, 0x333300, 0x330000, 0x00FF66, 0x00CC66, 0x009966, 0x006666, 0x003366, 0x000066,
    0x00FF33, 0x00CC33, 0x009933, 0x006633, 0x003333, 0x000033, 0x00FF00, 0x00CC00, 0x009900, 0x006600, 0x003300, 0x111111,
    0x222222, 0x444444, 0x555555, 0x777777, 0x888888, 0xAAAAAA, 0xBBBBBB, 0xDDDDDD, 0xEEEEEE, 0xC0C0C0, 0x800000, 0x800080,
    0x008000, 0x008080, 0x000000,
  ];

  /// <summary>How many system palette entries a thumbnail may name.</summary>
  internal static int SystemPaletteCount => _SystemPalette.Length;

  /// <summary>The system palette as 256 entries of red, green and blue; past <see cref="SystemPaletteCount"/> they are black.</summary>
  internal static byte[] SystemPaletteRgb() {
    var rgb = new byte[256 * 3];
    for (var i = 0; i < _SystemPalette.Length; ++i) {
      rgb[i * 3] = (byte)(_SystemPalette[i] >> 16);
      rgb[i * 3 + 1] = (byte)(_SystemPalette[i] >> 8);
      rgb[i * 3 + 2] = (byte)_SystemPalette[i];
    }

    return rgb;
  }

  /// <summary>Undoes a tile's run-length coding into <paramref name="target"/>, one element at a time.</summary>
  /// <remarks>
  /// An element is one byte at eight bits and one big-endian word at sixteen. Each element is copied;
  /// when the next is equal to it, that one is copied too and the byte after the pair counts further
  /// repeats. Whatever the stream does not reach keeps the value it had, which is what Sony's decoder
  /// does with a short stream, and a run that would overflow the tile is cut at its end.
  /// </remarks>
  internal static void Decode(ReadOnlySpan<byte> source, Span<byte> target, int elementSize) {
    var at = 0;
    var to = 0;
    while (at + elementSize <= source.Length && to + elementSize <= target.Length) {
      var element = source.Slice(at, elementSize);
      element.CopyTo(target[to..]);
      at += elementSize;
      to += elementSize;

      if (at + elementSize > source.Length || to + elementSize > target.Length || !source.Slice(at, elementSize).SequenceEqual(element))
        continue;

      element.CopyTo(target[to..]);
      at += elementSize;
      to += elementSize;
      if (at >= source.Length)
        break;

      var repeats = source[at++];
      for (var i = 0; i < repeats && to + elementSize <= target.Length; ++i, to += elementSize)
        element.CopyTo(target[to..]);
    }
  }

  /// <summary>Run-length codes one tile the way Sony's encoder does, so a picture always has the same coding.</summary>
  /// <remarks>
  /// The first of two equal elements is written, then the second, then the count of how many more
  /// equal ones follow, up to 255; the element after the run starts afresh. A tile of no runs at all
  /// grows by nothing, a tile of pairs by half — the format stores the coded tile either way.
  /// </remarks>
  internal static byte[] Encode(ReadOnlySpan<byte> tile, int elementSize) {
    using var output = new MemoryStream(tile.Length + tile.Length / 2 + 4);
    var at = 0;
    while (at < tile.Length) {
      var element = tile.Slice(at, elementSize);
      output.Write(element);
      at += elementSize;
      if (at >= tile.Length || !tile.Slice(at, elementSize).SequenceEqual(element))
        continue;

      output.Write(element);
      at += elementSize;
      var repeats = 0;
      while (at < tile.Length && repeats < 255 && tile.Slice(at, elementSize).SequenceEqual(element)) {
        at += elementSize;
        ++repeats;
      }

      output.WriteByte((byte)repeats);
    }

    return output.ToArray();
  }

  /// <summary>Palm OS counts time in seconds from the start of 1904, in the device's local time.</summary>
  private static readonly DateTime _PalmEpoch = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

  /// <summary>Reads a Palm date; nought means none was recorded.</summary>
  internal static DateTime FromPalmSeconds(uint seconds) => seconds == 0 ? default : _PalmEpoch.AddSeconds(seconds);

  /// <summary>Writes a Palm date; a date that was never set, or one the field cannot hold, is written as nought.</summary>
  internal static uint ToPalmSeconds(DateTime value) {
    if (value == default)
      return 0;

    var seconds = (value - _PalmEpoch).TotalSeconds;
    return seconds is < 0 or > uint.MaxValue ? 0 : (uint)seconds;
  }
}

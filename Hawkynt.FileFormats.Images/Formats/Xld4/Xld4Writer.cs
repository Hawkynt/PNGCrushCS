using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Xld4;

/// <summary>Writes XLD4/Q4 pictures.</summary>
public static class Xld4Writer {

  private const int _HEADER_SIZE = 16;
  private const int _LITERALS = 17;
  private const int _MAX_CODES = 16384;
  private const int _LENGTH_BASE = 17;
  private const int _MAX_RUN = 16 * _LENGTH_BASE + 16;
  private const int _PIXELS_PER_CHUNK = 32000;

  /// <summary>Serializes one 640x400, sixteen-colour XLD4 picture.</summary>
  public static byte[] ToBytes(Xld4File file) {
    var pixels = file.Pixels ?? throw new ArgumentException("An XLD4 picture needs pixel data.", nameof(file));
    var palette = file.Palette ?? throw new ArgumentException("An XLD4 picture needs a palette.", nameof(file));

    if (pixels.Length != Xld4File.Width * Xld4File.Height)
      throw new ArgumentException(
        $"An XLD4 picture needs exactly {Xld4File.Width * Xld4File.Height} pixels, not {pixels.Length}.", nameof(file));

    if (palette.Length < Xld4File.ColorCount * 3)
      throw new ArgumentException(
        $"An XLD4 picture needs {Xld4File.ColorCount} RGB palette entries.", nameof(file));

    for (var i = 0; i < pixels.Length; ++i)
      if (pixels[i] >= Xld4File.ColorCount)
        throw new ArgumentException($"Pixel {i} uses palette index {pixels[i]}, but XLD4 only has sixteen colours.", nameof(file));

    var result = new List<byte>(ushort.MaxValue);
    result.AddRange(new byte[_HEADER_SIZE]);
    result[2] = 2;
    result[11] = (byte)'M';
    result[12] = (byte)'A';
    result[13] = (byte)'J';
    result[14] = (byte)'Y';
    result[15] = (byte)'O';

    _AppendChunk(result, _PaletteSymbols(palette), 0);

    for (var offset = 0; offset < pixels.Length; offset += _PIXELS_PER_CHUNK) {
      var count = Math.Min(_PIXELS_PER_CHUNK, pixels.Length - offset);
      _AppendChunk(result, _RunLengthEncode(pixels.AsSpan(offset, count)), count);
    }

    if (result.Count > ushort.MaxValue)
      throw new InvalidDataException(
        $"XLD4 stores the complete file length in sixteen bits; this picture needs {result.Count} bytes after compression.");

    result[8] = (byte)result.Count;
    result[9] = (byte)(result.Count >> 8);
    return [.. result];
  }

  /// <summary>
  /// Writes the palette in the hardware-register order used by XLD4. Each channel is a four-bit
  /// value, represented twice in the stream; readers use the second copy.
  /// </summary>
  private static List<byte> _PaletteSymbols(byte[] palette) {
    var result = new List<byte>(Xld4File.ColorCount * 3 * 2);

    for (var stored = 0; stored < Xld4File.ColorCount; ++stored) {
      var logical = (stored & 8) | ((stored & 1) << 2) | ((stored >> 1) & 3);
      var at = logical * 3;

      for (var channel = 0; channel < 3; ++channel) {
        var level = (byte)((palette[at + channel] + 8) / 17);
        result.Add(level);
        result.Add(level);
      }
    }

    return result;
  }

  /// <summary>Encodes palette indices into XLD4's run-length symbol stream.</summary>
  private static List<byte> _RunLengthEncode(ReadOnlySpan<byte> pixels) {
    var result = new List<byte>(Math.Min(pixels.Length, 4096));
    var lastValue = 0;

    for (var offset = 0; offset < pixels.Length;) {
      var value = pixels[offset];
      var run = 1;
      while (offset + run < pixels.Length && pixels[offset + run] == value)
        ++run;

      offset += run;
      var left = run;

      while (left > 0) {
        if (left < 5) {
          for (; left > 0; --left)
            result.Add(value);
          continue;
        }

        if (value == lastValue && left >= _LENGTH_BASE) {
          var count = Math.Min(left, _MAX_RUN);
          result.Add(16);
          result.Add((byte)(count / _LENGTH_BASE));
          result.Add((byte)(count % _LENGTH_BASE));
          left -= count;
          continue;
        }

        var namedCount = Math.Min(left, _MAX_RUN);
        result.Add(16);
        result.Add(0);
        result.Add(value);
        result.Add((byte)(namedCount / _LENGTH_BASE));
        result.Add((byte)(namedCount % _LENGTH_BASE));
        lastValue = value;
        left -= namedCount;
      }
    }

    return result;
  }

  /// <summary>Adds one independently dictionary-coded chunk and its six-byte header.</summary>
  private static void _AppendChunk(List<byte> file, List<byte> symbols, int pixels) {
    if ((pixels & 1) != 0)
      throw new InvalidDataException("An XLD4 chunk can only describe an even number of pixels.");

    var packed = _DictionaryEncode(symbols);
    if (packed.Length > ushort.MaxValue)
      throw new InvalidDataException($"An XLD4 chunk is too large ({packed.Length} bytes).");

    var halfPixels = pixels >> 1;
    file.Add((byte)packed.Length);
    file.Add((byte)(packed.Length >> 8));
    file.Add(0);
    file.Add(0);
    file.Add((byte)halfPixels);
    file.Add((byte)(halfPixels >> 8));
    file.AddRange(packed);
  }

  /// <summary>
  /// Compresses the seventeen-symbol stream with XLD4's LZW-family dictionary. Code one widens the
  /// following code by one bit; zero terminates the chunk. Bits are emitted most-significant first.
  /// </summary>
  private static byte[] _DictionaryEncode(IReadOnlyList<byte> symbols) {
    var output = new List<byte>();
    var dictionary = new Dictionary<int, int>(_MAX_CODES - _LITERALS);
    var held = 0;
    var heldBits = 0;
    var codeBits = 3;
    var dataCodes = 0;

    void PutBits(int value, int count) {
      for (var bit = count - 1; bit >= 0; --bit) {
        held = (held << 1) | ((value >> bit) & 1);
        if (++heldBits != 8)
          continue;

        output.Add((byte)held);
        held = 0;
        heldBits = 0;
      }
    }

    void PutCode(int code) {
      if (++dataCodes >= _MAX_CODES - _LITERALS)
        throw new InvalidDataException("An XLD4 chunk needs more dictionary codes than the format permits.");

      var encoded = code + 2;
      while (encoded >= 1 << codeBits) {
        PutBits(1, codeBits);
        if (++codeBits > 15)
          throw new InvalidDataException("An XLD4 dictionary code exceeds fifteen bits.");
      }

      PutBits(encoded, codeBits);
    }

    if (symbols.Count > 0) {
      var nextCode = _LITERALS;
      var current = (int)symbols[0];

      for (var i = 1; i < symbols.Count; ++i) {
        var symbol = symbols[i];
        var key = (current << 5) | symbol;
        if (dictionary.TryGetValue(key, out var combined)) {
          current = combined;
          continue;
        }

        PutCode(current);
        if (nextCode >= _MAX_CODES)
          throw new InvalidDataException("An XLD4 dictionary grew beyond the format limit.");

        dictionary.Add(key, nextCode++);
        current = symbol;
      }

      PutCode(current);
    }

    PutBits(0, codeBits);
    if (heldBits > 0)
      output.Add((byte)(held << (8 - heldBits)));

    return [.. output];
  }
}

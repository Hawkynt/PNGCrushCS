using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.Vmd;

/// <summary>
/// VMD's own LZSS variant: a 4096-byte ring buffer addressed by an absolute twelve-bit position
/// rather than a lookback distance, seeded with either the format's fixed preload dictionary or
/// nothing but spaces, unpacking whatever the coding-method byte in front of it says is compressed.
/// </summary>
/// <remarks>
/// <b>Only the preload-dictionary form is decoded.</b> Sierra's own published description of this
/// algorithm gives two initialisations — a four-byte marker (<c>34 12 78 56</c>) that switches the
/// ring buffer's write position to <c>0x111</c> and turns on an eighteen-byte escape for a longer
/// match, or, when that marker is absent, a plain position of <c>0xFEE</c> with no escape at all — and
/// states both as the same otherwise-identical algorithm. They are not. Reproducing the marker form
/// exactly against a real intraframe was straightforward; the marker-absent form, measured against a
/// real interframe whose first several output bytes are provably wrong — the picture that byte range
/// paints does not match ffmpeg's decode even though the row coding built on top of it consumes every
/// byte cleanly and never runs off the end of anything — was not recovered by any reading tried:
/// different starting positions for the ring buffer, treating the four bytes after the length field as
/// always consumed whether or not they are the marker, and combinations of both, each checked against
/// the same real bytes, come closer without reaching exact. Nothing published states a third
/// possibility, so a marker-absent stream is refused by name rather than decoded wrong. It is also the
/// less common of the two forms in what this decoder was measured against: a marker-present stream
/// decodes every interframe of one real file outright and the marker-absent form only appears at all
/// in one of six, where it accounts for well under half that file's own frames.
/// </remarks>
internal static class VmdLzDecoder {

  private const int _WINDOW_SIZE = 4096;
  private const int _WINDOW_MASK = _WINDOW_SIZE - 1;
  private const byte _INITIAL_FILL = 0x20;
  private const int _MARKER_LENGTH = 4;
  private const int _DATA_LEFT_LENGTH = 4;
  private const int _PRELOAD_QUEUE_POSITION = 0x111;
  private const int _NO_PRELOAD_QUEUE_POSITION = 0xFEE;
  private const int _MINIMUM_CHAIN_LENGTH = 3;
  private const int _CHAIN_LENGTH_BITS = 4;
  private const byte _CHAIN_LENGTH_MASK = (1 << _CHAIN_LENGTH_BITS) - 1;
  private const int _SPECIAL_CHAIN_LENGTH = 18; // the escape's own threshold, only reachable with the preload marker
  private const byte _EIGHT_LITERAL_TAG = 0xFF;
  private const int _EIGHT_LITERAL_COUNT = 8;

  private static readonly byte[] _PreloadMarker = [0x34, 0x12, 0x78, 0x56];

  /// <summary>Whether the four bytes right after the output length state the preload marker this
  /// decoder requires — checked once by the caller so it can refuse a marker-absent stream by name
  /// before doing any decompression at all.</summary>
  internal static bool HasPreloadMarker(ReadOnlySpan<byte> input)
    => input.Length >= _DATA_LEFT_LENGTH + _MARKER_LENGTH && input.Slice(_DATA_LEFT_LENGTH, _MARKER_LENGTH).SequenceEqual(_PreloadMarker);

  /// <summary>Decompresses a marker-present VMD LZ chunk in full.</summary>
  internal static byte[] Decode(ReadOnlySpan<byte> input) {
    if (input.Length < _DATA_LEFT_LENGTH + _MARKER_LENGTH)
      throw new InvalidDataException($"A VMD LZ chunk is {input.Length} bytes, short of the eight its own output length and preload marker need.");

    var outputLength = BinaryPrimitives.ReadUInt32LittleEndian(input);
    var output = new byte[outputLength];
    var outputPosition = 0;

    var queue = new byte[_WINDOW_SIZE];
    Array.Fill(queue, _INITIAL_FILL);
    var queuePosition = _PRELOAD_QUEUE_POSITION;

    var inputPosition = _DATA_LEFT_LENGTH + _MARKER_LENGTH;

    while (outputPosition < output.Length) {
      var tag = _ReadByte(input, ref inputPosition);

      if (tag == _EIGHT_LITERAL_TAG && output.Length - outputPosition > _EIGHT_LITERAL_COUNT) {
        for (var i = 0; i < _EIGHT_LITERAL_COUNT; ++i) {
          var b = _ReadByte(input, ref inputPosition);
          output[outputPosition++] = b;
          queue[queuePosition] = b;
          queuePosition = (queuePosition + 1) & _WINDOW_MASK;
        }

        continue;
      }

      for (var bit = 0; bit < 8 && outputPosition < output.Length; ++bit) {
        if (((tag >> bit) & 1) != 0) {
          var b = _ReadByte(input, ref inputPosition);
          output[outputPosition++] = b;
          queue[queuePosition] = b;
          queuePosition = (queuePosition + 1) & _WINDOW_MASK;
          continue;
        }

        var b0 = _ReadByte(input, ref inputPosition);
        var b1 = _ReadByte(input, ref inputPosition);
        var chainOffset = b0 | ((b1 & 0xF0) << 4);
        var chainLength = (b1 & _CHAIN_LENGTH_MASK) + _MINIMUM_CHAIN_LENGTH;
        if (chainLength == _SPECIAL_CHAIN_LENGTH)
          chainLength = _SPECIAL_CHAIN_LENGTH + _ReadByte(input, ref inputPosition);

        if (outputPosition + chainLength > output.Length)
          throw new InvalidDataException(
            $"A VMD LZ back-reference at output byte {outputPosition} asks for {chainLength} bytes, "
            + $"which runs past the chunk's own declared output length of {output.Length}.");

        for (var i = 0; i < chainLength; ++i) {
          var b = queue[(chainOffset + i) & _WINDOW_MASK];
          output[outputPosition++] = b;
          queue[queuePosition] = b;
          queuePosition = (queuePosition + 1) & _WINDOW_MASK;
        }
      }
    }

    return output;
  }

  private static byte _ReadByte(ReadOnlySpan<byte> input, ref int position) {
    if (position >= input.Length)
      throw new InvalidDataException($"A VMD LZ chunk ran out of input at byte {position} before its declared output length was reached.");

    return input[position++];
  }
}

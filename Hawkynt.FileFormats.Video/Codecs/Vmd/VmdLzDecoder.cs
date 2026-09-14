using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.Vmd;

/// <summary>Decodes the two LZSS initialisations used by Sierra VMD video.</summary>
/// <remarks>
/// Converted from FFmpeg's LGPL-2.1-or-later <c>libavcodec/vmdvideo.c</c>; provenance and licence are
/// recorded in <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> beside this file. VMD addresses a 4096-byte ring
/// buffer by absolute twelve-bit position. Streams beginning with <c>34 12 78 56</c> start writing at
/// <c>0x111</c> and extend an eighteen-byte match with one extra length byte. Streams without that
/// marker start at <c>0xFEE</c> and have no extended-length escape.
/// </remarks>
internal static class VmdLzDecoder {

  private const int _WINDOW_SIZE = 4096;
  private const int _WINDOW_MASK = _WINDOW_SIZE - 1;
  private const byte _INITIAL_FILL = 0x20;
  private const int _LENGTH_FIELD_SIZE = 4;
  private const int _MARKER_SIZE = 4;
  private const int _PRELOAD_POSITION = 0x111;
  private const int _PLAIN_POSITION = 0xFEE;
  private const int _MINIMUM_MATCH = 3;
  private const int _EXTENDED_MATCH = 18;
  private const int _EIGHT_LITERAL_COUNT = 8;
  private const byte _EIGHT_LITERAL_TAG = 0xFF;

  private static readonly byte[] _PreloadMarker = [0x34, 0x12, 0x78, 0x56];

  internal static bool HasPreloadMarker(ReadOnlySpan<byte> input)
    => input.Length >= _LENGTH_FIELD_SIZE + _MARKER_SIZE
       && input.Slice(_LENGTH_FIELD_SIZE, _MARKER_SIZE).SequenceEqual(_PreloadMarker);

  /// <summary>Expands one VMD LZ chunk and requires its own declared output length to fit the supplied limit.</summary>
  internal static byte[] Decode(ReadOnlySpan<byte> input, int maximumOutputLength = int.MaxValue) {
    if (input.Length < _LENGTH_FIELD_SIZE)
      throw new InvalidDataException(
        $"A VMD LZ chunk is {input.Length} bytes, short of its four-byte decompressed-length field.");

    var outputLength = BinaryPrimitives.ReadUInt32LittleEndian(input);
    if (outputLength > int.MaxValue || outputLength > maximumOutputLength)
      throw new InvalidDataException(
        $"A VMD LZ chunk declares {outputLength} decompressed bytes, beyond the permitted {maximumOutputLength}.");

    var marker = HasPreloadMarker(input);
    var inputPosition = _LENGTH_FIELD_SIZE + (marker ? _MARKER_SIZE : 0);
    var queuePosition = marker ? _PRELOAD_POSITION : _PLAIN_POSITION;
    var extendedMatch = marker;

    var output = new byte[(int)outputLength];
    var outputPosition = 0;
    var queue = new byte[_WINDOW_SIZE];
    Array.Fill(queue, _INITIAL_FILL);

    while (outputPosition < output.Length) {
      var tag = _ReadByte(input, ref inputPosition);

      if (tag == _EIGHT_LITERAL_TAG && output.Length - outputPosition > _EIGHT_LITERAL_COUNT) {
        for (var i = 0; i < _EIGHT_LITERAL_COUNT; ++i)
          _WriteLiteral(_ReadByte(input, ref inputPosition), output, ref outputPosition, queue, ref queuePosition);
        continue;
      }

      for (var bit = 0; bit < 8 && outputPosition < output.Length; ++bit) {
        if (((tag >> bit) & 1) != 0) {
          _WriteLiteral(_ReadByte(input, ref inputPosition), output, ref outputPosition, queue, ref queuePosition);
          continue;
        }

        var low = _ReadByte(input, ref inputPosition);
        var highAndLength = _ReadByte(input, ref inputPosition);
        var offset = low | ((highAndLength & 0xF0) << 4);
        var length = (highAndLength & 0x0F) + _MINIMUM_MATCH;
        if (extendedMatch && length == _EXTENDED_MATCH)
          length += _ReadByte(input, ref inputPosition);

        if (outputPosition + length > output.Length)
          throw new InvalidDataException(
            $"A VMD LZ back-reference at output byte {outputPosition} asks for {length} bytes, "
            + $"past the declared output length of {output.Length}.");

        for (var i = 0; i < length; ++i) {
          var value = queue[(offset + i) & _WINDOW_MASK];
          output[outputPosition++] = value;
          queue[queuePosition] = value;
          queuePosition = (queuePosition + 1) & _WINDOW_MASK;
        }
      }
    }

    return output;
  }

  private static void _WriteLiteral(byte value, byte[] output, ref int outputPosition, byte[] queue, ref int queuePosition) {
    output[outputPosition++] = value;
    queue[queuePosition] = value;
    queuePosition = (queuePosition + 1) & _WINDOW_MASK;
  }

  private static byte _ReadByte(ReadOnlySpan<byte> input, ref int position) {
    if ((uint)position >= (uint)input.Length)
      throw new InvalidDataException(
        $"A VMD LZ chunk ran out of input at byte {position} before its declared output length was reached.");

    return input[position++];
  }
}

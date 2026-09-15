using System;
using FileFormat.Codecs.H263.Tests;
using FileFormat.Core;

namespace FileFormat.Codecs.RealVideo.Tests;

/// <summary>Writes RealVideo 1 pictures a bit at a time over their H.263 macroblock layer.</summary>
internal static class RealVideoTestStream {

  /// <summary>The private data naming the baseline RV10 bitstream implemented here.</summary>
  internal static byte[] Revision0 => [0, 0, 0, 8, 0x10, 0, 0, 0];

  /// <summary>Private data naming an RV10 micro version.</summary>
  internal static byte[] Micro(int micro) => [0, 0, 0, 8, 0x10, 0, (byte)(micro << 4), 0];

  internal static H263TestStream Picture(
    bool isIntra, int quantiser, (int Column, int Row, int Count)? position = null, bool isPbFrame = false) {
    var stream = new H263TestStream();
    stream.Bits(1, 1);
    stream.Bits(isIntra ? 0 : 1, 1);
    stream.Bits(isPbFrame ? 1 : 0, 1);
    stream.Bits(quantiser, 5);

    if (position == null) {
      stream.Bits(0xFFF, 12);
      return stream;
    }

    var (column, row, count) = position.Value;
    stream.Bits(column, 6);
    stream.Bits(row, 6);
    stream.Bits(count, 12);
    stream.Bits(0, 3);
    return stream;
  }

  internal static MediaStreamInfo Stream(
    string fourCharacterCode, int width = 176, int height = 144, byte[]? codecPrivateData = null)
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(fourCharacterCode),
      Width = width,
      Height = height,
      TimeBase = new(1, 1000),
      CodecPrivateData = codecPrivateData ?? Revision0,
    };

  internal static CodedPacket Packet(params byte[][] runs) {
    ArgumentNullException.ThrowIfNull(runs);

    var total = 0;
    var offsets = new int[runs.Length];
    for (var i = 0; i < runs.Length; ++i) {
      offsets[i] = total;
      total += runs[i].Length;
    }

    var joined = new byte[total];
    for (var i = 0; i < runs.Length; ++i)
      runs[i].CopyTo(joined, offsets[i]);

    return new(0, joined, 0, IsKeyFrame: true, FragmentOffsets: offsets);
  }
}
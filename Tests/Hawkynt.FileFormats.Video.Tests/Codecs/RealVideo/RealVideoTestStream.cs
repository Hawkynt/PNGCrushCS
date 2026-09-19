using System;
using FileFormat.Codecs.H263.Tests;
using FileFormat.Core;

namespace FileFormat.Codecs.RealVideo.Tests;

/// <summary>Writes RealVideo 1 pictures a bit at a time over their H.263 macroblock layer.</summary>
internal static class RealVideoTestStream {

  /// <summary>The private data naming the baseline RV10 bitstream implemented here.</summary>
  internal static byte[] Revision0 => Version(0, 0);

  /// <summary>Private data naming an RV10 micro version.</summary>
  internal static byte[] Micro(int micro) => Version(0, micro);

  /// <summary>Private data naming an RV10 minor/micro version and its motion-vector mode.</summary>
  internal static byte[] Version(int minor, int micro, bool longVectors = false) {
    var version = 0x10000000u | ((uint)(minor & 0xFF) << 20) | ((uint)(micro & 0xFF) << 12);
    return [
      0, 0, 0, (byte)(8 | (longVectors ? 1 : 0)),
      (byte)(version >> 24), (byte)(version >> 16), (byte)(version >> 8), (byte)version,
    ];
  }

  internal static H263TestStream Picture(
    bool isIntra, int quantiser, (int Column, int Row, int Count)? position = null, bool isPbFrame = false,
    (int Luma, int Cb, int Cr)? initialDc = null) {
    var stream = new H263TestStream();
    stream.Bits(1, 1);
    stream.Bits(isIntra ? 0 : 1, 1);
    stream.Bits(isPbFrame ? 1 : 0, 1);
    stream.Bits(quantiser, 5);

    if (initialDc is { } dc) {
      stream.Bits(dc.Luma, 8);
      stream.Bits(dc.Cb, 8);
      stream.Bits(dc.Cr, 8);
    }

    if (position == null) {
      // No position/count pair means the first run covers the whole picture. Every RV10 run still
      // ends its header with the same three ignored bits.
      stream.Bits(0, 3);
      return stream;
    }

    var (column, row, count) = position.Value;
    stream.Bits(column, 6);
    stream.Bits(row, 6);
    stream.Bits(count, 12);
    stream.Bits(0, 3);
    return stream;
  }

  /// <summary>
  /// One flat intra macroblock using RealVideo's predictive DC syntax.
  /// </summary>
  /// <param name="firstInRun">
  /// Whether this is the first macroblock of the run. The first Y, Cb and Cr blocks consume their
  /// seed from the run header instead of a VLC; every later block uses the supplied difference code.
  /// </param>
  internal static H263TestStream FlatPredictiveIntraMacroblock(
    this H263TestStream stream, bool firstInRun, string lumaDifference = "00", string chromaDifference = "00") {
    stream.Code(H263TestStream.IntraMacroblock).Code(H263TestStream.NoLuminanceCoded);

    for (var block = firstInRun ? 1 : 0; block < 4; ++block)
      stream.Code(lumaDifference);

    if (!firstInRun)
      stream.Code(chromaDifference).Code(chromaDifference);

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
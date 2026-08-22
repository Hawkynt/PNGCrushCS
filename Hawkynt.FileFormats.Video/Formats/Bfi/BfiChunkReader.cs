using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Bfi;

/// <summary>
/// Splits a BFI file — Tsunami Media's "Brute Force &amp; Ignorance", the video format behind
/// <i>Blue Force</i> and <i>Flash Traffic: City of Angels</i> — into its 960-byte header, the palette it
/// carries, and the flat run of <c>IVAS</c> chunks that follow, each one holding a frame's own PCM audio
/// alongside its coded picture.
/// </summary>
/// <remarks>
/// The header, the chunk framing and the video compression scheme are stated in MultimediaWiki's BFI
/// page, whose acronym expansion it quotes verbatim from the README.TXT of <i>Flash Traffic: City of
/// Angels</i> itself — a first-party source for the one fact that is not a byte offset — and whose byte
/// tables mark several fields "unknown" or "(?)" rather than naming them with the confidence a paraphrase
/// of a working decoder would. Nothing on the page cites an implementation.
/// <para/>
/// Only the header, the palette and the chunk framing are this reader's concern; <see
/// cref="Codecs.BfiVideoDecoder"/> is the only place a compression code is read.
/// </remarks>
internal static class BfiChunkReader {

  internal const int HeaderLength = 960;
  private const int _PALETTE_OFFSET = 60;
  private const int _PALETTE_LENGTH = 256 * 3;

  internal static bool LooksPlausible(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[0] == (byte)'B' && header[1] == (byte)'F' && header[2] == (byte)'&' && header[3] == (byte)'I';

  internal readonly record struct RawFrame(int Offset, int Length);

  private static IEnumerable<RawFrame> _WalkFrames(ReadOnlyMemory<byte> data, int firstFrameOffset, int frameCount) {
    var length = data.Length;
    var pos = firstFrameOffset;
    var count = 0;

    while (count < frameCount && pos + 8 <= length) {
      if (data.Span[pos] != (byte)'I' || data.Span[pos + 1] != (byte)'V' || data.Span[pos + 2] != (byte)'A' || data.Span[pos + 3] != (byte)'S')
        yield break;

      var chunkSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(pos + 4)..]);
      if (chunkSize < 8 || pos + chunkSize > length)
        yield break;

      yield return new(pos, chunkSize);
      pos += chunkSize;
      ++count;
    }
  }

  internal static BfiContainer Open(ReadOnlyMemory<byte> data) {
    if (data.Length < HeaderLength)
      throw new NotSupportedException(
        $"The file is {data.Length} bytes, short of the 960-byte header a BFI file opens with. This is "
        + "not a BFI file.");

    var header = data.Span[..HeaderLength];
    if (!LooksPlausible(header))
      throw new NotSupportedException("This file does not open with the four bytes 'BF&I'. This is not a BFI file.");

    var firstFrameOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
    var frameCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[44..]);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[48..]);
    var sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[828..]);
    var channels = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[832..]);

    if (width <= 0 || width > 4096 || height <= 0 || height > 4096)
      throw new NotSupportedException($"This BFI file's header states a picture of {width}x{height}, which is not plausible.");

    var palette = data.Slice(_PALETTE_OFFSET, _PALETTE_LENGTH);

    var frames = new List<RawFrame>();
    foreach (var frame in _WalkFrames(data, firstFrameOffset, frameCount))
      frames.Add(frame);

    return new() {
      Data = data,
      Width = width,
      Height = height,
      Palette = palette,
      FrameCount = frames.Count,
      SampleRate = sampleRate,
      Channels = channels is 1 or 2 ? channels : 1,
    };
  }

  internal static IEnumerable<CodedPacket> ReadPackets(BfiContainer container) {
    var data = container.Data;
    var header = data.Span[..HeaderLength];
    var firstFrameOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);

    long frame = 0;
    foreach (var raw in _WalkFrames(data, firstFrameOffset, container.FrameCount)) {
      yield return new(
        StreamIndex: 0,
        Data: data.Slice(raw.Offset, raw.Length),
        PresentationTimestamp: frame,
        DecodeTimestamp: frame,
        IsKeyFrame: frame == 0);

      // Audio, where the chunk's own offsets state a non-empty span of it — the video offset is where
      // the audio data ends, since both are counted from the chunk's own start.
      var payload = data.Slice(raw.Offset + 8, raw.Length - 8);
      if (payload.Length >= 16) {
        var audioOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Span[4..]) - 8;
        var videoOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Span[12..]) - 8;
        if (audioOffset is >= 0 && videoOffset > audioOffset && videoOffset <= payload.Length)
          yield return new(
            StreamIndex: 1,
            Data: payload.Slice(audioOffset, videoOffset - audioOffset),
            PresentationTimestamp: frame,
            IsKeyFrame: true);
      }

      ++frame;
    }
  }
}

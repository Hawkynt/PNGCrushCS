using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FlicVideo;

/// <summary>
/// Splits an Autodesk FLIC file into the frame chunks it is made of, without reading a single
/// opcode inside any of them.
/// </summary>
/// <remarks>
/// FLIC has no separate container layer the way an AVI or a Matroska has one — a file is its own
/// codec's bitstream, laid out as a 128-byte header and then a run of <c>FRAME_TYPE</c> chunks. What
/// this reader takes from that is exactly the part that is a demuxer's job regardless: where each
/// frame begins and ends. <see cref="Split"/> reads the four-byte size in front of every frame chunk
/// and nothing past it, and hands the rest of each chunk over untouched — every palette packet, every
/// delta opcode and every byte-run stays inside the packet, because reading any of it here would be
/// the codec's work done twice.
/// <para/>
/// Two things a naive walk gets wrong and both are handled here rather than left for the decoder to
/// discover the hard way.
/// <para/>
/// The first is where frame one actually starts. An <c>.fli</c> file (magic <c>0xAF11</c>) has no
/// field for it — the format assumes frame one sits directly behind the header — but an <c>.flc</c>
/// file (magic <c>0xAF12</c>) carries <c>oframe1</c>, and at least one file in the wild
/// (<c>2422.FLC</c>, from ffmpeg's own sample corpus) uses it for real: a 2778-byte
/// <c>PREFIX_TYPE</c> chunk of undocumented settings sits between the header and the first frame, and
/// a reader that assumed "right after the header" would try to decode it as one. <c>oframe1</c> is
/// trusted whenever the header states one.
/// <para/>
/// The second is the ring frame. A <c>.fli</c>'s last frame is not a picture of the film — it is a
/// delta back to frame one, written so a player can loop without paying to re-decode the
/// run-length-coded first frame. Every one of eleven clean samples pulled from ffmpeg's own corpus
/// carries exactly one more <c>FRAME_TYPE</c> chunk than the header's <c>frames</c> field states, with
/// zero bytes left over afterwards — ffmpeg's own frame count confirms it by being one higher again.
/// <see cref="Split"/> stops after exactly <c>frames</c> chunks, so that ring frame is never handed
/// out as a packet: an ordinary caller asking for the pictures of the film gets the film and not one
/// more frame that only exists to shorten the next loop's first redraw.
/// </remarks>
internal static class FliReader {

  /// <summary>The header magic of the original Autodesk Animator format.</summary>
  internal const ushort MAGIC_FLI = 0xAF11;

  /// <summary>
  /// The header magic of Autodesk Animator Pro's format, also used by an eight-bit-deep <c>.flx</c>.
  /// </summary>
  /// <remarks>
  /// A <c>.flx</c> carrying a colour depth other than eight is a different, undocumented-by-Autodesk
  /// bitstream under its own magic (<c>0xAF44</c>) with its own chunk types — not this format with a
  /// wider sample, and not read here. This library is paletted eight-bit throughout, and a file
  /// stating any other magic is refused by name before a single frame is looked at.
  /// </remarks>
  internal const ushort MAGIC_FLC = 0xAF12;

  internal const int HEADER_SIZE = 128;
  private const int _FRAME_HEADER_SIZE = 16;
  private const ushort _FRAME_MAGIC = 0xF1FA;

  internal readonly record struct Header(
    ushort Magic,
    ushort FrameCount,
    int Width,
    int Height,
    ushort Depth,
    uint Speed,
    int FirstFrameOffset);

  internal static FliContainer Open(ReadOnlyMemory<byte> data) {
    var header = ReadHeader(data.Span);
    return new() {
      Data = data,
      Magic = header.Magic,
      Width = header.Width,
      Height = header.Height,
      FrameCount = header.FrameCount,
      Speed = header.Speed,
      FirstFrameOffset = header.FirstFrameOffset,
    };
  }

  internal static Header ReadHeader(ReadOnlySpan<byte> data) {
    if (data.Length < HEADER_SIZE)
      throw new InvalidDataException(
        $"A FLIC file is {data.Length} bytes, short of the 128-byte header every one of them opens with.");

    var magic = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    if (magic is not (MAGIC_FLI or MAGIC_FLC))
      throw new NotSupportedException(
        $"The file states magic 0x{magic:X4} at offset 4. Only 0x{MAGIC_FLI:X4} (.fli) and 0x{MAGIC_FLC:X4} "
        + "(.flc, and an eight-bit .flx) are read; every other FLIC-family magic — the Huffman/BWT form "
        + "(0xAF30), the frame-shift form (0xAF31), and DTA's non-eight-bit form (0xAF44) — is a different, "
        + "undocumented bitstream under a shared file extension and is refused rather than guessed at.");

    var frameCount = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
    var depth = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
    var speed = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);

    if (width <= 0 || height <= 0)
      throw new InvalidOperationException(
        $"The file states a picture of {width}x{height}, which has no pixels.");

    if (depth != 8)
      throw new NotSupportedException(
        $"The file states {depth} bits per pixel. This library's FLIC codec is paletted eight-bit "
        + "throughout — the depth every file under magic 0xAF11 or 0xAF12 carries — and nothing else is read.");

    // oframe1 exists only in the FLC-shaped header (.fli's byte 80 is reserved and always zero in
    // practice, which happens to agree with "no prefix chunk" — the one answer that is also correct
    // for every .fli file, since the format has no field to say otherwise).
    var oframe1 = magic == MAGIC_FLC ? BinaryPrimitives.ReadUInt32LittleEndian(data[80..]) : 0u;
    var firstFrameOffset = oframe1 != 0 ? (int)oframe1 : HEADER_SIZE;

    if (firstFrameOffset < HEADER_SIZE || firstFrameOffset > data.Length)
      throw new InvalidDataException(
        $"The file's oframe1 field states the first frame begins at byte {firstFrameOffset}, which is "
        + $"{(firstFrameOffset < HEADER_SIZE ? "inside the 128-byte header" : $"past the file's {data.Length} bytes")}.");

    return new(magic, frameCount, width, height, depth, speed, firstFrameOffset);
  }

  /// <summary>
  /// Walks exactly <see cref="Header.FrameCount"/> <c>FRAME_TYPE</c> chunks from
  /// <see cref="Header.FirstFrameOffset"/>, handing out each one's sub-chunks as a packet.
  /// </summary>
  internal static IEnumerable<CodedPacket> Split(FliContainer container) {
    var data = container.Data;
    var offset = container.FirstFrameOffset;

    // .fli states its delay in 1/70-second ticks and .flc in milliseconds; both are carried in the
    // stream's own time base rather than converted, so a duration is exact rather than rounded.
    long presentation = 0;

    for (var frame = 0; frame < container.FrameCount; ++frame) {
      if (offset + _FRAME_HEADER_SIZE > data.Length)
        throw new InvalidDataException(
          $"Frame {frame} of {container.FrameCount} would start at byte {offset}, past the file's "
          + $"{data.Length} bytes. The header promises more frames than the file holds.");

      var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[offset..]);
      var magic = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(offset + 4)..]);
      if (magic != _FRAME_MAGIC)
        throw new InvalidDataException(
          $"Frame {frame} at byte {offset} states magic 0x{magic:X4} where a FRAME_TYPE chunk states "
          + $"0x{_FRAME_MAGIC:X4}. The frame chunks no longer line up, so nothing after this one can be "
          + "trusted to be a frame either.");

      if (size < _FRAME_HEADER_SIZE || offset + size > data.Length)
        throw new InvalidDataException(
          $"Frame {frame} at byte {offset} states a size of {size} bytes, which "
          + (size < _FRAME_HEADER_SIZE ? "is shorter than a frame chunk's own 16-byte header." : "runs past the end of the file."));

      var delay = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(offset + 8)..]);
      var widthOverride = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(offset + 12)..]);
      var heightOverride = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(offset + 14)..]);
      if (widthOverride != 0 || heightOverride != 0)
        throw new NotSupportedException(
          $"Frame {frame} at byte {offset} states a picture size override of {widthOverride}x{heightOverride}. "
          + "A FLIC frame changing the picture size midstream is not implemented — no sample this was built "
          + "against uses it — and reading the rest of the file against a size the frame itself contradicts "
          + "would be a guess.");

      var duration = delay != 0 ? delay : container.Speed;
      var payload = data.Slice(offset + _FRAME_HEADER_SIZE, (int)size - _FRAME_HEADER_SIZE);
      var isKeyFrame = _CarriesWholeFramePicture(payload.Span);

      yield return new(
        StreamIndex: 0,
        Data: payload,
        PresentationTimestamp: presentation,
        DecodeTimestamp: presentation,
        Duration: duration,
        IsKeyFrame: isKeyFrame);

      presentation += duration;
      offset += (int)size;
    }
  }

  /// <summary>
  /// Whether a frame's sub-chunks include a whole-frame picture chunk — <c>BLACK</c>, <c>BRUN</c> or
  /// <c>COPY</c> — which is what makes a frame decodable without anything before it.
  /// </summary>
  /// <remarks>
  /// A structural question about which chunk types are present, answered by reading each sub-chunk's
  /// six-byte header and skipping its payload — not a decode of any of them. Delta chunks
  /// (<c>SS2</c>, <c>LC</c>) and palette chunks carry no such guarantee, since both depend on the
  /// canvas or the palette a previous frame left behind.
  /// </remarks>
  private static bool _CarriesWholeFramePicture(ReadOnlySpan<byte> subChunks) {
    var at = 0;
    while (at + 6 <= subChunks.Length) {
      var size = BinaryPrimitives.ReadUInt32LittleEndian(subChunks[at..]);
      var type = BinaryPrimitives.ReadUInt16LittleEndian(subChunks[(at + 4)..]);
      if (type is FliChunkType.BLACK or FliChunkType.BRUN or FliChunkType.COPY)
        return true;

      if (size < 6 || at + size > subChunks.Length)
        return false;

      at += (int)size;
    }

    return false;
  }
}

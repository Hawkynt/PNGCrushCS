using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Anim;

/// <summary>
/// Splits an IFF <c>FORM ANIM</c> file — the Amiga's own animation format, layered on top of the same
/// IFF ILBM still picture format this library already reads — into the flat run of <c>FORM ILBM</c>
/// sub-forms it holds, each one whole frame's worth of chunks: an animation header, a palette, and
/// either a complete picture (the first frame) or the coded difference from an earlier one.
/// </summary>
/// <remarks>
/// Unlike <see cref="Hawkynt.FileFormats.Images"/>'s own <c>FileFormat.IffAnim</c> reader — which opens
/// an ANIM file only to show its first frame as a still picture, the one thing an ANIM shares with an
/// ordinary ILBM — this walks every frame. It does not call into that reader or into <c>FileFormat.Iff</c>'s
/// generic chunk parser: both build a tree of every chunk's bytes copied out of the file, which is the
/// right shape for a still picture and the wrong one for a container whose packets are meant to be
/// windows onto the file rather than copies of it. What this does instead is find where each top-level
/// <c>FORM ILBM</c> begins and ends and hand that whole span out as one packet — the generic parser is
/// used again inside <c>AnimVideoDecoder</c>, on one packet at a time, where a frame's few hundred bytes
/// of BMHD, ANHD, CMAP and BODY or DLTA chunks are exactly the tree it is for.
/// <para/>
/// The header fields, the frame sequence and the double-buffering scheme this format assumes are stated
/// in the original ANIM specification, "An IFF Format For CEL Animations" by Gary Bonham of Sparta Inc.
/// and Aegis Development, together with the later Anim6, Anim7 and Anim8 addenda by their own named
/// authors (William Coldwell, Wolfgang Hofer and Joe Porkka) and Dan Silva's account of DPaint's own
/// "Anim Brush" variant — all first-party descriptions of formats their authors built, mirrored in full
/// at <c>wiki.amigaos.net/wiki/ANIM_IFF_CEL_Animations</c>. This reader only needs the outer shape the
/// specification gives in full: a <c>FORM ANIM</c> is a flat run of <c>FORM ILBM</c>s, and nothing about
/// finding where one ends and the next begins differs from finding the same for an ordinary IFF file.
/// </remarks>
internal static class AnimChunkReader {

  private static bool _Is(ReadOnlySpan<byte> data, int offset, string tag) {
    if (offset + 4 > data.Length)
      return false;

    return data[offset] == tag[0] && data[offset + 1] == tag[1] && data[offset + 2] == tag[2] && data[offset + 3] == tag[3];
  }

  internal static bool LooksPlausible(ReadOnlySpan<byte> header)
    => header.Length >= 12 && _Is(header, 0, "FORM") && _Is(header, 8, "ANIM");

  internal readonly record struct RawFrame(int Offset, int Length);

  /// <summary>Walks the top-level <c>FORM ILBM</c> children of the outer <c>FORM ANIM</c>, in file order,
  /// stopping cleanly at the first chunk that does not fully fit in what remains of the file.</summary>
  private static IEnumerable<RawFrame> _WalkFrames(ReadOnlyMemory<byte> data) {
    if (data.Length < 12 || !_Is(data.Span, 0, "FORM") || !_Is(data.Span, 8, "ANIM"))
      yield break;

    var formSize = BinaryPrimitives.ReadUInt32BigEndian(data.Span[4..]);
    var end = (int)Math.Min((long)8 + formSize, data.Length);
    var pos = 12;

    while (pos + 8 <= end) {
      if (pos + 8 > data.Length)
        yield break;

      var chunkSize = BinaryPrimitives.ReadUInt32BigEndian(data.Span[(pos + 4)..]);
      var isForm = _Is(data.Span, pos, "FORM");
      var isIlbm = isForm && pos + 12 <= data.Length && _Is(data.Span, pos + 8, "ILBM");
      var totalLength = 8 + (int)chunkSize;

      if (pos + totalLength > data.Length)
        yield break;

      if (isIlbm)
        yield return new(pos, totalLength);

      pos += totalLength + (int)(chunkSize & 1);
    }
  }

  internal static AnimContainer Open(ReadOnlyMemory<byte> data) {
    if (data.Length < 12)
      throw new NotSupportedException(
        $"The file is {data.Length} bytes, short of the twelve bytes an IFF group chunk header needs. "
        + "This is not an IFF ANIM file.");

    if (!LooksPlausible(data.Span))
      throw new NotSupportedException(
        "This file does not open with 'FORM', a size, and 'ANIM'. This is not an IFF ANIM file.");

    var frames = new List<RawFrame>();
    foreach (var frame in _WalkFrames(data))
      frames.Add(frame);

    // A file cut off before its first frame's BODY finished, or one whose FORM ANIM is otherwise
    // empty, is read as far as it goes rather than refused outright — the same stance id Cinematic's
    // reader takes on a file that runs out of room for its next chunk.
    var (width, height) = frames.Count > 0 ? _ReadDimensions(data.Span, frames[0]) : (0, 0);

    return new() {
      Data = data,
      Width = width,
      Height = height,
      FrameCount = frames.Count,
    };
  }

  /// <summary>Reads width and height from the first frame's own BMHD, without decoding anything else —
  /// purely so the container's <see cref="MediaStreamInfo"/> can state a picture size the way every other
  /// container here does.</summary>
  private static (int Width, int Height) _ReadDimensions(ReadOnlySpan<byte> data, RawFrame first) {
    var pos = first.Offset + 12; // skip FORM + size + "ILBM"
    var end = first.Offset + first.Length;

    while (pos + 8 <= end) {
      var chunkSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(pos + 4)..]);
      if (_Is(data, pos, "BMHD") && pos + 8 + 4 <= end) {
        var width = BinaryPrimitives.ReadUInt16BigEndian(data[(pos + 8)..]);
        var height = BinaryPrimitives.ReadUInt16BigEndian(data[(pos + 10)..]);
        return (width, height);
      }

      pos += 8 + chunkSize + (chunkSize & 1);
    }

    return (0, 0);
  }

  internal static IEnumerable<CodedPacket> ReadPackets(AnimContainer container) {
    long frame = 0;
    foreach (var raw in _WalkFrames(container.Data)) {
      yield return new(
        StreamIndex: 0,
        Data: container.Data.Slice(raw.Offset, raw.Length),
        PresentationTimestamp: frame,
        DecodeTimestamp: frame,
        IsKeyFrame: frame == 0);
      ++frame;
    }
  }
}

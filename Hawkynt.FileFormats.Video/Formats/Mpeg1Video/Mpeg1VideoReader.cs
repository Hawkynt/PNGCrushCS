using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Mpeg1Video;

/// <summary>
/// Cuts an MPEG-1 video elementary stream into one packet per coded picture.
/// </summary>
/// <remarks>
/// The whole of the format's structure is the start code: three zero bytes, a one byte, and one more
/// byte saying what follows (ISO/IEC 11172-2, 2.4.2.1 and Table 2-B.1). Start codes are byte-aligned
/// and the encoder is required to make sure the pattern never occurs inside coded data, so finding
/// them is a byte search and not a bit search — there is no emulation prevention to undo the way
/// there is in H.264.
/// <para/>
/// What this does <em>not</em> do is read any of the headers it walks past. It never learns the
/// picture size, the frame rate, the quantiser matrices or which picture type a packet holds: every
/// one of those lives in a header the decoder parses for itself, and a demuxer that parsed them too
/// would be a second place for the same field to be read differently. The one thing it looks at
/// beyond the start-code byte is nothing at all.
/// </remarks>
public static class Mpeg1VideoReader {

  /// <summary>The byte that follows <c>00 00 01</c> in a picture start code (11172-2, Table 2-B.1).</summary>
  internal const byte PictureStartCode = 0x00;

  /// <summary>The lowest and highest slice start-code values; the value is the slice's row.</summary>
  internal const byte FirstSliceStartCode = 0x01;

  internal const byte LastSliceStartCode = 0xAF;

  /// <summary>Sequence header — the only point at which decoding may begin (11172-2, 2.4.2.3).</summary>
  internal const byte SequenceHeaderCode = 0xB3;

  /// <summary>Sequence end code — the last thing in a stream.</summary>
  internal const byte SequenceEndCode = 0xB7;

  public static Mpeg1VideoContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MPEG-1 video file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Mpeg1VideoContainer FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static Mpeg1VideoContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    _RefuseWithoutSequenceHeader(data);
    return new() { Data = data };
  }

  /// <summary>
  /// Opens a stream from a span, which copies it once.
  /// </summary>
  /// <remarks>
  /// The container outlives this call and its packets are windows onto the bytes, which a span makes
  /// no promise about. Callers holding an array should use <see cref="FromBytes"/>.
  /// </remarks>
  public static Mpeg1VideoContainer FromSpan(ReadOnlySpan<byte> data) {
    _RefuseWithoutSequenceHeader(data);

    return new() { Data = data.ToArray() };
  }

  /// <summary>
  /// Refuses anything that does not begin with a sequence header start code.
  /// </summary>
  /// <remarks>
  /// The only eager check. A stream may only be entered at a sequence header — that is where the
  /// picture size and the quantiser matrices are stated — so a file that does not open with one is
  /// not an elementary stream from its beginning, whatever else it may be.
  /// </remarks>
  private static void _RefuseWithoutSequenceHeader(ReadOnlySpan<byte> data) {
    if (data.Length < 4 || data[0] != 0x00 || data[1] != 0x00 || data[2] != 0x01 || data[3] != SequenceHeaderCode)
      throw new InvalidDataException(
        "Data does not begin with an MPEG-1 sequence header start code (00 00 01 B3).");
  }

  /// <summary>
  /// Walks the coded pictures of the stream, one packet each.
  /// </summary>
  /// <remarks>
  /// A packet runs from the first header belonging to a picture through the last byte of that
  /// picture's final slice. The sequence header, group-of-pictures header, user data and extension
  /// data that precede a picture are part of <em>its</em> packet rather than of the one before,
  /// because they describe what follows them: a decoder handed a picture without the sequence header
  /// that introduced it has no picture size to decode it at.
  /// <para/>
  /// So the boundary is not "at every picture start code". It is at the first of the run of headers
  /// that leads up to a picture start code, which is why the position of that run is remembered as
  /// the walk passes it rather than being searched for backwards afterwards.
  /// </remarks>
  internal static IEnumerable<CodedPacket> Split(ReadOnlyMemory<byte> data) {
    var packetStart = 0;

    // Where the current run of sequence/GOP/user/extension headers began. Once a picture has been
    // seen, such a run is the start of the next packet rather than a part of this one.
    var pendingBoundary = -1;
    var sawPicture = false;

    // Whether a sequence header has been read into the packet being accumulated, and whether one has
    // been read into the run of headers that will open the next.
    var openable = false;
    var nextOpenable = false;
    var ordinal = 0L;

    foreach (var (position, code) in StartCodes(data)) {
      switch (code) {
        case >= FirstSliceStartCode and <= LastSliceStartCode:
          // Slice data belongs to the picture that opened it, and cannot open a packet of its own.
          pendingBoundary = -1;
          nextOpenable = false;
          continue;

        case PictureStartCode:
          if (sawPicture) {
            var boundary = pendingBoundary >= 0 ? pendingBoundary : position;
            yield return _Packet(data, packetStart, boundary, ordinal, openable);
            ++ordinal;
            packetStart = boundary;
            openable = nextOpenable;
          }

          sawPicture = true;
          pendingBoundary = -1;
          nextOpenable = false;
          continue;

        case SequenceEndCode:
          if (sawPicture)
            yield return _Packet(data, packetStart, pendingBoundary >= 0 ? pendingBoundary : position, ordinal, openable);

          yield break;

        default:
          // A header that introduces whatever comes after it. It opens the next packet if a picture
          // has already been read into this one, and it is only known to do so once that picture
          // start code is reached — hence remembering it rather than searching backwards for it.
          if (sawPicture) {
            if (pendingBoundary < 0)
              pendingBoundary = position;

            nextOpenable |= code == SequenceHeaderCode;
          } else
            openable |= code == SequenceHeaderCode;

          continue;
      }
    }

    // A stream that simply stops, with no sequence end code. Everything from the last boundary is
    // still one whole picture as long as a picture start code was seen in it.
    if (sawPicture)
      yield return _Packet(data, packetStart, data.Length, ordinal, openable);
  }

  /// <summary>
  /// One packet, flagged as a point decoding may begin at when it carries a sequence header.
  /// </summary>
  /// <remarks>
  /// The flag is the sequence header and not the picture coding type, and deliberately so. Reading
  /// the coding type means parsing the picture header, which is the decoder's job; and for this
  /// format the sequence header is the stronger answer anyway — an I-picture whose sequence header
  /// is upstream still cannot be decoded on its own, because its picture size and quantiser matrices
  /// were stated there and nowhere else.
  /// </remarks>
  private static CodedPacket _Packet(ReadOnlyMemory<byte> data, int from, int to, long ordinal, bool carriesSequenceHeader)
    => new(0, data[from..to], PresentationTimestamp: null, DecodeTimestamp: ordinal, IsKeyFrame: carriesSequenceHeader);

  /// <summary>
  /// Walks every start code in the data: the offset of its leading zero byte and the byte that says
  /// what it introduces.
  /// </summary>
  /// <remarks>
  /// Any number of zero bytes may precede the <c>00 00 01</c> — encoders pad with them to reach a
  /// byte boundary or a target rate (11172-2, 2.4.2.1) — so a code found at <c>00 00 00 01 B3</c>
  /// must report the position of the <em>last</em> two zeroes, not the first. Reporting the earlier
  /// one would put the stuffing inside the next packet instead of the previous one, which changes
  /// nothing about the decode but does change where the packets are cut, and packets that are cut
  /// somewhere other than where they are written are hard to compare against another demuxer's.
  /// </remarks>
  internal static IEnumerable<(int Position, byte Code)> StartCodes(ReadOnlyMemory<byte> data) {
    for (var i = 0; i + 3 < data.Length; ++i) {
      if (!_IsStartCodePrefix(data, i))
        continue;

      yield return (i, _At(data, i + 3));
      i += 2;
    }
  }

  // Both of these exist because a span cannot be a local of an iterator method.
  private static bool _IsStartCodePrefix(ReadOnlyMemory<byte> data, int offset) {
    var span = data.Span;
    return span[offset] == 0x00 && span[offset + 1] == 0x00 && span[offset + 2] == 0x01;
  }

  private static byte _At(ReadOnlyMemory<byte> data, int offset) => data.Span[offset];
}

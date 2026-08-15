using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Mjpeg;

/// <summary>Splits a raw Motion JPEG stream into the JPEGs it is a concatenation of.</summary>
/// <remarks>
/// The split walks the marker structure rather than searching for <c>FF D9</c>. Entropy-coded data
/// may contain those two bytes, and a JPEG carrying a thumbnail has a whole second JPEG inside its
/// APP1 with an <c>FF D9</c> of its own — a search would cut the first frame short at either. The
/// walk is <see cref="JpegChunkLayout.FirstImageLength"/>, which shares the entropy scanner with the
/// chunk enumeration the JPEG optimizer already relies on, so the delicate part has one home.
/// <para/>
/// The walk happens one frame at a time, as packets are asked for. The reader that preceded this one
/// split the whole stream in its constructor and kept every frame in a list of byte arrays; for the
/// one file format in this library where a hundredfold of the memory is normal, that was the wrong
/// way round.
/// </remarks>
public static class MjpegReader {

  public static MjpegContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MJPEG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MjpegContainer FromStream(Stream stream) {
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

  public static MjpegContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return _Open(data);
  }

  /// <summary>
  /// Opens a stream from a span, which copies it once.
  /// </summary>
  /// <remarks>
  /// The container outlives this call and its packets are windows onto the bytes, which a span makes
  /// no promise about. Callers holding an array should use <see cref="FromBytes"/>.
  /// </remarks>
  public static MjpegContainer FromSpan(ReadOnlySpan<byte> data) {
    _RefuseWithoutStartOfImage(data);

    return new() { Data = data.ToArray() };
  }

  private static MjpegContainer _Open(ReadOnlyMemory<byte> data) {
    _RefuseWithoutStartOfImage(data.Span);

    return new() { Data = data };
  }

  /// <summary>
  /// Refuses anything that does not begin with a start-of-image marker.
  /// </summary>
  /// <remarks>
  /// The only eager check left. Whether the stream holds a *complete* frame is not asked here,
  /// because answering it means finding one, and finding one means walking — which is what the
  /// caller asked to be able to do lazily. A stream that begins correctly and then stops mid-frame
  /// walks to zero packets, which is the same answer arrived at without reading the file to say so.
  /// </remarks>
  private static void _RefuseWithoutStartOfImage(ReadOnlySpan<byte> data) {
    if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
      throw new InvalidDataException("Data does not begin with a JPEG start-of-image marker.");
  }

  /// <summary>Walks the complete JPEGs a stream is made of, in order.</summary>
  internal static IEnumerable<CodedPacket> Split(ReadOnlyMemory<byte> data) {
    var offset = 0;
    var ordinal = 0L;

    while (offset + 4 <= data.Length) {
      // Skip whatever a writer left between frames. Some cameras separate them with a boundary line
      // and some pad to a block; neither is part of a picture, and the next one starts at the next
      // start-of-image marker either way.
      if (!_StartsImage(data, offset)) {
        ++offset;
        continue;
      }

      var length = _FrameLength(data, offset);

      // No end-of-image marker means the stream stops in the middle of this frame. What is there is
      // part of a picture, not a picture, so it is dropped rather than decoded into whatever the
      // truncation happens to produce.
      if (length <= 0)
        yield break;

      // Every frame of a Motion JPEG stream is coded on its own, so every one of them is a point
      // decoding may start at.
      yield return new(0, data.Slice(offset, length), ordinal, ordinal, IsKeyFrame: true);
      ++ordinal;
      offset += length;
    }
  }

  // Both of these exist because a span cannot be a local of an iterator method.
  private static bool _StartsImage(ReadOnlyMemory<byte> data, int offset) {
    var span = data.Span;
    return span[offset] == 0xFF && span[offset + 1] == 0xD8;
  }

  private static int _FrameLength(ReadOnlyMemory<byte> data, int offset) => JpegChunkLayout.FirstImageLength(data.Span[offset..]);
}

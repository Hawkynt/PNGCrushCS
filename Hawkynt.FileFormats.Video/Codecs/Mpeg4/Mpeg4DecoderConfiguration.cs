using System;
using System.Buffers.Binary;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// Finds the headers a container carried out of band, wherever that container chose to put them.
/// </summary>
/// <remarks>
/// An MPEG-4 stream in an AVI or a Matroska file repeats its visual object sequence, visual object
/// and video object layer headers in front of the first picture, so the packets alone are enough. A
/// stream in an ISO base media file usually does not: the headers sit in the sample entry's
/// <c>esds</c> box and the packets hold nothing but coded pictures. A decoder that only read packets
/// would take the first picture of such a file for a stream with no layer header and refuse it.
/// <para/>
/// Which of the two a container hands over is not something the container decides on this decoder's
/// behalf: it hands over whatever described the codec, verbatim, and picking the headers out of it is
/// the codec's own business. So this takes the private data as it comes and works out what it is —
/// raw headers if it begins with a start code, and otherwise a sample entry to be walked into.
/// <para/>
/// The walk is a real one and not a search for <c>00 00 01</c>. A visual sample entry begins with six
/// reserved zero bytes and a data reference index of one, which is the byte sequence <c>00 00 00 00
/// 00 00 00 01</c> — a search would find a start code inside it, four bytes before the real headers
/// start, and read the two bytes after it as a video object header.
/// </remarks>
internal static class Mpeg4DecoderConfiguration {

  /// <summary>
  /// The fixed part of a VisualSampleEntry, before the boxes describing the codec (ISO/IEC 14496-12).
  /// </summary>
  /// <remarks>
  /// Written as its terms rather than as eighty-six, because two of the terms are four reserved bytes
  /// that carry nothing and are easy to leave out — and leaving one out lands the walk four bytes
  /// short of the first child box, where it reads a length out of the middle of a field and gives up.
  /// </remarks>
  private const int _VISUAL_SAMPLE_ENTRY_HEADER =
    8       // the box's own size and type
    + 6     // reserved
    + 2     // data_reference_index
    + 2 + 2 + 12  // pre_defined, reserved, pre_defined
    + 2 + 2 // width and height
    + 4 + 4 // horizontal and vertical resolution
    + 4     // reserved
    + 2     // frame_count
    + 32    // compressorname
    + 2     // depth
    + 2;    // pre_defined

  private const int _BOX_HEADER = 8;

  /// <summary>ES_DescrTag: the descriptor an <c>esds</c> box holds.</summary>
  private const byte _ELEMENTARY_STREAM_DESCRIPTOR = 0x03;

  /// <summary>DecoderConfigDescrTag.</summary>
  private const byte _DECODER_CONFIGURATION_DESCRIPTOR = 0x04;

  /// <summary>DecSpecificInfoTag, whose bytes are the visual headers themselves.</summary>
  private const byte _DECODER_SPECIFIC_INFORMATION = 0x05;

  /// <summary>
  /// The out-of-band headers, or an empty span when the private data holds none.
  /// </summary>
  /// <remarks>
  /// Empty is an ordinary answer and not a failure: a container that repeats the headers in front of
  /// every keyframe has nothing to put here, and so does one that carries no private data at all.
  /// </remarks>
  internal static ReadOnlyMemory<byte> HeadersIn(ReadOnlyMemory<byte> privateData) {
    var span = privateData.Span;
    if (span.Length >= 4 && span[0] == 0 && span[1] == 0 && span[2] == 1)
      return privateData;

    return _DecoderSpecificInformationIn(privateData);
  }

  /// <summary>Walks a visual sample entry to the decoder specific information inside its <c>esds</c>.</summary>
  private static ReadOnlyMemory<byte> _DecoderSpecificInformationIn(ReadOnlyMemory<byte> sampleEntry) {
    if (sampleEntry.Length <= _VISUAL_SAMPLE_ENTRY_HEADER)
      return ReadOnlyMemory<byte>.Empty;

    var offset = _VISUAL_SAMPLE_ENTRY_HEADER;
    while (offset + _BOX_HEADER <= sampleEntry.Length) {
      var header = sampleEntry.Span.Slice(offset, _BOX_HEADER);
      var size = (int)BinaryPrimitives.ReadUInt32BigEndian(header);
      if (size < _BOX_HEADER || offset + size > sampleEntry.Length)
        return ReadOnlyMemory<byte>.Empty;

      // 'esds', and it is a full box: a version byte and three flag bytes before the descriptor.
      if (header[4] == (byte)'e' && header[5] == (byte)'s' && header[6] == (byte)'d' && header[7] == (byte)'s'
          && size >= _BOX_HEADER + 4)
        return _WalkDescriptors(sampleEntry.Slice(offset + _BOX_HEADER + 4, size - _BOX_HEADER - 4));

      offset += size;
    }

    return ReadOnlyMemory<byte>.Empty;
  }

  /// <summary>
  /// Walks the descriptor tree of ISO/IEC 14496-1 down to the decoder specific information.
  /// </summary>
  /// <remarks>
  /// Three descriptors nested one inside the next, and the two outer ones carry fields whose presence
  /// depends on flags, so the walk has to read them rather than step over a fixed number of bytes.
  /// </remarks>
  private static ReadOnlyMemory<byte> _WalkDescriptors(ReadOnlyMemory<byte> data) {
    if (!_TryReadDescriptor(data, out var tag, out var body) || tag != _ELEMENTARY_STREAM_DESCRIPTOR)
      return ReadOnlyMemory<byte>.Empty;

    var span = body.Span;
    if (span.Length < 3)
      return ReadOnlyMemory<byte>.Empty;

    var flags = span[2];
    var offset = 3;
    if ((flags & 0x80) != 0)
      offset += 2; // depends_on_ES_ID

    if ((flags & 0x40) != 0) {
      if (offset >= span.Length)
        return ReadOnlyMemory<byte>.Empty;

      offset += 1 + span[offset]; // URL_length and URL
    }

    if ((flags & 0x20) != 0)
      offset += 2; // OCR_ES_ID

    if (offset >= body.Length
        || !_TryReadDescriptor(body[offset..], out tag, out body)
        || tag != _DECODER_CONFIGURATION_DESCRIPTOR)
      return ReadOnlyMemory<byte>.Empty;

    // objectTypeIndication, streamType and flags, bufferSizeDB, maxBitrate, avgBitrate.
    const int configurationFields = 1 + 1 + 3 + 4 + 4;
    if (body.Length <= configurationFields
        || !_TryReadDescriptor(body[configurationFields..], out tag, out body)
        || tag != _DECODER_SPECIFIC_INFORMATION)
      return ReadOnlyMemory<byte>.Empty;

    return body;
  }

  /// <summary>
  /// Reads one descriptor's tag and body. The length is seven bits a byte, with the top bit saying
  /// another byte follows.
  /// </summary>
  private static bool _TryReadDescriptor(ReadOnlyMemory<byte> data, out byte tag, out ReadOnlyMemory<byte> body) {
    tag = 0;
    body = ReadOnlyMemory<byte>.Empty;

    var span = data.Span;
    if (span.Length < 2)
      return false;

    tag = span[0];
    var length = 0;
    var offset = 1;
    for (var step = 0; step < 4; ++step) {
      if (offset >= span.Length)
        return false;

      var piece = span[offset++];
      length = (length << 7) | (piece & 0x7F);
      if ((piece & 0x80) == 0)
        break;
    }

    if (length < 0 || offset + length > data.Length)
      return false;

    body = data.Slice(offset, length);
    return true;
  }
}

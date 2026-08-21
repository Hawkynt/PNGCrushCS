using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.Asf;

/// <summary>
/// Takes the Data Object's packets apart into the payloads they carry.
/// </summary>
/// <remarks>
/// This is the part of ASF that is genuinely intricate, and all of the intricacy is in the service of
/// one thing: a packet is a fixed number of bytes on the wire whatever it happens to hold. Everything
/// that varies — how long the packet claims to be, how many bytes each of its fields occupies, how
/// many payloads are inside it — is encoded as a two-bit length type so the header can shrink to fit
/// whatever is left (clause 5.2). A reader that assumed any one of those widths would read one file
/// and misread the next.
/// <para/>
/// Three shapes have to be handled or ordinary files come out wrong. A packet may open with an error
/// correction block, which is not part of the payload data and has to be stepped over (clause 5.2.1).
/// A packet may hold several payloads, each with its own length (clause 5.2.3.3). And a payload whose
/// replicated data is exactly one byte is not a fragment at all but a run of whole objects packed
/// together, with what would have been the offset field holding the first one's presentation time and
/// the single replicated byte holding the step between them (clause 5.2.3.4) — read as a fragment it
/// would hand a decoder several frames glued into one.
/// <para/>
/// Nothing here reassembles. A payload is what the packet holds; putting the pieces of a media object
/// back together needs state that outlives the packet, and that belongs to the walk in
/// <see cref="AsfContainer"/>.
/// </remarks>
internal static class AsfPacketReader {

  /// <summary>How many bytes a field occupies for each of the four length types (clause 5.2.2).</summary>
  /// <remarks>
  /// Not one, two, three, four: type 3 is a 32-bit number and there is no 24-bit width in the format.
  /// </remarks>
  private static int _WidthOf(int lengthType) => lengthType switch {
    0 => 0,
    1 => 1,
    2 => 2,
    _ => 4,
  };

  /// <summary>Walks every payload of the Data Object, in the order the file stores them.</summary>
  /// <remarks>
  /// Lazily, and re-runnably: nothing is touched until a payload is asked for. The list of payloads is
  /// built once and refilled per packet rather than allocated per packet — a packet holds a few dozen
  /// at most, and a two-hour recording holds millions of packets.
  /// </remarks>
  internal static IEnumerable<AsfPayload> Walk(
    ReadOnlyMemory<byte> file, int dataStart, int dataEnd, long packetCount, int packetSize, long preroll) {
    var payloads = new List<AsfPayload>();
    var offset = dataStart;

    // The declared count is the writer's claim; the bytes are the fact. A recording cut off mid-write
    // keeps a count from before it stopped, so the walk ends at whichever of the two comes first
    // rather than reading past the object or inventing packets that were never written.
    for (long packet = 0; (packetCount <= 0 || packet < packetCount) && offset + 1 <= dataEnd; ++packet) {
      var consumed = _ReadPacket(file, offset, dataEnd, packetSize, preroll, payloads);
      if (consumed <= 0)
        yield break;

      foreach (var payload in payloads)
        yield return payload;

      offset += consumed;
    }
  }

  /// <summary>
  /// Reads one data packet, filling <paramref name="payloads"/> with what it holds.
  /// </summary>
  /// <returns>How many bytes the packet occupied, or zero when it could not be read.</returns>
  private static int _ReadPacket(
    ReadOnlyMemory<byte> file, int start, int end, int packetSize, long preroll, List<AsfPayload> payloads) {
    payloads.Clear();

    var span = file.Span;
    var at = start;

    // The Error Correction Present bit is bit 7 of the first byte whether or not there is an error
    // correction block, because when there is none that byte is already the Length Type Flags and the
    // bit sits in the same place there (clauses 5.2.1 and 5.2.2). One test therefore serves both.
    if ((span[at] & 0x80) != 0) {
      var errorCorrectionFlags = span[at];

      // The specification fixes the Error Correction Length Type at zero, which is what makes the low
      // nibble a byte count. Anything else describes the block some other way and there is no second
      // way defined, so the packet cannot be stepped over to reach the payloads.
      if (((errorCorrectionFlags >> 5) & 0x03) != 0)
        return 0;

      at += 1 + (errorCorrectionFlags & 0x0F);
      if (at + 2 > end)
        return 0;
    }

    if (at + 2 > end)
      return 0;

    var lengthTypeFlags = span[at];
    var propertyFlags = span[at + 1];
    at += 2;

    var multiplePayloads = (lengthTypeFlags & 0x01) != 0;
    var sequenceWidth = _WidthOf((lengthTypeFlags >> 1) & 0x03);
    var paddingWidth = _WidthOf((lengthTypeFlags >> 3) & 0x03);
    var packetLengthWidth = _WidthOf((lengthTypeFlags >> 5) & 0x03);

    if (at + packetLengthWidth + sequenceWidth + paddingWidth + 6 > end)
      return 0;

    var declaredLength = _ReadNumber(span, at, packetLengthWidth);
    at += packetLengthWidth;

    // The sequence field is read only to be stepped over: the specification reserves it and says it is
    // not used, so a value in it says nothing about where anything is.
    at += sequenceWidth;

    var paddingLength = _ReadNumber(span, at, paddingWidth);
    at += paddingWidth;

    // Send Time is when the packet leaves, in milliseconds, and Duration how long it covers. Neither is
    // a payload's presentation time, but Send Time is the only clock a payload that states no time of
    // its own can be placed by.
    var sendTime = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(at, 4));
    at += 6;

    // A packet whose length type is zero states no length, and occupies the maximum the file declared
    // (clause 5.2.2) — that fixed size is the whole reason the field may be left out.
    var packetLength = packetLengthWidth == 0 ? packetSize : (int)declaredLength;
    if (packetLength <= 0 || start + packetLength > end)
      packetLength = end - start;

    // Padding sits at the tail and is not payload. Reading to the end of the packet instead would hand
    // a decoder a frame with a run of filler stuck to it.
    var payloadEnd = Math.Min(start + packetLength, end);

    // The stated padding is four bytes of the file like any other and may be longer than the packet it
    // is padding. Subtracting it unguarded puts the end of the payloads before their beginning, and
    // every length measured from there comes out negative — which is a cursor that walks backwards out
    // of the file rather than a packet that is refused. It is compared unsigned for the same reason the
    // subtraction is guarded: narrowed first, a padding length with its top bit set is a negative one.
    payloadEnd = paddingLength >= (uint)payloadEnd ? 0 : payloadEnd - (int)paddingLength;
    if (payloadEnd < at)
      payloadEnd = at;

    var count = 1;
    var payloadLengthWidth = 0;
    if (multiplePayloads) {
      if (at + 1 > payloadEnd)
        return packetLength;

      var payloadFlags = span[at++];
      count = payloadFlags & 0x3F;
      payloadLengthWidth = _WidthOf((payloadFlags >> 6) & 0x03);
    }

    var replicatedWidth = _WidthOf(propertyFlags & 0x03);
    var offsetWidth = _WidthOf((propertyFlags >> 2) & 0x03);
    var mediaObjectWidth = _WidthOf((propertyFlags >> 4) & 0x03);
    var streamNumberWidth = _WidthOf((propertyFlags >> 6) & 0x03);

    // The stream number is one byte in every file the specification allows, but its length type may
    // still be stated as zero — and a zero-width field is not a field that is absent here, because the
    // number has to come from somewhere. Counting it as the one byte it is keeps the bounds check below
    // honest; counting it as nothing would let the cursor sit exactly on the end of the packet and read
    // the byte after it.
    var streamNumberBytes = streamNumberWidth == 0 ? 1 : streamNumberWidth;

    for (var i = 0; i < count; ++i) {
      if (at + streamNumberBytes + mediaObjectWidth + offsetWidth + replicatedWidth > payloadEnd)
        break;

      // The stream number's top bit is the key frame flag, so the field is never simply a number
      // (clause 5.2.3.1). A file that stated a wider field for it still puts the flag in the top bit of
      // the first byte, because the flag belongs to the byte and not to the number.
      var streamByte = span[at];
      var isKeyFrame = (streamByte & 0x80) != 0;
      var streamNumber = streamByte & 0x7F;
      at += streamNumberBytes;

      var mediaObjectNumber = (int)_ReadNumber(span, at, mediaObjectWidth);
      at += mediaObjectWidth;

      var offsetField = _ReadNumber(span, at, offsetWidth);
      at += offsetWidth;

      var replicatedLength = _ReadNumber(span, at, replicatedWidth);
      at += replicatedWidth;

      // Compared unsigned, and against what is left rather than by adding to the cursor. Its length type
      // may be four bytes, so the file may state a replicated length whose top bit is set; narrowed to a
      // signed integer that is negative, an addition to the cursor then satisfies any upper bound, and
      // the cursor walks backwards out of the file on the very next field.
      if (replicatedLength > (uint)(payloadEnd - at))
        break;

      var replicated = at;
      at += (int)replicatedLength;

      int payloadLength;
      if (multiplePayloads) {
        if (at + payloadLengthWidth > payloadEnd)
          break;

        payloadLength = (int)_ReadNumber(span, at, payloadLengthWidth);
        at += payloadLengthWidth;
      } else
        // A packet holding one payload states no length for it: it is whatever is left between here and
        // the padding, which is the other half of why the packet is a fixed size.
        payloadLength = payloadEnd - at;

      if (payloadLength < 0 || at + payloadLength > payloadEnd)
        payloadLength = payloadEnd - at;

      if (replicatedLength == 1) {
        _ReadCompressed(file, at, payloadLength, streamNumber, isKeyFrame, (long)offsetField - preroll, span[replicated], payloads);
        at += payloadLength;
        continue;
      }

      // Eight bytes of replicated data are the media object's size and its presentation time, in that
      // order (clause 5.2.3.2). Fewer than eight states neither, and the packet's own send time is then
      // the only clock there is — a payload placed nowhere is worse than one placed by its packet.
      var mediaObjectSize = replicatedLength >= 8
        ? (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(replicated, 4))
        : payloadLength;
      var presentationTime = replicatedLength >= 8
        ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(replicated + 4, 4))
        : sendTime;

      payloads.Add(new(
        streamNumber,
        mediaObjectNumber,
        (int)offsetField,
        mediaObjectSize,
        (long)presentationTime - preroll,
        isKeyFrame,
        file.Slice(at, payloadLength)));

      at += payloadLength;
    }

    return packetLength;
  }

  /// <summary>
  /// Unpacks a compressed payload, which is a run of whole media objects rather than a fragment of one.
  /// </summary>
  /// <remarks>
  /// Replicated data of exactly one byte means the payload was built by the other rule in clause
  /// 5.2.3.4: what would have been the Offset Into Media Object is the first object's presentation
  /// time, the one replicated byte is how many milliseconds each object is after the one before it, and
  /// the payload data is a run of sub-objects each introduced by a single byte of length. Small frames —
  /// most audio, and the quiet parts of a low-bitrate video — are stored this way because a header per
  /// frame would cost more than the frames.
  /// <para/>
  /// Each sub-object comes out whole, so the walk that reassembles fragments has nothing to do with
  /// these: they are already complete, and each states its own length as its own size.
  /// </remarks>
  private static void _ReadCompressed(
    ReadOnlyMemory<byte> file, int at, int length, int streamNumber, bool isKeyFrame,
    long firstPresentationTime, byte presentationTimeDelta, List<AsfPayload> payloads) {
    var span = file.Span;
    var end = at + length;
    var presentationTime = firstPresentationTime;

    while (at < end) {
      var subLength = span[at++];
      if (at + subLength > end)
        return;

      payloads.Add(new(
        streamNumber,
        MediaObjectNumber: 0,
        Offset: 0,
        MediaObjectSize: subLength,
        presentationTime,
        isKeyFrame,
        file.Slice(at, subLength)));

      at += subLength;
      presentationTime += presentationTimeDelta;
    }
  }

  /// <summary>Reads one of the format's variable-width little-endian numbers.</summary>
  /// <remarks>
  /// A width of zero is not an error: it is what a length type of zero means, and the value it stands
  /// for is nought (clause 5.2.2).
  /// </remarks>
  private static uint _ReadNumber(ReadOnlySpan<byte> span, int offset, int width) => width switch {
    0 => 0u,
    1 => span[offset],
    2 => BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2)),
    _ => BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4)),
  };
}

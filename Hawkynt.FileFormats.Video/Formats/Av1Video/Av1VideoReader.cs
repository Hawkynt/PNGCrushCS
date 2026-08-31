using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Av1Video;

/// <summary>Parses the framing of an AV1 low-overhead OBU elementary stream.</summary>
internal static class Av1VideoReader {

  private const int _OBU_TEMPORAL_DELIMITER = 2;

  private readonly record struct Obu(int Type, int Offset, int Length, int PayloadLength);

  internal static bool LooksLikeByteStream(ReadOnlySpan<byte> data) {
    if (data.IsEmpty)
      return false;

    var at = 0;
    var header = data[at++];
    if ((header & 0x81) != 0 || ((header >> 3) & 0x0F) != _OBU_TEMPORAL_DELIMITER || (header & 0x02) == 0)
      return false;

    if ((header & 0x04) != 0) {
      if (at >= data.Length || (data[at] & 0x07) != 0)
        return false;
      ++at;
    }

    return _TryReadLeb128(data, ref at, out var payloadSize) && payloadSize == 0;
  }

  internal static Av1VideoContainer FromSpan(ReadOnlySpan<byte> data) => _Open(data.ToArray());

  internal static Av1VideoContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return _Open(data);
  }

  internal static IEnumerable<CodedPacket> Split(ReadOnlyMemory<byte> data) {
    var at = 0;
    var temporalUnitStart = 0;
    var sawContent = false;
    long ordinal = 0;

    while (at < data.Length) {
      var obu = _ReadObu(data, at);
      if (obu.Type == _OBU_TEMPORAL_DELIMITER) {
        if (obu.PayloadLength != 0)
          throw new InvalidDataException($"AV1 temporal delimiter OBU at byte {at} has a {obu.PayloadLength}-byte payload; the delimiter payload must be empty.");

        if (at != temporalUnitStart) {
          if (!sawContent)
            throw new InvalidDataException($"AV1 temporal unit beginning at byte {temporalUnitStart} contains no content OBU before the next temporal delimiter at byte {at}.");

          yield return new(
            StreamIndex: 0,
            Data: data.Slice(temporalUnitStart, at - temporalUnitStart),
            PresentationTimestamp: null,
            DecodeTimestamp: ordinal++);
          temporalUnitStart = at;
          sawContent = false;
        }
      } else {
        if (at == 0)
          throw new InvalidDataException("An AV1 .obu stream must begin each temporal unit with a temporal delimiter OBU.");
        sawContent = true;
      }

      at += obu.Length;
    }

    if (temporalUnitStart < data.Length) {
      if (!sawContent)
        throw new InvalidDataException($"AV1 temporal unit beginning at byte {temporalUnitStart} contains only its temporal delimiter OBU.");

      yield return new(
        StreamIndex: 0,
        Data: data[temporalUnitStart..],
        PresentationTimestamp: null,
        DecodeTimestamp: ordinal);
    }
  }

  /// <summary>
  /// Validates one writer packet as a sequence of complete low-overhead OBUs and returns whether it
  /// already starts with its temporal delimiter.
  /// </summary>
  internal static bool ValidateTemporalUnit(ReadOnlyMemory<byte> data) {
    if (data.IsEmpty)
      throw new InvalidDataException("An AV1 temporal-unit packet cannot be empty.");

    var at = 0;
    var first = true;
    var hasDelimiter = false;
    var hasContent = false;
    while (at < data.Length) {
      var obu = _ReadObu(data, at);
      if (obu.Type == _OBU_TEMPORAL_DELIMITER) {
        if (!first)
          throw new InvalidDataException($"AV1 packet contains a second temporal unit beginning at byte {at}; each coded packet must contain exactly one temporal unit.");
        if (obu.PayloadLength != 0)
          throw new InvalidDataException($"AV1 temporal delimiter OBU at byte {at} has a {obu.PayloadLength}-byte payload; the delimiter payload must be empty.");
        hasDelimiter = true;
      } else
        hasContent = true;

      first = false;
      at += obu.Length;
    }

    if (!hasContent)
      throw new InvalidDataException("An AV1 temporal-unit packet must contain at least one non-delimiter OBU.");

    return hasDelimiter;
  }

  private static Av1VideoContainer _Open(ReadOnlyMemory<byte> data) {
    if (!LooksLikeByteStream(data.Span))
      throw new InvalidDataException("The stream does not begin with a valid AV1 temporal delimiter OBU in low-overhead framing.");

    return new() { Data = data };
  }

  private static Obu _ReadObu(ReadOnlyMemory<byte> data, int offset) {
    var span = data.Span;
    if ((uint)offset >= (uint)span.Length)
      throw new InvalidDataException($"AV1 OBU starts at byte {offset}, outside the {span.Length}-byte stream.");

    var at = offset;
    var header = span[at++];
    if ((header & 0x80) != 0)
      throw new InvalidDataException($"AV1 OBU at byte {offset} has obu_forbidden_bit set.");
    if ((header & 0x01) != 0)
      throw new InvalidDataException($"AV1 OBU at byte {offset} has obu_reserved_1bit set.");

    var type = (header >> 3) & 0x0F;
    var hasExtension = (header & 0x04) != 0;
    var hasSize = (header & 0x02) != 0;
    if (!hasSize)
      throw new InvalidDataException($"AV1 low-overhead OBU at byte {offset} does not carry its required obu_size field.");

    if (hasExtension) {
      if (at >= span.Length)
        throw new InvalidDataException($"AV1 OBU at byte {offset} ends before its extension header.");
      if ((span[at] & 0x07) != 0)
        throw new InvalidDataException($"AV1 OBU at byte {offset} has non-zero extension_header_reserved_3bits.");
      ++at;
    }

    var size = _ReadLeb128(span, ref at, offset);
    if (size > int.MaxValue)
      throw new InvalidDataException($"AV1 OBU at byte {offset} declares {size} payload bytes, which this in-memory reader cannot address.");
    if ((long)at + size > span.Length)
      throw new InvalidDataException($"AV1 OBU at byte {offset} declares {size} payload bytes, but only {span.Length - at} remain.");

    return new(type, offset, checked(at - offset + (int)size), (int)size);
  }

  private static uint _ReadLeb128(ReadOnlySpan<byte> data, ref int at, int obuOffset) {
    ulong value = 0;
    for (var i = 0; i < 8; ++i) {
      if (at >= data.Length)
        throw new InvalidDataException($"AV1 OBU at byte {obuOffset} ends inside its obu_size LEB128 value.");

      var octet = data[at++];
      value |= (ulong)(octet & 0x7F) << (i * 7);
      if ((octet & 0x80) == 0) {
        if (value > uint.MaxValue)
          throw new InvalidDataException($"AV1 OBU at byte {obuOffset} has an obu_size larger than the AV1 32-bit LEB128 limit.");
        return (uint)value;
      }
    }

    throw new InvalidDataException($"AV1 OBU at byte {obuOffset} has an obu_size LEB128 value longer than eight bytes.");
  }

  private static bool _TryReadLeb128(ReadOnlySpan<byte> data, ref int at, out uint result) {
    ulong value = 0;
    for (var i = 0; i < 8; ++i) {
      if (at >= data.Length) {
        result = 0;
        return false;
      }

      var octet = data[at++];
      value |= (ulong)(octet & 0x7F) << (i * 7);
      if ((octet & 0x80) != 0)
        continue;

      if (value > uint.MaxValue) {
        result = 0;
        return false;
      }

      result = (uint)value;
      return true;
    }

    result = 0;
    return false;
  }
}

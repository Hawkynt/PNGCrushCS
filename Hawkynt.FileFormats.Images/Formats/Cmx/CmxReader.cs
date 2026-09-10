using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.EmbeddedDib;

namespace FileFormat.Cmx;

/// <summary>Reads the RIFF/RIFX CMX container header and its explicitly referenced <c>DISP</c> preview.</summary>
public static class CmxReader {
  private const int _PronomVersionMarkerOffset = 74;
  private const int _ContainerHeaderMinimumSize = 96;

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 12)
      return null;

    if (!_IsAscii4(header, 8, "CMX1") || (!_IsAscii4(header, 0, "RIFF") && !_IsAscii4(header, 0, "RIFX")))
      return false;

    // PRONOM x-fmt/34 and x-fmt/35 distinguish the two CMX generations by the ASCII
    // internal-version byte at absolute offset 74: '1' for 16-bit and '2' for 32-bit CMX.
    if (header.Length <= _PronomVersionMarkerOffset)
      return null;

    return header[_PronomVersionMarkerOffset] is (byte)'1' or (byte)'2';
  }

  public static CmxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 12)
      throw new InvalidDataException("CMX data is too short to contain a RIFF/RIFX header.");

    var bigEndian = _IsAscii4(data, 0, "RIFX");
    if (!bigEndian && !_IsAscii4(data, 0, "RIFF"))
      throw new InvalidDataException("CMX must begin with RIFF or RIFX.");
    if (!_IsAscii4(data, 8, "CMX1"))
      throw new InvalidDataException("RIFF/RIFX data is not a CMX1 form.");

    var declaredSize = _ReadUInt32(data, 4, bigEndian);
    var containerEnd64 = 8L + declaredSize;
    if (containerEnd64 < 12 || containerEnd64 > data.Length)
      throw new InvalidDataException("The CMX RIFF size extends beyond the supplied data.");
    var containerEnd = checked((int)containerEnd64);

    if (!_FindChunk(data, 12, containerEnd, "cont", bigEndian, out _, out var headerOffset, out var headerLength))
      throw new InvalidDataException("The CMX container has no cont header chunk.");
    if (headerLength < _ContainerHeaderMinimumSize)
      throw new InvalidDataException("The CMX cont chunk is too short to contain its section offsets.");

    var cmxHeader = data.Slice(headerOffset, headerLength);
    var byteOrder = _ReadAsciiNumber(cmxHeader.Slice(48, 4), "byte order");
    if (byteOrder is not (2 or 4))
      throw new InvalidDataException($"Unsupported CMX byte-order marker {byteOrder}; expected 2 or 4.");
    if ((byteOrder == 4) != bigEndian)
      throw new InvalidDataException("The CMX cont byte-order marker disagrees with its RIFF/RIFX framing.");

    var coordinateBytes = _ReadAsciiNumber(cmxHeader.Slice(52, 2), "coordinate size");
    var precision = coordinateBytes switch {
      2 => 16,
      4 => 32,
      _ => throw new InvalidDataException($"Unsupported CMX coordinate size {coordinateBytes}; expected 2 or 4 bytes."),
    };

    var internalVersion = _ReadAsciiNumber(cmxHeader.Slice(54, 4), "internal version");
    if (internalVersion is not (1 or 2))
      throw new InvalidDataException($"Unsupported CMX internal version {internalVersion}; expected 1 or 2.");

    var thumbnailOffset = _ReadUInt32(cmxHeader, 92, bigEndian);
    if (thumbnailOffset is 0 or uint.MaxValue)
      throw new InvalidDataException("The CMX document does not reference a DISP thumbnail.");
    if (thumbnailOffset > int.MaxValue)
      throw new InvalidDataException("The CMX thumbnail offset is outside the addressable input range.");

    var displayOffset = checked((int)thumbnailOffset);
    if (displayOffset < 12 || displayOffset > containerEnd - 8 || !_IsAscii4(data, displayOffset, "DISP"))
      throw new InvalidDataException("The CMX thumbnail offset does not point at a DISP chunk.");

    var displayLength = _ReadUInt32(data, displayOffset + 4, bigEndian);
    if (displayLength > int.MaxValue)
      throw new InvalidDataException("The CMX DISP chunk is too large to decode.");
    var displayLengthInt = checked((int)displayLength);
    var displayDataOffset = checked(displayOffset + 8);
    if (displayDataOffset > containerEnd - displayLengthInt)
      throw new InvalidDataException("The CMX DISP chunk extends beyond the RIFF container.");
    if (displayLengthInt < 4 + EmbeddedDibFile.MinHeaderSize)
      throw new InvalidDataException("The CMX DISP chunk is too short to contain its packed Windows bitmap.");

    RawImage preview;
    try {
      preview = EmbeddedDibReader.DecodeHeaderless(data.Slice(displayDataOffset + 4, displayLengthInt - 4));
    } catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException) {
      throw new InvalidDataException("The CMX DISP chunk does not contain a decodable packed Windows bitmap.", exception);
    }

    return new() {
      IsBigEndian = bigEndian,
      CoordinatePrecisionBits = precision,
      InternalVersion = internalVersion,
      Preview = preview,
      PreviewOffset = displayDataOffset + 4,
    };
  }

  private static bool _FindChunk(
    ReadOnlySpan<byte> data,
    int start,
    int end,
    string id,
    bool bigEndian,
    out int chunkOffset,
    out int payloadOffset,
    out int payloadLength
  ) {
    chunkOffset = payloadOffset = payloadLength = 0;

    for (var offset = start; offset <= end - 8;) {
      var length = _ReadUInt32(data, offset + 4, bigEndian);
      var next64 = (long)offset + 8 + length + (length & 1);
      if (next64 > end)
        throw new InvalidDataException($"CMX chunk at offset {offset} extends beyond the RIFF container.");

      if (_IsAscii4(data, offset, id)) {
        if (length > int.MaxValue)
          throw new InvalidDataException($"CMX {id} chunk is too large to address.");
        chunkOffset = offset;
        payloadOffset = offset + 8;
        payloadLength = checked((int)length);
        return true;
      }

      offset = checked((int)next64);
    }

    return false;
  }

  private static int _ReadAsciiNumber(ReadOnlySpan<byte> value, string fieldName) {
    var result = 0;
    var digits = 0;
    foreach (var b in value) {
      if (b is < (byte)'0' or > (byte)'9')
        break;
      result = checked(result * 10 + b - (byte)'0');
      ++digits;
    }

    return digits == 0
      ? throw new InvalidDataException($"The CMX {fieldName} field is not an ASCII decimal number.")
      : result;
  }

  private static uint _ReadUInt32(ReadOnlySpan<byte> data, int offset, bool bigEndian) {
    if (offset < 0 || data.Length - offset < sizeof(uint))
      throw new InvalidDataException("CMX data ended while reading a 32-bit field.");

    return bigEndian
      ? BinaryPrimitives.ReadUInt32BigEndian(data[offset..])
      : BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
  }

  private static bool _IsAscii4(ReadOnlySpan<byte> data, int offset, string value) {
    if (value.Length != 4 || offset < 0 || offset > data.Length - 4)
      return false;

    for (var i = 0; i < 4; ++i)
      if (data[offset + i] != (byte)value[i])
        return false;

    return true;
  }
}

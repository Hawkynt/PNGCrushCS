using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.EmbeddedDib;

namespace FileFormat.Cmx;

/// <summary>Reads the externally documented RIFF/RIFX wrapper of Corel Presentation Exchange files.</summary>
public static class CmxReader {

  private const int _PrecisionMarkerOffset = 74;

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 12)
      return null;

    if (!_IsAscii4(header, 8, "CMX1") || (!_IsAscii4(header, 0, "RIFF") && !_IsAscii4(header, 0, "RIFX")))
      return false;

    return header.Length <= _PrecisionMarkerOffset ? null : header[_PrecisionMarkerOffset] is 1 or 2;
  }

  public static CmxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= _PrecisionMarkerOffset)
      throw new InvalidDataException("CMX data is too short to contain the documented CMX1 header.");

    var bigEndian = _IsAscii4(data, 0, "RIFX");
    if (!bigEndian && !_IsAscii4(data, 0, "RIFF"))
      throw new InvalidDataException("CMX must be enclosed by RIFF or RIFX.");
    if (!_IsAscii4(data, 8, "CMX1"))
      throw new InvalidDataException("The RIFF/RIFX form is not CMX1.");

    var declaredSize = bigEndian
      ? BinaryPrimitives.ReadUInt32BigEndian(data[4..])
      : BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
    if (8L + declaredSize > data.Length)
      throw new InvalidDataException("The CMX RIFF/RIFX extent runs past the available bytes.");

    var precision = data[_PrecisionMarkerOffset] switch {
      1 => 16,
      2 => 32,
      var marker => throw new InvalidDataException($"CMX precision marker {marker} is neither the documented 16- nor 32-bit form."),
    };

    EmbeddedDibFile embedded;
    try {
      embedded = EmbeddedDibReader.FromSpan(data);
    } catch (InvalidDataException exception) {
      throw new InvalidDataException("The CMX container has no decodable bitmap preview.", exception);
    }

    return new CmxFile {
      IsBigEndian = bigEndian,
      CoordinatePrecisionBits = precision,
      Preview = embedded.Preview,
      PreviewOffset = embedded.Offset,
    };
  }

  private static bool _IsAscii4(ReadOnlySpan<byte> data, int offset, string value)
    => data.Length >= offset + 4
       && data[offset] == value[0] && data[offset + 1] == value[1]
       && data[offset + 2] == value[2] && data[offset + 3] == value[3];
}

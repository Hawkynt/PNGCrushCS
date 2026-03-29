using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.Dicom;

/// <summary>Reads DICOM tag-length-value elements from a byte span (Explicit VR Little Endian only).</summary>
internal static class DicomTagReader {

  // VRs that use 2-byte reserved + 4-byte length
  private static readonly string[] _LongVRs = ["OB", "OD", "OF", "OL", "OW", "SQ", "UC", "UN", "UR", "UT"];

  /// <summary>Reads a single DICOM tag element.</summary>
  /// <returns>Tuple of (group, element, vr, value bytes, next offset).</returns>
  public static (ushort Group, ushort Element, string Vr, byte[] Value, int NextOffset) ReadTag(byte[] data, int offset) {
    if (offset + 8 > data.Length)
      return (0, 0, "", [], data.Length);

    var group = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    var element = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2));
    var vr = Encoding.ASCII.GetString(data, offset + 4, 2);

    var isLongVr = Array.IndexOf(_LongVRs, vr) >= 0;

    int length;
    int valueStart;
    if (isLongVr) {
      // 2-byte reserved + 4-byte length
      if (offset + 12 > data.Length)
        return (group, element, vr, [], data.Length);

      length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 8));
      valueStart = offset + 12;
    } else {
      // 2-byte length
      if (offset + 8 > data.Length)
        return (group, element, vr, [], data.Length);

      length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 6));
      valueStart = offset + 8;
    }

    // Undefined length (0xFFFFFFFF) - skip for now
    if (length < 0 || length == unchecked((int)0xFFFFFFFF))
      return (group, element, vr, [], data.Length);

    if (valueStart + length > data.Length)
      length = data.Length - valueStart;

    var value = new byte[length];
    data.AsSpan(valueStart, length).CopyTo(value.AsSpan(0));

    return (group, element, vr, value, valueStart + length);
  }

  /// <summary>Reads a US (unsigned short) value from tag value bytes.</summary>
  public static ushort ReadUS(byte[] value) =>
    value.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(value) : (ushort)0;

  /// <summary>Reads a CS (code string) value from tag value bytes.</summary>
  public static string ReadCS(byte[] value) =>
    Encoding.ASCII.GetString(value).Trim('\0', ' ');

  /// <summary>Reads a DS (decimal string) value from tag value bytes.</summary>
  public static double ReadDS(byte[] value) {
    var text = Encoding.ASCII.GetString(value).Trim('\0', ' ');
    return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
      ? result
      : 0.0;
  }
}

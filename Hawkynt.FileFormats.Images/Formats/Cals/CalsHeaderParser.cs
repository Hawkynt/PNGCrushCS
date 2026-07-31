using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Cals;

/// <summary>Parses and formats CALS Type 1 text headers (16 records x 128 bytes each).</summary>
/// <remarks>
/// The header is 2048 bytes and the image data begins straight after it. Only the first six records
/// were read, which stopped short of "rpelcnt" — the field carrying the image's dimensions, and the
/// eighth record in every file a standards-conforming writer produces — so those files were rejected
/// as having no dimensions at all. Reading 768 bytes also put the start of the pixel data 1280 bytes
/// too early.
/// </remarks>
internal static class CalsHeaderParser {

  /// <summary>Total header size in bytes.</summary>
  internal const int HeaderSize = 2048;

  /// <summary>Size of each record in the header.</summary>
  private const int _RECORD_SIZE = 128;

  /// <summary>Number of records in the header.</summary>
  private const int _RECORD_COUNT = HeaderSize / _RECORD_SIZE;

  /// <summary>Parses a 768-byte CALS header into key-value pairs.</summary>
  internal static Dictionary<string, string> Parse(byte[] headerData) {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < _RECORD_COUNT; ++i) {
      var offset = i * _RECORD_SIZE;
      var recordText = Encoding.ASCII.GetString(headerData, offset, _RECORD_SIZE).TrimEnd();

      var separatorIndex = recordText.IndexOf(": ", StringComparison.Ordinal);
      if (separatorIndex < 0)
        continue;

      var key = recordText[..separatorIndex].Trim();
      var value = recordText[(separatorIndex + 2)..].Trim();
      result[key] = value;
    }

    return result;
  }

  /// <summary>Builds a 2048-byte header from a <see cref="CalsFile"/>.</summary>
  /// <remarks>
  /// One field to a record, which is what the format calls for. What stood here packed four extra
  /// fields into the spare space of records 2 to 5 behind NUL separators — a private arrangement
  /// nothing else would have read, and unnecessary once all sixteen records are available.
  /// </remarks>
  internal static byte[] Format(CalsFile file) {
    var header = new byte[HeaderSize];
    for (var i = 0; i < HeaderSize; ++i)
      header[i] = (byte)' ';

    _WriteRecord(header, 0, $"srcdocid: {file.SrcDocId}");
    _WriteRecord(header, 1, $"dstdocid: {file.DstDocId}");
    _WriteRecord(header, 2, "txtfilid: NONE");
    _WriteRecord(header, 3, "figid: NONE");
    _WriteRecord(header, 4, "srcgph: NONE");
    _WriteRecord(header, 5, "doccls: NONE");
    _WriteRecord(header, 6, "rtype: 1");
    // The standard field is a pair of angles, which is what another CALS reader will look for.
    _WriteRecord(header, 7, "rorient: 000,270");
    _WriteRecord(header, 8, $"rpelcnt: {file.Width:000000},{file.Height:000000}");
    _WriteRecord(header, 9, $"rdensty: {file.Dpi:0000}");
    _WriteRecord(header, 10, "notes: NONE");

    // "orient" is this library's own, and not a CALS field — but there are five spare records and
    // the portrait/landscape distinction it carries has nowhere else to go, since rorient records
    // rotation angles rather than page shape.
    _WriteRecord(header, 11, $"orient: {file.Orientation}");

    return header;
  }

  /// <summary>Writes a text record at the given record index (0-based), CR+LF terminated.</summary>
  private static void _WriteRecord(byte[] header, int recordIndex, string text) {
    var offset = recordIndex * _RECORD_SIZE;
    var bytes = Encoding.ASCII.GetBytes(text);
    var len = Math.Min(bytes.Length, _RECORD_SIZE - 2);
    Array.Copy(bytes, 0, header, offset, len);
    header[offset + _RECORD_SIZE - 2] = (byte)'\r';
    header[offset + _RECORD_SIZE - 1] = (byte)'\n';
  }

  /// <summary>Extracts all key-value pairs from a 768-byte header, including embedded fields separated by null bytes.</summary>
  internal static Dictionary<string, string> ParseAll(byte[] headerData) {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < _RECORD_COUNT; ++i) {
      var offset = i * _RECORD_SIZE;
      var recordBytes = new byte[_RECORD_SIZE];
      Array.Copy(headerData, offset, recordBytes, 0, _RECORD_SIZE);

      // Split by null bytes to find multiple fields in one record
      var recordText = Encoding.ASCII.GetString(recordBytes);
      var parts = recordText.Split('\0');

      foreach (var part in parts) {
        var trimmed = part.TrimEnd(' ', '\r', '\n');
        if (string.IsNullOrEmpty(trimmed))
          continue;

        var separatorIndex = trimmed.IndexOf(": ", StringComparison.Ordinal);
        if (separatorIndex < 0)
          continue;

        var key = trimmed[..separatorIndex].Trim();
        var value = trimmed[(separatorIndex + 2)..].Trim();
        if (!string.IsNullOrEmpty(key))
          result[key] = value;
      }
    }

    return result;
  }
}

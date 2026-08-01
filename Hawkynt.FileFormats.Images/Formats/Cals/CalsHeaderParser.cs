using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Cals;

/// <summary>Parses and formats CALS text headers: sixteen records of 128 bytes each.</summary>
/// <remarks>
/// The header is a fixed 2048 bytes of <c>keyword: value</c> lines, one to a record, blank-padded.
/// This used to read six of them and write the rest of the fields into the unused tails of those
/// six, after a null byte — which no other tool looks at, so the files were readable only here and
/// every file written anywhere else was rejected for the size field it plainly contained.
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

  /// <summary>Builds a 768-byte header from a <see cref="CalsFile"/>.</summary>
  internal static byte[] Format(CalsFile file) {
    var header = new byte[HeaderSize];

    // Fill with spaces
    for (var i = 0; i < HeaderSize; ++i)
      header[i] = (byte)' ';

    // One field to a record, in the order the specification lists them.
    _WriteRecord(header, 0, $"srcdocid: {file.SrcDocId}");
    _WriteRecord(header, 1, $"dstdocid: {file.DstDocId}");
    _WriteRecord(header, 2, "txtfilid: NONE");
    _WriteRecord(header, 3, "figid: NONE");
    _WriteRecord(header, 4, "srcgph: NONE");
    _WriteRecord(header, 5, "doccls: NONE");
    _WriteRecord(header, 6, "rtype: 1");
    _WriteRecord(header, 7, $"rorient: {file.Orientation}");
    _WriteRecord(header, 8, $"rpelcnt: {file.Width:D6},{file.Height:D6}");
    _WriteRecord(header, 9, $"rdensty: {file.Dpi:D4}");
    _WriteRecord(header, 10, "notes: NONE");

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

  /// <summary>Appends an additional key-value pair into the unused space of a record after a null separator.</summary>
  private static void _AppendToRecord(byte[] header, int recordIndex, string text) {
    var offset = recordIndex * _RECORD_SIZE;

    // Find end of existing content (first space-padded area before CR LF)
    var contentEnd = offset;
    for (var i = offset; i < offset + _RECORD_SIZE - 2; ++i) {
      if (header[i] != (byte)' ')
        contentEnd = i + 1;
    }

    // Insert null separator then the additional field
    if (contentEnd < offset + _RECORD_SIZE - 2) {
      header[contentEnd] = 0;
      ++contentEnd;
    }

    var fieldBytes = Encoding.ASCII.GetBytes(text);
    var available = offset + _RECORD_SIZE - 2 - contentEnd;
    var copyLen = Math.Min(fieldBytes.Length, available);
    if (copyLen > 0)
      Array.Copy(fieldBytes, 0, header, contentEnd, copyLen);
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

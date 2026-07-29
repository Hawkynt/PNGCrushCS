using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Cals;

/// <summary>Parses and formats CALS 768-byte text headers (6 records x 128 bytes each).</summary>
internal static class CalsHeaderParser {

  /// <summary>Total header size in bytes.</summary>
  internal const int HeaderSize = 768;

  /// <summary>Size of each record in the header.</summary>
  private const int _RECORD_SIZE = 128;

  /// <summary>Number of records in the header.</summary>
  private const int _RECORD_COUNT = 6;

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

    _WriteRecord(header, 0, $"srcdocid: {file.SrcDocId}");
    _WriteRecord(header, 1, $"dstdocid: {file.DstDocId}");
    _WriteRecord(header, 2, $"txtfilid: ");
    _WriteRecord(header, 3, $"figid: ");
    _WriteRecord(header, 4, $"srcgph: ");
    _WriteRecord(header, 5, $"doccls: ");

    // Embed key fields into remaining space of records
    // We use records 2-5 to embed rtype, rpelcnt, rdensty, orient after the standard field
    _AppendToRecord(header, 2, $"rtype: 1");
    _AppendToRecord(header, 3, $"rpelcnt: {file.Width},{file.Height}");
    _AppendToRecord(header, 4, $"rdensty: {file.Dpi}");
    _AppendToRecord(header, 5, $"orient: {file.Orientation}");

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

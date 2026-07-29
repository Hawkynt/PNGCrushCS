using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.Pdf;

/// <summary>Parses PDF cross-reference tables (both traditional and cross-reference streams) to build an object offset map.</summary>
internal static class PdfXrefParser {

  /// <summary>Parses the cross-reference data from the PDF and returns a map of object number to file byte offset.</summary>
  public static Dictionary<int, long> Parse(byte[] data) {
    var xref = new Dictionary<int, long>();
    var startxrefOffset = _FindStartxref(data);
    if (startxrefOffset < 0)
      throw new InvalidDataException("Cannot find startxref in PDF file.");

    var xrefOffset = _ReadStartxrefValue(data, startxrefOffset);
    _ParseXrefAt(data, (int)xrefOffset, xref);
    return xref;
  }

  /// <summary>Parses the trailer dictionary from the PDF.</summary>
  public static Dictionary<string, object?>? ParseTrailer(byte[] data, Dictionary<int, long> xref) {
    var startxrefOffset = _FindStartxref(data);
    if (startxrefOffset < 0)
      return null;

    var xrefOffset = (int)_ReadStartxrefValue(data, startxrefOffset);

    // Check if this is a traditional xref table or a cross-reference stream
    var pos = xrefOffset;
    PdfParser.SkipWhitespace(data, ref pos);

    if (_MatchesAt(data, pos, "xref"u8)) {
      // Traditional: skip xref table, then look for "trailer"
      _SkipTraditionalXref(data, ref pos);
      PdfParser.SkipWhitespace(data, ref pos);
      if (_MatchesAt(data, pos, "trailer"u8)) {
        pos += 7;
        return PdfParser.ParseObject(data, ref pos) as Dictionary<string, object?>;
      }
    } else {
      // Cross-reference stream: the stream object IS the trailer
      var obj = PdfParser.ParseObject(data, ref pos); // skip obj number
      PdfParser.SkipWhitespace(data, ref pos);
      obj = PdfParser.ParseObject(data, ref pos); // skip generation
      PdfParser.SkipWhitespace(data, ref pos);
      obj = PdfParser.ParseObject(data, ref pos); // skip "obj"
      PdfParser.SkipWhitespace(data, ref pos);
      obj = PdfParser.ParseObject(data, ref pos);
      if (obj is PdfStream ps)
        return ps.Dictionary;
    }

    return null;
  }

  private static void _ParseXrefAt(byte[] data, int offset, Dictionary<int, long> xref) {
    var pos = offset;
    PdfParser.SkipWhitespace(data, ref pos);

    if (_MatchesAt(data, pos, "xref"u8))
      _ParseTraditionalXref(data, pos, xref);
    else
      _ParseXrefStream(data, pos, xref);
  }

  private static void _ParseTraditionalXref(byte[] data, int startPos, Dictionary<int, long> xref) {
    var pos = startPos + 4; // skip "xref"
    PdfParser.SkipWhitespace(data, ref pos);

    // Parse subsections
    while (pos < data.Length) {
      PdfParser.SkipWhitespace(data, ref pos);

      // Check for "trailer" keyword
      if (_MatchesAt(data, pos, "trailer"u8))
        break;

      // Read startObj and count
      var startObj = _ReadInt(data, ref pos);
      PdfParser.SkipWhitespace(data, ref pos);
      var count = _ReadInt(data, ref pos);
      PdfParser.SkipWhitespace(data, ref pos);

      for (var i = 0; i < count && pos + 18 <= data.Length; ++i) {
        // Each entry is exactly 20 bytes: "oooooooooo ggggg f/n \r\n" or similar
        var entryStr = Encoding.ASCII.GetString(data, pos, Math.Min(20, data.Length - pos));

        // Parse offset and gen from the entry
        var parts = entryStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3
            && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryOffset)
            && parts[2].Length > 0 && parts[2][0] == 'n') {
          var objNum = startObj + i;
          xref.TryAdd(objNum, entryOffset);
        }

        // Advance past this 20-byte entry
        pos += 20;
        // Some PDFs use slightly different line endings, so re-align
        while (pos < data.Length && data[pos] is 10 or 13 or 32)
          ++pos;
      }
    }

    // Parse trailer to check for /Prev
    PdfParser.SkipWhitespace(data, ref pos);
    if (_MatchesAt(data, pos, "trailer"u8)) {
      pos += 7;
      if (PdfParser.ParseObject(data, ref pos) is Dictionary<string, object?> trailer) {
        if (trailer.TryGetValue("Prev", out var prevObj) && prevObj is int prevOffset)
          _ParseXrefAt(data, prevOffset, xref);
        else if (prevObj is double prevD)
          _ParseXrefAt(data, (int)prevD, xref);
      }
    }
  }

  private static void _ParseXrefStream(byte[] data, int startPos, Dictionary<int, long> xref) {
    var pos = startPos;

    // Skip "N G obj"
    PdfParser.SkipWhitespace(data, ref pos);
    _SkipInt(data, ref pos);
    PdfParser.SkipWhitespace(data, ref pos);
    _SkipInt(data, ref pos);
    PdfParser.SkipWhitespace(data, ref pos);
    // skip "obj"
    if (_MatchesAt(data, pos, "obj"u8))
      pos += 3;

    PdfParser.SkipWhitespace(data, ref pos);
    var obj = PdfParser.ParseObject(data, ref pos);
    if (obj is not PdfStream stream)
      return;

    var dict = stream.Dictionary;

    // Decode the stream
    var streamData = PdfStreamDecoder.Decode(stream.RawData, dict);

    // Get W array (field widths)
    if (!dict.TryGetValue("W", out var wObj) || wObj is not List<object?> wList || wList.Count < 3)
      return;

    var w0 = _ObjToInt(wList[0]);
    var w1 = _ObjToInt(wList[1]);
    var w2 = _ObjToInt(wList[2]);
    var entrySize = w0 + w1 + w2;
    if (entrySize <= 0)
      return;

    // Get Index array (defaults to [0 Size])
    List<int> indexPairs;
    if (dict.TryGetValue("Index", out var idxObj) && idxObj is List<object?> idxList) {
      indexPairs = [];
      foreach (var item in idxList)
        indexPairs.Add(_ObjToInt(item));
    } else {
      var size = 0;
      if (dict.TryGetValue("Size", out var sizeObj))
        size = _ObjToInt(sizeObj);

      indexPairs = [0, size];
    }

    // Parse entries
    var dataPos = 0;
    for (var p = 0; p + 1 < indexPairs.Count; p += 2) {
      var firstObj = indexPairs[p];
      var count = indexPairs[p + 1];

      for (var i = 0; i < count && dataPos + entrySize <= streamData.Length; ++i) {
        var type = w0 > 0 ? _ReadBEInt(streamData, dataPos, w0) : 1; // default type 1
        var field2 = _ReadBEInt(streamData, dataPos + w0, w1);
        // field3 = _ReadBEInt(streamData, dataPos + w0 + w1, w2); // generation, not needed

        if (type == 1) // type 1 = uncompressed object
          xref.TryAdd(firstObj + i, field2);

        dataPos += entrySize;
      }
    }

    // Follow /Prev chain
    if (dict.TryGetValue("Prev", out var prevObj)) {
      var prevOffset = _ObjToInt(prevObj);
      if (prevOffset > 0)
        _ParseXrefAt(data, prevOffset, xref);
    }
  }

  private static void _SkipTraditionalXref(byte[] data, ref int pos) {
    // Skip "xref" keyword
    if (_MatchesAt(data, pos, "xref"u8))
      pos += 4;

    // Skip all xref entries until "trailer"
    while (pos < data.Length) {
      PdfParser.SkipWhitespace(data, ref pos);
      if (_MatchesAt(data, pos, "trailer"u8))
        break;

      // Skip a line
      while (pos < data.Length && data[pos] != 10 && data[pos] != 13)
        ++pos;
      while (pos < data.Length && data[pos] is 10 or 13)
        ++pos;
    }
  }

  private static int _FindStartxref(byte[] data) {
    // Search backwards from end of file for "startxref"
    var needle = "startxref"u8;
    var searchStart = Math.Max(0, data.Length - 1024);

    for (var i = data.Length - needle.Length; i >= searchStart; --i) {
      if (!_MatchesAt(data, i, needle))
        continue;

      return i;
    }

    // Extended search if not found in last 1024 bytes
    for (var i = searchStart - 1; i >= 0; --i)
      if (_MatchesAt(data, i, needle))
        return i;

    return -1;
  }

  private static long _ReadStartxrefValue(byte[] data, int startxrefPos) {
    var pos = startxrefPos + 9; // skip "startxref"
    PdfParser.SkipWhitespace(data, ref pos);
    return _ReadLong(data, ref pos);
  }

  private static int _ReadInt(byte[] data, ref int pos) {
    var start = pos;
    if (pos < data.Length && data[pos] is (byte)'+' or (byte)'-')
      ++pos;

    while (pos < data.Length && data[pos] is >= (byte)'0' and <= (byte)'9')
      ++pos;

    if (pos == start)
      return 0;

    return int.TryParse(Encoding.ASCII.GetString(data, start, pos - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) ? val : 0;
  }

  private static long _ReadLong(byte[] data, ref int pos) {
    var start = pos;
    if (pos < data.Length && data[pos] is (byte)'+' or (byte)'-')
      ++pos;

    while (pos < data.Length && data[pos] is >= (byte)'0' and <= (byte)'9')
      ++pos;

    if (pos == start)
      return 0;

    return long.TryParse(Encoding.ASCII.GetString(data, start, pos - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) ? val : 0;
  }

  private static void _SkipInt(byte[] data, ref int pos) {
    if (pos < data.Length && data[pos] is (byte)'+' or (byte)'-')
      ++pos;

    while (pos < data.Length && data[pos] is >= (byte)'0' and <= (byte)'9')
      ++pos;
  }

  private static bool _MatchesAt(byte[] data, int pos, ReadOnlySpan<byte> keyword) {
    if (pos + keyword.Length > data.Length)
      return false;

    for (var i = 0; i < keyword.Length; ++i)
      if (data[pos + i] != keyword[i])
        return false;

    return true;
  }

  private static int _ReadBEInt(byte[] data, int offset, int width) {
    var result = 0;
    for (var i = 0; i < width && offset + i < data.Length; ++i)
      result = (result << 8) | data[offset + i];

    return result;
  }

  private static int _ObjToInt(object? obj) => obj switch {
    int i => i,
    long l => (int)l,
    double d => (int)d,
    float f => (int)f,
    _ => 0,
  };
}

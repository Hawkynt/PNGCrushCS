using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Pds;

/// <summary>Parses and formats PDS3 text label headers.</summary>
internal static class PdsHeaderParser {

  private const string _CRLF = "\r\n";

  /// <summary>Parses PDS3 keyword=value lines from raw data, returning all labels and the image data offset.</summary>
  public static (Dictionary<string, string> labels, int imageOffset) Parse(byte[] data) {
    var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var text = Encoding.ASCII.GetString(data);
    var inImageObject = false;
    var imageLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var endIndex = 0;

    var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
    var offset = 0;

    foreach (var rawLine in lines) {
      var line = rawLine.Trim();
      offset += rawLine.Length;

      // account for line terminator
      var nextCharIndex = offset;
      if (nextCharIndex < text.Length && text[nextCharIndex] == '\r')
        ++offset;
      if (offset < text.Length && text[offset] == '\n')
        ++offset;

      if (line.Length == 0)
        continue;

      if (line.Equals("END", StringComparison.OrdinalIgnoreCase)) {
        endIndex = offset;
        break;
      }

      if (line.Equals("END_OBJECT = IMAGE", StringComparison.OrdinalIgnoreCase)
          || line.Equals("END_OBJECT=IMAGE", StringComparison.OrdinalIgnoreCase)) {
        inImageObject = false;
        continue;
      }

      var eqIndex = line.IndexOf('=');
      if (eqIndex < 0)
        continue;

      var key = line.Substring(0, eqIndex).Trim();
      var value = line.Substring(eqIndex + 1).Trim();

      // strip surrounding quotes
      if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        value = value[1..^1];

      if (key.Equals("OBJECT", StringComparison.OrdinalIgnoreCase) && value.Equals("IMAGE", StringComparison.OrdinalIgnoreCase)) {
        inImageObject = true;
        continue;
      }

      if (inImageObject)
        imageLabels[key] = value;
      else
        labels[key] = value;
    }

    // merge IMAGE object labels into top-level with prefix-free keys
    foreach (var kvp in imageLabels)
      labels[kvp.Key] = kvp.Value;

    // determine image offset
    var imageOffset = _CalculateImageOffset(labels, endIndex);
    return (labels, imageOffset);
  }

  private static int _CalculateImageOffset(Dictionary<string, string> labels, int headerEndOffset) {
    // ^IMAGE gives the starting record (1-based) or byte offset
    if (labels.TryGetValue("^IMAGE", out var imagePointer)) {
      var trimmed = imagePointer.Trim();
      if (int.TryParse(trimmed, out var recordNumber)) {
        // record-based pointer
        if (labels.TryGetValue("RECORD_BYTES", out var recBytesStr) && int.TryParse(recBytesStr, out var recordBytes))
          return (recordNumber - 1) * recordBytes;

        // fall back to byte offset interpretation if no RECORD_BYTES
        return recordNumber;
      }
    }

    // fallback: LABEL_RECORDS * RECORD_BYTES
    if (labels.TryGetValue("LABEL_RECORDS", out var lblRecStr) && int.TryParse(lblRecStr, out var lblRecs)
        && labels.TryGetValue("RECORD_BYTES", out var recBStr) && int.TryParse(recBStr, out var recB))
      return lblRecs * recB;

    // last resort: immediately after END line
    return headerEndOffset;
  }

  /// <summary>Formats labels into PDS3 header text with CR+LF line endings.</summary>
  public static byte[] Format(
    int width,
    int height,
    int sampleBits,
    int bands,
    PdsBandStorage bandStorage,
    PdsSampleType sampleType,
    int pixelDataLength,
    Dictionary<string, string>? extraLabels
  ) {
    var sb = new StringBuilder();

    sb.Append("PDS_VERSION_ID = PDS3").Append(_CRLF);

    var recordBytes = width * bands * (sampleBits / 8);
    if (recordBytes < 1)
      recordBytes = 1;

    sb.Append($"RECORD_TYPE = FIXED_LENGTH").Append(_CRLF);
    sb.Append($"RECORD_BYTES = {recordBytes}").Append(_CRLF);

    // We'll fill in LABEL_RECORDS and ^IMAGE after we know the header size
    var placeholderLabelRecords = "LABEL_RECORDS_PLACEHOLDER";
    var placeholderImage = "IMAGE_POINTER_PLACEHOLDER";

    sb.Append($"LABEL_RECORDS = {placeholderLabelRecords}").Append(_CRLF);
    sb.Append($"^IMAGE = {placeholderImage}").Append(_CRLF);

    // extra labels (excluding reserved ones)
    if (extraLabels != null)
      foreach (var kvp in extraLabels)
        if (!_IsReservedKey(kvp.Key))
          sb.Append($"{kvp.Key} = {kvp.Value}").Append(_CRLF);

    sb.Append("OBJECT = IMAGE").Append(_CRLF);
    sb.Append($"  LINES = {height}").Append(_CRLF);
    sb.Append($"  LINE_SAMPLES = {width}").Append(_CRLF);
    sb.Append($"  SAMPLE_BITS = {sampleBits}").Append(_CRLF);
    sb.Append($"  SAMPLE_TYPE = {_SampleTypeString(sampleType)}").Append(_CRLF);
    sb.Append($"  BANDS = {bands}").Append(_CRLF);

    if (bands > 1)
      sb.Append($"  BAND_STORAGE_TYPE = {_BandStorageString(bandStorage)}").Append(_CRLF);

    sb.Append("END_OBJECT = IMAGE").Append(_CRLF);
    sb.Append("END").Append(_CRLF);

    // compute header size as multiple of record bytes
    var rawHeaderText = sb.ToString();
    var rawLen = Encoding.ASCII.GetByteCount(rawHeaderText);
    var labelRecords = recordBytes > 0 ? (rawLen / recordBytes) + 1 : 1;
    var headerSize = labelRecords * recordBytes;

    // ensure header is large enough
    while (headerSize < rawLen) {
      ++labelRecords;
      headerSize = labelRecords * recordBytes;
    }

    var imageRecord = labelRecords + 1; // 1-based record number

    // replace placeholders
    var finalText = rawHeaderText
      .Replace(placeholderLabelRecords, labelRecords.ToString())
      .Replace(placeholderImage, imageRecord.ToString());

    // re-check size after replacement (numeric values may differ in length)
    var finalLen = Encoding.ASCII.GetByteCount(finalText);
    while (headerSize < finalLen) {
      ++labelRecords;
      headerSize = labelRecords * recordBytes;
      imageRecord = labelRecords + 1;
      finalText = rawHeaderText
        .Replace(placeholderLabelRecords, labelRecords.ToString())
        .Replace(placeholderImage, imageRecord.ToString());
      finalLen = Encoding.ASCII.GetByteCount(finalText);
    }

    // pad header to exact size with spaces
    var headerBytes = new byte[headerSize];
    var contentBytes = Encoding.ASCII.GetBytes(finalText);
    Array.Copy(contentBytes, headerBytes, Math.Min(contentBytes.Length, headerSize));
    for (var i = contentBytes.Length; i < headerSize; ++i)
      headerBytes[i] = (byte)' ';

    return headerBytes;
  }

  private static string _SampleTypeString(PdsSampleType sampleType) => sampleType switch {
    PdsSampleType.UnsignedByte => "UNSIGNED_INTEGER",
    PdsSampleType.MsbUnsigned16 => "MSB_UNSIGNED_INTEGER",
    PdsSampleType.LsbUnsigned16 => "LSB_UNSIGNED_INTEGER",
    _ => throw new ArgumentOutOfRangeException(nameof(sampleType))
  };

  private static string _BandStorageString(PdsBandStorage bandStorage) => bandStorage switch {
    PdsBandStorage.BandSequential => "BAND_SEQUENTIAL",
    PdsBandStorage.LineInterleaved => "LINE_INTERLEAVED",
    PdsBandStorage.SampleInterleaved => "SAMPLE_INTERLEAVED",
    _ => throw new ArgumentOutOfRangeException(nameof(bandStorage))
  };

  private static bool _IsReservedKey(string key) => key.ToUpperInvariant() switch {
    "PDS_VERSION_ID" or "RECORD_TYPE" or "RECORD_BYTES" or "LABEL_RECORDS"
      or "^IMAGE" or "OBJECT" or "END_OBJECT" or "LINES" or "LINE_SAMPLES"
      or "SAMPLE_BITS" or "SAMPLE_TYPE" or "BANDS" or "BAND_STORAGE_TYPE"
      or "END" => true,
    _ => false
  };
}

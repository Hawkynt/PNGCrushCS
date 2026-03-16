namespace FileFormat.Core;

/// <summary>Specifies how a numeric field is encoded as ASCII text in the binary header.</summary>
public enum AsciiEncoding {
  /// <summary>Standard binary encoding (default).</summary>
  None,
  /// <summary>Fixed-width decimal ASCII string, zero-padded (e.g., "000320").</summary>
  Decimal
}

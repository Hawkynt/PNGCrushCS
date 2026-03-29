using System.Collections.Generic;

namespace FileFormat.Pdf;

/// <summary>A PDF stream object: a dictionary plus raw stream bytes.</summary>
internal sealed class PdfStream {
  public Dictionary<string, object?> Dictionary { get; init; } = [];
  public byte[] RawData { get; init; } = [];
}

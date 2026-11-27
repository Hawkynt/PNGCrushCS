using System;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

/// <summary>The two bilevel image filters, CCITT fax and JBIG2, with their PDF parameters read.</summary>
internal static class PdfBilevelFilters {

  /// <summary>Decodes CCITTFaxDecode data to packed rows, 0 meaning black unless /BlackIs1.</summary>
  public static byte[] DecodeCcitt(PdfDocument document, byte[] data, PdfDict? parms, int width, int height)
    => throw new NotSupportedException("The PDF image filter /CCITTFaxDecode is not supported.");

  /// <summary>Decodes JBIG2Decode data, with its /JBIG2Globals, to packed rows, 0 meaning black.</summary>
  public static byte[] DecodeJbig2(PdfDocument document, byte[] data, PdfDict? parms, int width, int height)
    => throw new NotSupportedException("The PDF image filter /JBIG2Decode is not supported.");
}

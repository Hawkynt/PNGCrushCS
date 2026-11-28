using FileFormat.Pdf.Document;
using FileFormat.Pdf.Filters;

namespace FileFormat.Pdf.Rendering;

/// <summary>The two bilevel image filters, CCITT fax and JBIG2, with their PDF parameters read.</summary>
internal static class PdfBilevelFilters {

  /// <summary>Decodes CCITTFaxDecode data to packed rows, 0 meaning black unless /BlackIs1.</summary>
  /// <remarks>The parameter defaults are those of PDF 32000-1 table 11.</remarks>
  public static byte[] DecodeCcitt(PdfDocument document, byte[] data, PdfDict? parms, int width, int height) {
    var k = document.Integer(parms, "K", 0);
    var endOfLine = document.Get(parms, "EndOfLine") is true;
    var byteAlign = document.Get(parms, "EncodedByteAlign") is true;
    var columns = document.Integer(parms, "Columns", 1728);
    var rows = document.Integer(parms, "Rows", 0);
    var endOfBlock = document.Get(parms, "EndOfBlock") is not false;
    var blackIs1 = document.Get(parms, "BlackIs1") is true;

    var decoded = PdfCcittFaxDecoder.Decode(data, k, endOfLine, byteAlign, columns, rows > 0 ? rows : height, endOfBlock, blackIs1);
    return columns == width ? decoded : _Recolumn(decoded, columns, width, height);
  }

  /// <summary>Decodes JBIG2Decode data, with its /JBIG2Globals, to packed rows, 0 meaning black.</summary>
  public static byte[] DecodeJbig2(PdfDocument document, byte[] data, PdfDict? parms, int width, int height) {
    var globals = document.Stream(parms?.Raw("JBIG2Globals")) is { } stream ? document.Decode(stream) : null;
    return PdfJbig2Decoder.Decode(data, globals, width, height);
  }

  /// <summary>Repacks rows decoded at one width into another, as happens when /Columns and /Width disagree.</summary>
  private static byte[] _Recolumn(byte[] rows, int columns, int width, int height) {
    var from = (columns + 7) / 8;
    var to = (width + 7) / 8;
    var output = new byte[to * height];
    System.Array.Fill(output, (byte)0xFF);
    for (var y = 0; y < height && (y + 1) * from <= rows.Length; ++y)
      System.Array.Copy(rows, y * from, output, y * to, System.Math.Min(from, to));
    return output;
  }
}

using System;

namespace FileFormat.FaxG3;

/// <summary>Assembles Raw Group 3 fax image file bytes.</summary>
public static class FaxG3Writer {

  /// <summary>Codes a picture as a bare Group 3 stream.</summary>
  /// <remarks>
  /// There is no header: a raw fax file is the coding and nothing else, which is why its width has
  /// to be assumed on the way back in. This used to write a six-byte size header of its own and then
  /// the uncompressed rows, which is a private format wearing the extension of a public one.
  /// <para/>
  /// With no header there is nothing to say how tall the page is either, so the stream has to say it
  /// by itself, and T.4's way of saying it is punctuation: an end-of-line word in front of every row
  /// and a return-to-control after the last one. Written without them — a row, a marker, a row, a
  /// marker, and then simply stopping — the stream reads as one row short to any decoder that takes
  /// the marker as the start of a row rather than the end of one, because the first row falls in
  /// front of the first marker and the file ends where the last row should begin. netpbm's
  /// <c>g3topbm</c> is one such decoder and it returned 199 rows for a 200-row picture.
  /// </remarks>
  public static byte[] ToBytes(FaxG3File file) {
    ArgumentNullException.ThrowIfNull(file);
    return FileFormat.Ccitt.CcittG3Encoder.Encode(
      file.PixelData, file.Width, file.Height, leadingEndOfLine: true, returnToControl: true);
  }
}

using System;
using FileFormat.Ccitt;

namespace FileFormat.RicohFax;

/// <summary>Assembles Ricoh Fax page bytes from a <see cref="RicohFaxFile"/>.</summary>
public static class RicohFaxWriter {

  public static byte[] ToBytes(RicohFaxFile file) {
    var pixelData = file.PixelData ?? [];
    var coded = CcittG3Encoder.Encode(pixelData, RicohFaxFile.PageWidth, file.Height, leadingEndOfLine: true);

    // The coding goes down with its bits the other way up, which is how the format holds it.
    var reversed = CcittFillOrder.Reverse(coded);

    var result = new byte[RicohFaxFile.HeaderSize + reversed.Length];
    RicohFaxFile.Signature.CopyTo(result.AsSpan(RicohFaxFile.SignatureOffset));
    reversed.CopyTo(result.AsSpan(RicohFaxFile.HeaderSize));

    return result;
  }
}

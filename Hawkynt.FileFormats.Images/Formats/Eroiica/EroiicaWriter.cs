using System;
using FileFormat.Tiff;

namespace FileFormat.Eroiica;

/// <summary>Writes the raster-bearing subset of an Eroiica document (.eif).</summary>
/// <remarks>
/// The one recovered sample establishes two things independently: the first eight bytes identify the
/// document, and each raster page is a complete standalone TIFF whose offsets are relative to its own
/// first byte. The records describing sets, page lists and text are not represented by
/// <see cref="EroiicaFile"/>, so this writer does not fabricate them. It writes exactly the part this
/// model can state: the identifying bytes followed by the complete TIFF streams in page order.
/// </remarks>
public static class EroiicaWriter {

  public static byte[] ToBytes(EroiicaFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Pages is null || file.Pages.Count == 0)
      throw new ArgumentException("An Eroiica document needs at least one TIFF page.", nameof(file));

    var length = EroiicaFile.Magic.Length;
    for (var i = 0; i < file.Pages.Count; ++i) {
      var page = file.Pages[i];
      if (page is null || page.Length < 8)
        throw new ArgumentException($"Eroiica page {i} is not a complete TIFF stream.", nameof(file));

      // Parse before writing so a constructed EroiicaFile cannot smuggle an arbitrary byte blob into
      // a document. Parsed files already contain exactly these standalone streams; FromRawImage uses
      // TiffWriter, so both ordinary writer paths satisfy this without conversion or re-encoding.
      _ = TiffReader.FromSpan(page);
      length = checked(length + page.Length);
    }

    var output = new byte[length];
    EroiicaFile.Magic.CopyTo(output);

    var at = EroiicaFile.Magic.Length;
    foreach (var page in file.Pages) {
      page.CopyTo(output, at);
      at += page.Length;
    }

    return output;
  }
}

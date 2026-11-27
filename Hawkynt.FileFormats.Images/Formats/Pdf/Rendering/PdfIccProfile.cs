using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

/// <summary>Colour conversion through an embedded ICC profile, where the profile is one this can evaluate.</summary>
internal static class PdfIccProfile {

  /// <summary>A colour model that evaluates the profile, or null to fall back on the alternate space.</summary>
  public static PdfColorModel? TryCreate(PdfDocument document, PdfStreamObject stream, int components) => null;
}

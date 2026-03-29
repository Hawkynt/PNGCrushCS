namespace FileFormat.Pdf;

/// <summary>An indirect object reference in a PDF file (object number + generation).</summary>
internal readonly record struct PdfRef(int ObjectNumber, int Generation);

using System;
using System.Collections.Generic;
using FileFormat.Core.Vector;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

internal sealed partial class PdfRenderer {

  private void _RunGroup(byte[] content, PdfDict? resources, PdfDict group) {
    if (this._state.FillAlpha < 1 || this._state.StrokeAlpha < 1 || this._state.BlendMode != PdfBlendMode.Normal || this._state.SoftMask != null)
      throw new NotSupportedException("PDF transparency groups are not supported yet.");
    this.Run(content, resources);
  }

  private PdfSoftMask? _BuildSoftMask(PdfDict? dict) => throw new NotSupportedException("PDF soft masks are not supported yet.");

  private void _BeginText() { }
  private void _EndText() { }
  private void _SetFont(string name, double size) { this._state.FontSize = size; }
  private void _SetFontFromReference(object? font, double size) { this._state.FontSize = size; }
  private void _TextMove(double tx, double ty) { }
  private void _SetTextMatrix(Matrix2D m) { }

  private void _ShowText(byte[] text, bool hidden) {
    if (!hidden && this._state.RenderMode != 3)
      throw new NotSupportedException("PDF text is not supported yet.");
  }

  private void _ShowTextArray(List<object?> array, bool hidden) {
    if (!hidden && this._state.RenderMode != 3)
      throw new NotSupportedException("PDF text is not supported yet.");
  }

  private void _Type3Width(List<object?> operands, bool uncoloured) { }
}

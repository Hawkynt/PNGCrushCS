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

}

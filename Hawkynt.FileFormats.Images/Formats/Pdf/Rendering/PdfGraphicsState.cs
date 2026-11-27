using System;
using FileFormat.Core.Vector;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

/// <summary>The graphics state of PDF 32000-1 §8.4, including the text state of §9.3.</summary>
internal sealed class PdfGraphicsState {

  public Matrix2D Ctm = Matrix2D.Identity;

  /// <summary>Where drawing may reach, or null for anywhere on the surface.</summary>
  public PdfClipMask? Clip;

  public PdfColorModel FillSpace = PdfColorModel.DeviceGray;
  public PdfColorModel StrokeSpace = PdfColorModel.DeviceGray;
  public double[] FillColor = [0];
  public double[] StrokeColor = [0];

  /// <summary>The pattern selected by <c>scn</c>/<c>SCN</c> with a name, or null.</summary>
  public object? FillPattern;
  public object? StrokePattern;

  public double LineWidth = 1;
  public LineCap Cap = LineCap.Butt;
  public LineJoin Join = LineJoin.Miter;
  public double MiterLimit = 10;
  public double[] Dash = [];
  public double DashPhase;

  public PdfBlendMode BlendMode = PdfBlendMode.Normal;
  public double FillAlpha = 1;
  public double StrokeAlpha = 1;
  public PdfSoftMask? SoftMask;
  public bool AlphaIsShape;

  public PdfFont? Font;
  public double FontSize;
  public double CharSpacing;
  public double WordSpacing;
  public double HorizontalScaling = 1;
  public double Leading;
  public double Rise;
  public int RenderMode;

  public PdfGraphicsState Clone() {
    var copy = (PdfGraphicsState)this.MemberwiseClone();
    return copy;
  }

  public PdfLineStyle LineStyle => new(this.LineWidth, this.Cap, this.Join, this.MiterLimit, this.Dash, this.DashPhase);
}

namespace FileFormat.Pdf;

/// <summary>A rectangle in PDF user space, in points: the shape of a page box.</summary>
public readonly record struct PdfBox(double Left, double Bottom, double Right, double Top) {

  /// <summary>How wide the box is.</summary>
  public double Width => this.Right - this.Left;

  /// <summary>How tall the box is.</summary>
  public double Height => this.Top - this.Bottom;
}

/// <summary>One page of a PDF document, as its page tree describes it.</summary>
public sealed class PdfPage {

  /// <summary>Where the page sits in the document, from zero.</summary>
  public int Index { get; init; }

  /// <summary>The page's media box, inherited from the tree where the page has none of its own.</summary>
  public PdfBox MediaBox { get; init; }

  /// <summary>The region shown: the crop box clipped to the media box, or the media box when there is no crop box.</summary>
  public PdfBox CropBox { get; init; }

  /// <summary>How far the page is turned clockwise for display: 0, 90, 180 or 270 degrees.</summary>
  public int Rotation { get; init; }

  /// <summary>How many points one unit of user space is (PDF 1.6 /UserUnit), normally 1.</summary>
  public double UserUnit { get; init; } = 1;

  /// <summary>The displayed width in points: the crop box turned by <see cref="Rotation"/> and scaled by <see cref="UserUnit"/>.</summary>
  public double Width => (this.Rotation is 90 or 270 ? this.CropBox.Height : this.CropBox.Width) * this.UserUnit;

  /// <summary>The displayed height in points.</summary>
  public double Height => (this.Rotation is 90 or 270 ? this.CropBox.Width : this.CropBox.Height) * this.UserUnit;
}

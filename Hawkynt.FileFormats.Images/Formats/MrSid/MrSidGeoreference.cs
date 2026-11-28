namespace FileFormat.MrSid;

/// <summary>Where a MrSID picture lies on the ground.</summary>
/// <remarks>
/// MrSID stores the centre of the top-left pixel, not its outer corner: GDAL's tests for the same picture expect a
/// geotransform half a pixel further out than the stored origin, and <see cref="ToGeoTransform"/> applies that shift.
/// </remarks>
/// <param name="CentreX">Ground X of the centre of the top-left pixel.</param>
/// <param name="CentreY">Ground Y of the centre of the top-left pixel.</param>
/// <param name="PixelWidth">Ground distance from one column to the next.</param>
/// <param name="PixelHeight">Ground distance from one row to the next; negative for a north-up picture.</param>
public readonly record struct MrSidGeoreference(double CentreX, double CentreY, double PixelWidth, double PixelHeight) {

  /// <summary>The GDAL-style affine transform: outer corner X, pixel width, 0, outer corner Y, 0, pixel height.</summary>
  public double[] ToGeoTransform()
    => [this.CentreX - this.PixelWidth / 2, this.PixelWidth, 0, this.CentreY - this.PixelHeight / 2, 0, this.PixelHeight];
}

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Pdf.Tests;

/// <summary>
/// How close two renders of a page are, measured the way the PDF tests judge agreement with poppler.
/// </summary>
/// <remarks>
/// Two renderers never agree to the pixel: antialiasing, stroke adjustment and image resampling all
/// differ in the last detail, and where an edge lands can differ by up to a pixel (poppler, for one,
/// maps an image covering 0 to 612 onto 613 pixels). So each pixel of ours is compared with the
/// closest of the reference's pixels in the 3×3 neighbourhood around it, and two numbers are taken
/// from that: the mean absolute error over every channel (0–255), which says how far the picture is
/// off overall, and the share of pixels where even the closest neighbour differs by more than
/// <see cref="BadThreshold"/> in some channel, which says how much of it is visibly wrong — a missing
/// shape, a glyph drawn in the wrong place or colour. The plain per-pixel mean absolute error is
/// kept alongside for information.
/// </remarks>
internal readonly record struct PdfRenderComparison(double MeanAbsoluteError, double BadPercent, bool SameSize, double StrictMeanAbsoluteError = 0) {

  /// <summary>A channel difference larger than this marks a pixel as visibly different.</summary>
  public const int BadThreshold = 48;

  public static PdfRenderComparison Of(RawImage ours, RawImage reference) {
    var a = _Rgb(ours);
    var b = _Rgb(reference);
    if (a.Width != b.Width || a.Height != b.Height)
      return new(double.PositiveInfinity, 100, false, double.PositiveInfinity);

    long sum = 0;
    long strict = 0;
    long bad = 0;
    var width = a.Width;
    var height = a.Height;
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        int r = a.PixelData[at], g = a.PixelData[at + 1], bl = a.PixelData[at + 2];
        strict += Math.Abs(r - b.PixelData[at]) + Math.Abs(g - b.PixelData[at + 1]) + Math.Abs(bl - b.PixelData[at + 2]);

        var bestSum = int.MaxValue;
        var bestWorst = 0;
        for (var dy = -1; dy <= 1; ++dy)
          for (var dx = -1; dx <= 1; ++dx) {
            var nx = x + dx;
            var ny = y + dy;
            if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
              continue;

            var n = (ny * width + nx) * 3;
            var d0 = Math.Abs(r - b.PixelData[n]);
            var d1 = Math.Abs(g - b.PixelData[n + 1]);
            var d2 = Math.Abs(bl - b.PixelData[n + 2]);
            var total = d0 + d1 + d2;
            if (total < bestSum) {
              bestSum = total;
              bestWorst = Math.Max(d0, Math.Max(d1, d2));
            }
          }

        sum += bestSum;
        if (bestWorst > BadThreshold)
          ++bad;
      }

    var pixels = (double)width * height;
    return new(sum / (3 * pixels), 100.0 * bad / pixels, true, strict / (3 * pixels));
  }

  public override string ToString() => this.SameSize
    ? string.Format(CultureInfo.InvariantCulture, "mean abs error {0:F2} (strict {3:F2}), {1:F2}% of pixels off by more than {2}", this.MeanAbsoluteError, this.BadPercent, BadThreshold, this.StrictMeanAbsoluteError)
    : "different sizes";

  private static RawImage _Rgb(RawImage image) => image.Format == PixelFormat.Rgb24 ? image : PixelConverter.Convert(image, PixelFormat.Rgb24);

  /// <summary>Reads a PNG reference render.</summary>
  public static RawImage ReadPng(byte[] png) => PngFile.ToRawImage(PngReader.FromBytes(png));

  /// <summary>Asks poppler's pdftoppm for a page, at a resolution, over the crop box; null when it is not installed or fails.</summary>
  public static RawImage? Poppler(string pdf, int page, double resolution) {
    var folder = Path.Combine(Path.GetTempPath(), "pdf-oracle-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try {
      var prefix = Path.Combine(folder, "page");
      Process? process;
      try {
        process = Process.Start(new ProcessStartInfo("pdftoppm", string.Format(CultureInfo.InvariantCulture, "-r {0} -f {1} -l {1} -singlefile -cropbox -aa yes -aaVector yes -png \"{2}\" \"{3}\"", resolution, page + 1, pdf, prefix)) {
          RedirectStandardError = true,
          RedirectStandardOutput = true,
          UseShellExecute = false,
        });
      } catch (System.ComponentModel.Win32Exception) {
        return null;
      }

      if (process == null)
        return null;

      process.StandardError.ReadToEnd();
      process.StandardOutput.ReadToEnd();
      if (!process.WaitForExit(300_000)) {
        process.Kill();
        return null;
      }

      var file = prefix + ".png";
      return File.Exists(file) ? ReadPng(File.ReadAllBytes(file)) : null;
    } finally {
      try {
        Directory.Delete(folder, true);
      } catch (IOException) {
      }
    }
  }
}

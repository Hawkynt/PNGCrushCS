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
/// differ in the last detail. So a render is compared by two numbers. The mean absolute error over
/// every channel of every pixel (0–255) says how far the picture is off overall; the share of
/// pixels where any channel differs by more than <see cref="BadThreshold"/> says how much of it is
/// visibly wrong — a mislaid glyph or a missing shape shows up there even when the mean barely moves.
/// Edge antialiasing differences stay under the threshold; something drawn in the wrong place or
/// colour does not.
/// </remarks>
internal readonly record struct PdfRenderComparison(double MeanAbsoluteError, double BadPercent, bool SameSize) {

  /// <summary>A channel difference larger than this marks a pixel as visibly different.</summary>
  public const int BadThreshold = 48;

  public static PdfRenderComparison Of(RawImage ours, RawImage reference) {
    var a = _Rgb(ours);
    var b = _Rgb(reference);
    if (a.Width != b.Width || a.Height != b.Height)
      return new(double.PositiveInfinity, 100, false);

    long sum = 0;
    long bad = 0;
    var pixels = a.Width * a.Height;
    for (var i = 0; i < pixels; ++i) {
      var worst = 0;
      for (var c = 0; c < 3; ++c) {
        var d = Math.Abs(a.PixelData[i * 3 + c] - b.PixelData[i * 3 + c]);
        sum += d;
        worst = Math.Max(worst, d);
      }

      if (worst > BadThreshold)
        ++bad;
    }

    return new(sum / (3.0 * pixels), 100.0 * bad / pixels, true);
  }

  public override string ToString() => this.SameSize
    ? string.Format(CultureInfo.InvariantCulture, "mean abs error {0:F2}, {1:F2}% of pixels off by more than {2}", this.MeanAbsoluteError, this.BadPercent, BadThreshold)
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

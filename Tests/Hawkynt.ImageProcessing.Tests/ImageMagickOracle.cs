using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing.Tests;

/// <summary>
/// Asks ImageMagick what an operation should produce, and measures how far our answer is from it.
/// </summary>
/// <remarks>
/// Pictures cross the boundary as binary PPM. That is deliberate: a format with no compression, no
/// colour profile and no gamma chunk is the one where nothing but the operation itself can cause a
/// difference, so a mismatch cannot be blamed on either side's codec.
/// <para/>
/// A machine without ImageMagick has not disagreed with anything, so the test is inconclusive there
/// rather than failed. <c>MAGICK</c> overrides the executable if it is not on the path as
/// <c>magick</c>.
/// </remarks>
internal static class ImageMagickOracle {

  /// <summary>How far two 8-bit pictures are apart.</summary>
  /// <param name="MaxDifference">The largest difference of any one component, in 8-bit levels.</param>
  /// <param name="MeanDifference">The mean absolute difference over every component.</param>
  /// <param name="Exact">The fraction of components that agree exactly.</param>
  public readonly record struct Agreement(int MaxDifference, double MeanDifference, double Exact) {
    public override string ToString()
      => $"max |d| = {this.MaxDifference}, mean |d| = {this.MeanDifference:0.0000}, exact = {this.Exact:P2}";
  }

  /// <summary>Runs ImageMagick over an RGB picture with the given operator arguments.</summary>
  public static RawImage Run(RawImage source, string arguments) {
    var executable = Environment.GetEnvironmentVariable("MAGICK") is { Length: > 0 } configured ? configured : "magick";
    var directory = Path.Combine(Path.GetTempPath(), "imgproc-oracle-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);

    try {
      var input = Path.Combine(directory, "in.ppm");
      var output = Path.Combine(directory, "out.ppm");
      File.WriteAllBytes(input, _ToPpm(source));

      Process? process = null;
      try {
        process = Process.Start(new ProcessStartInfo(executable, $"\"{input}\" {arguments} -depth 8 \"{output}\"") {
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
        });
      } catch (Win32Exception) {
        // Not installed, which is not a disagreement.
      }

      if (process is null) {
        Assert.Inconclusive($"no {executable} on this machine to ask. Set MAGICK to the binary or put it on PATH.");
        return null!;
      }

      using (process) {
        var errors = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
          Assert.Fail($"{executable} {arguments} failed with {process.ExitCode}: {errors}");
      }

      return _FromPpm(File.ReadAllBytes(output));
    } finally {
      Directory.Delete(directory, true);
    }
  }

  /// <summary>Compares two pictures component by component after bringing both to 8-bit RGB.</summary>
  public static Agreement Compare(RawImage ours, RawImage theirs) {
    Assert.That((ours.Width, ours.Height), Is.EqualTo((theirs.Width, theirs.Height)), "the two pictures differ in size");

    var left = ours.ToRgb24();
    var right = theirs.ToRgb24();
    int max = 0, exact = 0;
    long total = 0;

    for (var i = 0; i < left.Length; ++i) {
      var difference = Math.Abs(left[i] - right[i]);
      total += difference;
      if (difference > max)
        max = difference;
      if (difference == 0)
        ++exact;
    }

    return new(max, (double)total / left.Length, (double)exact / left.Length);
  }

  /// <summary>Pearson correlation of two pictures' components, for results compared by structure.</summary>
  public static double Correlation(RawImage ours, RawImage theirs) {
    var left = ours.ToRgb24();
    var right = theirs.ToRgb24();
    double meanLeft = 0, meanRight = 0;
    for (var i = 0; i < left.Length; ++i) {
      meanLeft += left[i];
      meanRight += right[i];
    }

    meanLeft /= left.Length;
    meanRight /= right.Length;
    double covariance = 0, varianceLeft = 0, varianceRight = 0;
    for (var i = 0; i < left.Length; ++i) {
      var a = left[i] - meanLeft;
      var b = right[i] - meanRight;
      covariance += a * b;
      varianceLeft += a * a;
      varianceRight += b * b;
    }

    return covariance / Math.Sqrt(varianceLeft * varianceRight);
  }

  /// <summary>Runs both sides, reports the agreement, and holds it to a ceiling.</summary>
  public static Agreement AssertAgrees(RawImage source, RawImage ours, string arguments, int maxDifference, double meanDifference) {
    var theirs = Run(source, arguments);
    var agreement = Compare(ours, theirs);
    TestContext.Out.WriteLine($"magick {arguments}: {agreement}");

    Assert.Multiple(() => {
      Assert.That(agreement.MaxDifference, Is.LessThanOrEqualTo(maxDifference), $"worst component against magick {arguments}");
      Assert.That(agreement.MeanDifference, Is.LessThanOrEqualTo(meanDifference), $"mean difference against magick {arguments}");
    });

    return agreement;
  }

  private static byte[] _ToPpm(RawImage image) {
    var header = Encoding.ASCII.GetBytes($"P6\n{image.Width} {image.Height}\n255\n");
    var pixels = image.ToRgb24();
    var file = new byte[header.Length + pixels.Length];
    header.CopyTo(file, 0);
    pixels.CopyTo(file, header.Length);
    return file;
  }

  private static RawImage _FromPpm(byte[] file) {
    var at = 0;
    var magic = _Token(file, ref at);
    Assert.That(magic, Is.EqualTo("P6"), "ImageMagick was asked for a binary PPM");
    var width = int.Parse(_Token(file, ref at));
    var height = int.Parse(_Token(file, ref at));
    var maximum = int.Parse(_Token(file, ref at));
    Assert.That(maximum, Is.EqualTo(255), "ImageMagick was asked for 8 bits per component");
    ++at;

    var pixels = new byte[width * height * 3];
    Array.Copy(file, at, pixels, 0, pixels.Length);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static string _Token(byte[] file, ref int at) {
    while (true) {
      while (char.IsWhiteSpace((char)file[at]))
        ++at;
      if (file[at] != '#')
        break;
      while (file[at] != '\n')
        ++at;
    }

    var start = at;
    while (!char.IsWhiteSpace((char)file[at]))
      ++at;

    return Encoding.ASCII.GetString(file, start, at - start);
  }
}

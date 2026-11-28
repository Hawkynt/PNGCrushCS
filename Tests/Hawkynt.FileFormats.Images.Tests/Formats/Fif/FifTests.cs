using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FileFormat.Core;
using FileFormat.Fif;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Fif.Tests;

/// <summary>Iterated Systems fractal images, held to Iterated's own DECO_32.DLL.</summary>
/// <remarks>
/// The committed fixtures are pictures made here, with the DLL's decode beside them (see
/// Fixtures/PROVENANCE.txt), at the coded size and at other output sizes. The real files — 34 still
/// images across FTC 1.1, FTC 3.1–3.3 (bit and arithmetic coded, YUV, error-UV and grey, with and
/// without FTT templates) and FIF1 — are not redistributable and are checked by
/// <see cref="Corpus_EveryFileMatchesTheDllDecode"/> when present.
/// </remarks>
[TestFixture]
public sealed class FifTests {

  private const string _CORPUS_VARIABLE = "FIF_CORPUS";

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Fif", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  private static (int Width, int Height, byte[] Rgb) _Ppm(byte[] data) {
    var fields = new List<string>();
    var position = 0;
    while (fields.Count < 4) {
      while (char.IsWhiteSpace((char)data[position]))
        ++position;
      var start = position;
      while (!char.IsWhiteSpace((char)data[position]))
        ++position;
      fields.Add(Encoding.ASCII.GetString(data, start, position - start));
    }

    int width = int.Parse(fields[1]), height = int.Parse(fields[2]);
    return (width, height, data.AsSpan(position + 1, width * height * 3).ToArray());
  }

  // ==========================================================================================
  // Decoding, against the DLL
  // ==========================================================================================

  [TestCase("selfmade_ftc11_32x16", FifVariant.Fixed)]
  [TestCase("selfmade_ftc31_37x23", FifVariant.Region)]
  [Category("Conformance")]
  public void Fixture_DecodesExactlyAsTheDllDoes(string name, FifVariant variant) {
    var file = FifReader.FromBytes(_Fixture(name + ".fif"));
    var (width, height, expected) = _Ppm(_Fixture(name + ".ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Variant, Is.EqualTo(variant));
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(file.PixelData, Is.EqualTo(expected));
    });
  }

  [TestCase("selfmade_ftc11_32x16", 8, 4)]
  [TestCase("selfmade_ftc11_32x16", 16, 8)]
  [TestCase("selfmade_ftc11_32x16", 24, 12)]
  [TestCase("selfmade_ftc11_32x16", 45, 19)]
  [TestCase("selfmade_ftc11_32x16", 64, 32)]
  [TestCase("selfmade_ftc11_32x16", 96, 48)]
  [TestCase("selfmade_ftc11_32x16", 128, 64)]
  [TestCase("selfmade_ftc31_37x23", 9, 5)]
  [TestCase("selfmade_ftc31_37x23", 18, 11)]
  [TestCase("selfmade_ftc31_37x23", 23, 13)]
  [TestCase("selfmade_ftc31_37x23", 27, 17)]
  [TestCase("selfmade_ftc31_37x23", 74, 46)]
  [TestCase("selfmade_ftc31_37x23", 111, 69)]
  [TestCase("selfmade_ftc31_37x23", 148, 92)]
  [Category("Conformance")]
  public void Fixture_DecodesAtAnotherSizeExactlyAsTheDllDoes(string name, int width, int height) {
    var file = FifReader.FromBytes(_Fixture(name + ".fif"), null, width, height);
    var (expectedWidth, expectedHeight, expected) = _Ppm(_Fixture($"{name}.{width}x{height}.ppm"));

    Assert.Multiple(() => {
      Assert.That((file.Width, file.Height), Is.EqualTo((expectedWidth, expectedHeight)));
      Assert.That(file.PixelData, Is.EqualTo(expected));
      Assert.That(FifFile.ToRawImage(file).Width, Is.EqualTo(width));
    });
  }

  [Test]
  [Category("Unit")]
  public void DecodingAtTheCodedSize_IsTheDefaultDecode() {
    var data = _Fixture("selfmade_ftc31_37x23.fif");
    Assert.Multiple(() => {
      Assert.That(FifReader.CodedSize(data), Is.EqualTo((37, 23)));
      Assert.That(FifReader.FromBytes(data, null, 37, 23).PixelData, Is.EqualTo(FifReader.FromBytes(data).PixelData));
    });
  }

  [TestCase(0, 10)]
  [TestCase(10, -1)]
  [Category("Unit")]
  public void NonPositiveOutputSize_IsRefused(int width, int height)
    => Assert.Throws<ArgumentOutOfRangeException>(() => FifReader.FromBytes(_Fixture("selfmade_ftc31_37x23.fif"), null, width, height));

  [Test]
  [Category("Unit")]
  public void OutputSizeBeyondTheDllsRange_IsRefused()
    => Assert.Throws<ArgumentOutOfRangeException>(() => FifReader.FromBytes(_Fixture("selfmade_ftc11_32x16.fif"), null, 32 * 1024, 16));

  // SetOutputResolution's plan: exactly half is drawn at half size; smaller requests at half size
  // with FIF1's finest levels left out; larger ones at the next power-of-two multiple covering them.
  [TestCase(640, 480, 320, 240, 0, 0, false)]
  [TestCase(640, 480, 160, 120, 0, 2, true)]
  [TestCase(640, 480, 80, 60, 0, 3, true)]
  [TestCase(640, 480, 300, 240, 0, 1, true)]
  [TestCase(640, 480, 480, 360, 1, 0, true)]
  [TestCase(640, 480, 640, 480, 1, 0, false)]
  [TestCase(640, 480, 641, 480, 2, 0, true)]
  [TestCase(640, 480, 1280, 960, 2, 0, false)]
  [TestCase(640, 480, 1920, 1440, 3, 0, true)]
  [TestCase(161, 121, 80, 60, 0, 0, false)]
  [Category("Unit")]
  public void OutputPlan_ChoosesTheDllsLevel(int originalWidth, int originalHeight, int width, int height, int level, int extra, bool resamples) {
    var plan = FifOutputPlan.For(originalWidth, originalHeight, width, height);
    Assert.That((plan.Level, plan.ExtraHalvings, plan.Resamples), Is.EqualTo((level, extra, resamples)));
  }

  [Test]
  [Category("Unit")]
  public void OutputPlan_RegionRectanglesTileTheOutput() {
    var plan = FifOutputPlan.For(570, 380, 333, 211);
    var regions = new (int X, int Y, int Width, int Height)[] { (0, 0, 192, 192), (192, 0, 192, 192), (384, 0, 192, 192), (0, 192, 192, 192), (192, 192, 192, 192), (384, 192, 192, 192) };
    var rects = plan.Rectangles(regions);
    Assert.Multiple(() => {
      Assert.That(rects.Take(3).Sum(r => r.Width), Is.EqualTo(rects.Skip(3).Sum(r => r.Width)));
      Assert.That(rects[2].X + rects[2].Width, Is.GreaterThanOrEqualTo(333));
      Assert.That(rects[3].Y + rects[3].Height, Is.GreaterThanOrEqualTo(211));
      Assert.That(rects.Where((r, i) => i % 3 != 2).All(r => r.Width % 2 == 0), Is.True, "Inner edges land on even pixels.");
    });
  }

  [Test]
  [Category("Unit")]
  public void AreaResample_AveragesWholeAndPartialCoverage() {
    byte[] source = [0, 30, 60, 90, 120, 150];
    Assert.Multiple(() => {
      Assert.That(FifOutputPlan.AreaResample(source, 6, 1, 3, 1), Is.EqualTo(new byte[] { 15, 75, 135 }));
      Assert.That(FifOutputPlan.AreaResample(source, 6, 1, 4, 1), Is.EqualTo(new byte[] { 10, 50, 100, 140 }));
      Assert.That(FifOutputPlan.AreaResample(source, 6, 1, 6, 1), Is.EqualTo(source));
      Assert.Throws<InvalidDataException>(() => FifOutputPlan.AreaResample(source, 6, 1, 7, 1));
    });
  }

  [Test]
  [Category("Unit")]
  public void TemplateUpsampling_KeepsSamplesAndAveragesBetweenThem() {
    var raster = new FifTemplateRaster(2, 2, 0, [10, 20, 30, 41]);
    var twice = FifReconstruction.UpsampleTemplate(raster);
    Assert.That(twice.Pixels, Is.EqualTo(new byte[] { 10, 15, 20, 20, 20, 26, 31, 31, 30, 36, 41, 41, 30, 36, 41, 41 }));
  }

  /// <summary>
  /// Decodes every file under <c>FIF_CORPUS</c> that has a <c>name.ppm</c> beside it — the DLL's
  /// decode — and requires an exact match (FTT templates are found beside the files). A
  /// <c>name.WxH.ppm</c> is the DLL's decode at that output size.
  /// </summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs a directory of fractal images and their DECO_32.DLL decodes in FIF_CORPUS.")]
  public void Corpus_EveryFileMatchesTheDllDecode() {
    var directory = Environment.GetEnvironmentVariable(_CORPUS_VARIABLE);
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
      Assert.Inconclusive($"Set {_CORPUS_VARIABLE} to a directory of fractal images with .ppm decodes beside them.");

    var failures = new List<string>();
    var checkedCount = 0;
    foreach (var expectedPath in Directory.EnumerateFiles(directory!, "*.ppm")) {
      var stem = Path.GetFileNameWithoutExtension(expectedPath);
      var path = Path.Combine(directory!, stem);
      (int Width, int Height)? size = null;
      if (!File.Exists(path)) {
        var dot = stem.LastIndexOf('.');
        var dimensions = dot < 0 ? [] : stem[(dot + 1)..].Split('x');
        if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out var w) || !int.TryParse(dimensions[1], out var h))
          continue;

        path = Path.Combine(directory!, stem[..dot]);
        size = (w, h);
        if (!File.Exists(path))
          continue;
      }

      ++checkedCount;
      try {
        var file = size is { } s ? FifReader.FromFile(new FileInfo(path), s.Width, s.Height) : FifReader.FromFile(new FileInfo(path));
        var (width, height, expected) = _Ppm(File.ReadAllBytes(expectedPath));
        if (file.Width != width || file.Height != height) {
          failures.Add($"{path}: {file.Width}x{file.Height}, the DLL says {width}x{height}");
          continue;
        }

        var maximum = 0;
        for (var i = 0; i < expected.Length; ++i)
          maximum = Math.Max(maximum, Math.Abs(expected[i] - file.PixelData[i]));

        if (maximum > 0)
          failures.Add($"{expectedPath}: differs from the DLL by up to {maximum}");
      } catch (Exception exception) {
        failures.Add($"{path}: {exception.Message}");
      }
    }

    Assert.That(checkedCount, Is.GreaterThan(0), "No file in the corpus had a .ppm beside it.");
    Assert.That(failures, Is.Empty, $"{failures.Count} of {checkedCount} files failed:\n" + string.Join("\n", failures));
  }

  // ==========================================================================================
  // Writing
  // ==========================================================================================

  private static FifFile _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var i = (y * width + x) * 3;
      pixels[i] = (byte)(128 + 100 * Math.Sin(x / 9.0));
      pixels[i + 1] = (byte)(128 + 90 * Math.Cos(y / 7.0));
      pixels[i + 2] = (byte)(64 + x + y);
    }

    return new() { Width = width, Height = height, PixelData = pixels };
  }

  private static double _Psnr(byte[] a, byte[] b) {
    double sum = 0;
    for (var i = 0; i < a.Length; ++i)
      sum += (a[i] - b[i]) * (double)(a[i] - b[i]);

    return sum == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 * a.Length / sum);
  }

  [TestCase(1, 1)]
  [TestCase(7, 5)]
  [TestCase(37, 23)]
  [TestCase(96, 64)]
  [Category("Unit")]
  public void WrittenFile_ReadsBackAtItsSizeAndCloseToThePicture(int width, int height) {
    var picture = _Picture(width, height);
    var again = FifReader.FromBytes(FifWriter.ToBytes(picture));

    Assert.Multiple(() => {
      Assert.That(again.Variant, Is.EqualTo(FifVariant.Region));
      Assert.That(again.Width, Is.EqualTo(width));
      Assert.That(again.Height, Is.EqualTo(height));
      Assert.That(_Psnr(picture.PixelData, again.PixelData), Is.GreaterThan(26.0), "A smooth picture should survive fractal coding well.");
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_IsDeterministic() {
    var picture = _Picture(40, 24);
    Assert.That(FifWriter.ToBytes(picture), Is.EqualTo(FifWriter.ToBytes(picture)));
  }

  [Test]
  [Category("Unit")]
  public void Writer_RefusesPixelDataSmallerThanThePicture() {
    Assert.Throws<ArgumentException>(() => FifWriter.ToBytes(new FifFile { Width = 4, Height = 4, PixelData = new byte[10] }));
  }

  // ==========================================================================================
  // Recognition
  // ==========================================================================================

  [TestCase("selfmade_ftc11_32x16.fif")]
  [TestCase("selfmade_ftc31_37x23.fif")]
  [Category("Unit")]
  public void Registry_DetectsAFractalImageBySignature(string name)
    => Assert.That(FormatRegistry.DetectFromBytes(_Fixture(name)), Is.EqualTo(ImageFormat.Fif));

  [TestCase(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F' }, TestName = "JpegRenamedToFif_IsRefused")]
  [TestCase(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }, TestName = "PngRenamedToFif_IsRefused")]
  [TestCase(new byte[] { (byte)'B', (byte)'M', 0x3A, 0, 0, 0, 0, 0, 0, 0, 0x36, 0, 0, 0 }, TestName = "BmpRenamedToFif_IsRefused")]
  [Category("Unit")]
  public void OtherFormatsRenamedToFif_AreRefused(byte[] head) {
    var data = new byte[256];
    head.CopyTo(data, 0);
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => FifReader.FromBytes(data));
      Assert.That(FormatRegistry.DetectFromBytes(data), Is.Not.EqualTo(ImageFormat.Fif));
    });
  }

  [TestCase(new byte[] { 0x46, 0x54, 0x43, 0x00, 0x01, 0x01, 0x01, 0x02 })]
  [TestCase(new byte[] { 0x46, 0x54, 0x43, 0x00, 0x01, 0x01, 0x02, 0x02 })]
  [TestCase(new byte[] { 0x46, 0x54, 0x43, 0x00, 0x02, 0x02, 0x02, 0x02 })]
  [Category("Unit")]
  public void FractalVideo_IsRefusedByName(byte[] head) {
    var data = new byte[128];
    head.CopyTo(data, 0);
    var exception = Assert.Throws<NotSupportedException>(() => FifReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("video"));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedFile_IsRefused() {
    var data = _Fixture("selfmade_ftc31_37x23.fif");
    Assert.Throws<InvalidDataException>(() => FifReader.FromBytes(data[..^5]));
  }

  // ==========================================================================================
  // Constants
  // ==========================================================================================

  [Test]
  [Category("Unit")]
  public void ProgressiveTables_AreTheDllsBytes() {
    // SHA-256 of DECO_32.DLL 6.1 (227840 bytes), virtual addresses 0x110310D0..0x11033867.
    Assert.That(Convert.ToHexString(SHA256.HashData(FifProgressiveTables.Data)),
      Is.EqualTo("5C57D7169136E2D6980D98A45C68BB623C576F14A6738C10B4C5D0736BD3406D"));
  }

  [Test]
  [Category("Unit")]
  public void FixedQuantizer_SpansTheAffineTable() {
    var table = FifTransforms.FixedQuantization;
    Assert.Multiple(() => {
      Assert.That(table, Has.Length.EqualTo(128));
      Assert.That(table[0], Is.EqualTo(0));
      Assert.That(table, Is.Ordered);
      Assert.That(table[^1], Is.LessThan(8192));
    });
  }

  [TestCase(0, 0)]
  [TestCase(255, 255)]
  [TestCase(256, 255)]
  [TestCase(639, 255)]
  [TestCase(640, 0)]
  [TestCase(-1, 0)]
  [Category("Unit")]
  public void Clamp10_SaturatesLikeTheDll(int value, int expected) => Assert.That(FifTransforms.Clamp10(value), Is.EqualTo(expected));
}

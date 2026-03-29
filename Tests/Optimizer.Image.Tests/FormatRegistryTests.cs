using FileFormat.Core;
using Optimizer.Image;

namespace Optimizer.Image.Tests;

[TestFixture]
public sealed class FormatRegistryTests {

  [Test]
  [Category("Unit")]
  public void GetEntry_Png_ReturnsEntry() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Png);
    Assert.That(entry, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void GetEntry_UnknownFormat_ReturnsNull() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Unknown);
    Assert.That(entry, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void GetEntry_AllRegistered_HaveNonEmptyPrimaryExtension() {
    var formatsToTest = new[] {
      ImageFormat.Png, ImageFormat.Bmp, ImageFormat.Tga, ImageFormat.Pcx,
      ImageFormat.Jpeg, ImageFormat.Tiff, ImageFormat.Ico, ImageFormat.Cur,
      ImageFormat.Qoi, ImageFormat.Farbfeld, ImageFormat.Sgi, ImageFormat.Wbmp,
      ImageFormat.Xbm, ImageFormat.Xpm, ImageFormat.MacPaint, ImageFormat.Hrz,
    };

    foreach (var format in formatsToTest) {
      var entry = FormatRegistry.GetEntry(format);
      Assert.That(entry, Is.Not.Null, $"Format {format} should be registered");
      Assert.That(entry!.PrimaryExtension, Is.Not.Empty, $"Format {format} should have a primary extension");
    }
  }

  [Test]
  [Category("Unit")]
  public void GetEntry_AllRegistered_HaveNonEmptyAllExtensions() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Sgi);
    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.AllExtensions, Is.Not.Empty);
  }

  [Test]
  [Category("Unit")]
  public void GetExtension_Png_ReturnsDotPng() {
    var ext = FormatRegistry.GetExtension(ImageFormat.Png);
    Assert.That(ext, Is.EqualTo(".png"));
  }

  [Test]
  [Category("Unit")]
  public void GetExtension_UnknownFormat_ReturnsEmpty() {
    var ext = FormatRegistry.GetExtension(ImageFormat.Unknown);
    Assert.That(ext, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DetectFromExtension_DotPng_ReturnsPng() {
    var format = FormatRegistry.DetectFromExtension(".png");
    Assert.That(format, Is.EqualTo(ImageFormat.Png));
  }

  [Test]
  [Category("Unit")]
  public void DetectFromExtension_CaseInsensitive() {
    var lower = FormatRegistry.DetectFromExtension(".png");
    var upper = FormatRegistry.DetectFromExtension(".PNG");
    var mixed = FormatRegistry.DetectFromExtension(".Png");

    Assert.That(lower, Is.EqualTo(ImageFormat.Png));
    Assert.That(upper, Is.EqualTo(ImageFormat.Png));
    Assert.That(mixed, Is.EqualTo(ImageFormat.Png));
  }

  [Test]
  [Category("Unit")]
  public void DetectFromExtension_Unknown_ReturnsDefault() {
    var format = FormatRegistry.DetectFromExtension(".xyz_unknown");
    Assert.That(format, Is.EqualTo(ImageFormat.Unknown));
  }

  [Test]
  [Category("Unit")]
  public void GetEntry_Wbmp_HasMonochromeCapability() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Wbmp);
    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.Capabilities & FormatCapability.MonochromeOnly, Is.Not.EqualTo(FormatCapability.None));
  }

  [Test]
  [Category("Unit")]
  public void GetEntry_Xpm_HasIndexedOnlyCapability() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Xpm);
    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.Capabilities & FormatCapability.IndexedOnly, Is.Not.EqualTo(FormatCapability.None));
  }

  [Test]
  [Category("Unit")]
  public void GetEntry_Png_HasDedicatedOptimizerCapability() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Png);
    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.Capabilities & FormatCapability.HasDedicatedOptimizer, Is.Not.EqualTo(FormatCapability.None));
  }

  [Test]
  [Category("Unit")]
  public void GetEntry_Qoi_HasVariableResolutionCapability() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Qoi);
    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.Capabilities, Is.EqualTo(FormatCapability.VariableResolution));
  }

  [Test]
  [Category("Unit")]
  public void ConversionTargets_ExcludesDedicatedOptimizerFormats() {
    var targets = FormatRegistry.ConversionTargets.ToList();
    Assert.That(targets.Any(e => e.Format == ImageFormat.Png), Is.False);
    Assert.That(targets.Any(e => e.Format == ImageFormat.Bmp), Is.False);
    Assert.That(targets.Any(e => e.Format == ImageFormat.Jpeg), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ConversionTargets_IncludesNonOptimizerFormats() {
    var targets = FormatRegistry.ConversionTargets.ToList();
    Assert.That(targets.Any(e => e.Format == ImageFormat.Qoi), Is.True);
    Assert.That(targets.Any(e => e.Format == ImageFormat.Farbfeld), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void GetEntry_LoadRawImage_ReturnsNullForMissingFile() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Qoi);
    Assert.That(entry, Is.Not.Null);

    var tempPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.qoi");
    var result = entry!.LoadRawImage(new FileInfo(tempPath));
    Assert.That(result, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void GetEntry_LoadRawImage_ReturnsNullForInvalidData() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Qoi);
    Assert.That(entry, Is.Not.Null);

    var tempPath = Path.Combine(Path.GetTempPath(), $"invalid_{Guid.NewGuid():N}.qoi");
    try {
      File.WriteAllBytes(tempPath, [0x00, 0x01, 0x02, 0x03]);
      var result = entry!.LoadRawImage(new FileInfo(tempPath));
      Assert.That(result, Is.Null);
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Unit")]
  public void DetectFromExtension_MultipleExtensions_AllResolve() {
    var sgiEntry = FormatRegistry.GetEntry(ImageFormat.Sgi);
    Assert.That(sgiEntry, Is.Not.Null);

    foreach (var ext in sgiEntry!.AllExtensions) {
      var detected = FormatRegistry.DetectFromExtension(ext);
      Assert.That(detected, Is.EqualTo(ImageFormat.Sgi), $"Extension '{ext}' should resolve to Sgi");
    }
  }

  [Test]
  [Category("Unit")]
  public void FormatCapability_Flags_ArePowersOfTwo() {
    var values = Enum.GetValues<FormatCapability>();
    foreach (var value in values) {
      if (value == FormatCapability.None)
        continue;
      var intVal = (int)value;
      Assert.That(intVal & (intVal - 1), Is.EqualTo(0), $"{value} should be a power of two");
    }
  }

  [Test]
  [Category("Unit")]
  public void FormatCapability_HasExpectedCount() {
    var values = Enum.GetValues<FormatCapability>();
    Assert.That(values, Has.Length.EqualTo(6));
  }
}

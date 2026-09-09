using System;
using FileFormat.Core.PixelFormats;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class TypedRawImageTests {

  [Test]
  public void EveryLegacyPixelFormat_HasMatchingTypedTraits() {
    foreach (var format in Enum.GetValues<PixelFormat>()) {
      var traits = RawPixelFormats.Get(format);

      Assert.Multiple(() => {
        Assert.That(traits.LegacyFormat, Is.EqualTo(format), $"{format} maps to another legacy format");
        Assert.That(traits.StorageBitsPerPixel, Is.EqualTo(RawImage.BitsPerPixel(format)), $"{format} bit storage differs");
        Assert.That(traits.BytesPerPixel, Is.EqualTo(RawImage.BytesPerPixel(format)), $"{format} byte storage differs");
        Assert.That(traits.IsPlanarYuv, Is.EqualTo(RawImage.IsPlanarYuvFormat(format)), $"{format} planar classification differs");
        Assert.That(traits.IsFloatingPoint, Is.EqualTo(RawImage.IsFloatingPointFormat(format)), $"{format} floating classification differs");
      });
    }
  }

  [TestCase(PixelFormat.Indexed1, true, false, false, 1)]
  [TestCase(PixelFormat.Indexed16, true, false, false, 1)]
  [TestCase(PixelFormat.RgbaF32, false, false, true, 1)]
  [TestCase(PixelFormat.Yuv420P10, false, true, false, 3)]
  [TestCase(PixelFormat.Rgb24, false, false, false, 1)]
  public void LegacyRawImage_ClassificationMatchesDeclaredTraits(
    PixelFormat format,
    bool isIndexed,
    bool isPlanarYuv,
    bool isFloatingPoint,
    int planeCount
  ) {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = format,
      PixelData = new byte[Math.Max(1, (int)new RawImage {
        Width = 1,
        Height = 1,
        Format = format,
        PixelData = [],
      }.MinimumPixelDataLength)],
    };

    Assert.Multiple(() => {
      Assert.That(image.IsIndexed, Is.EqualTo(isIndexed));
      Assert.That(image.IsPlanarYuv, Is.EqualTo(isPlanarYuv));
      Assert.That(image.IsFloatingPoint, Is.EqualTo(isFloatingPoint));
      Assert.That(image.PlaneCount, Is.EqualTo(planeCount));
    });
  }

  [Test]
  public void LegacyRawImage_HasAlphaPreservesChannelAndPaletteSemantics() {
    var channelAlpha = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0, 0, 0, 255],
    };
    var opaqueIndexed = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
      Palette = [0, 0, 0],
      PaletteCount = 1,
      AlphaTable = [255],
    };
    var transparentIndexed = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
      Palette = [0, 0, 0],
      PaletteCount = 1,
      AlphaTable = [254],
    };
    var noAlpha = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0, 0, 0],
    };

    Assert.Multiple(() => {
      Assert.That(channelAlpha.HasAlpha, Is.True);
      Assert.That(opaqueIndexed.HasAlpha, Is.False);
      Assert.That(transparentIndexed.HasAlpha, Is.True);
      Assert.That(noAlpha.HasAlpha, Is.False);
    });
  }

  [TestCase(PixelFormat.Yuv420P8, 8, 2, 2, 6L)]
  [TestCase(PixelFormat.Yuv422P10, 10, 2, 1, 16L)]
  [TestCase(PixelFormat.Yuv440P12, 12, 1, 2, 16L)]
  [TestCase(PixelFormat.Yuv444P16, 16, 1, 1, 24L)]
  public void LegacyRawImage_YuvLayoutMatchesDeclaredTraits(
    PixelFormat format,
    int bitDepth,
    int subsampleX,
    int subsampleY,
    long minimumBytes
  ) {
    var image = new RawImage {
      Width = 2,
      Height = 2,
      Format = format,
      PixelData = new byte[minimumBytes],
    };

    Assert.Multiple(() => {
      Assert.That(RawImage.YuvBitDepth(format), Is.EqualTo(bitDepth));
      Assert.That(RawImage.YuvSubsampling(format), Is.EqualTo((subsampleX, subsampleY)));
      Assert.That(image.MinimumPixelDataLength, Is.EqualTo(minimumBytes));
      Assert.That(image.HasEnoughPixelData, Is.True);
    });
  }

  [Test]
  public void GenericRawImage_DeclaresFormatWithoutRuntimeFormatArgument() {
    byte[] pixels = [1, 2, 3, 4, 5, 6];
    var image = new RawImage<Rgb24>(2, 1, pixels);

    RawImage legacy = image;

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(RawImage<Rgb24>.PixelTraits.ComponentBitDepth, Is.EqualTo(8));
      Assert.That(image.Width, Is.EqualTo(2));
      Assert.That(image.Height, Is.EqualTo(1));
      Assert.That(image.PixelData, Is.SameAs(pixels));
      Assert.That(legacy, Is.SameAs(image.Untyped));
      Assert.That(legacy.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(legacy.PixelData, Is.SameAs(pixels));
    });
  }

  [Test]
  public void Indexed6_UsesByteCompatibilityStorageButKeepsSixBitSemantics() {
    var palette = new byte[64 * 3];
    var image = new RawImage<Indexed6>(2, 1, [0, 63], palette: palette, paletteCount: 64);
    var traits = RawImage<Indexed6>.PixelTraits;

    Assert.Multiple(() => {
      Assert.That(traits.Kind, Is.EqualTo(RawPixelFormatKind.Indexed));
      Assert.That(traits.LegacyFormat, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(traits.StorageBitsPerPixel, Is.EqualTo(8));
      Assert.That(traits.IndexBitDepth, Is.EqualTo(6));
      Assert.That(traits.MaximumPaletteEntries, Is.EqualTo(64));
      Assert.That(image.Untyped.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(image.Untyped.PixelData, Is.EqualTo(new byte[] { 0, 63 }));
    });
  }

  [Test]
  public void Indexed6_RejectsIndexOutsideDeclaredDomain() {
    Assert.That(
      () => new RawImage<Indexed6>(1, 1, [64], palette: new byte[64 * 3], paletteCount: 64),
      Throws.ArgumentException
    );
  }

  [Test]
  public void Indexed6_RejectsPaletteLargerThanDeclaredDomain() {
    Assert.That(
      () => new RawImage<Indexed6>(1, 1, [0], palette: new byte[65 * 3], paletteCount: 65),
      Throws.ArgumentException
    );
  }

  [Test]
  public void FromUntyped_AddsTypedViewWithoutCopyingBuffers() {
    byte[] pixels = [0, 7, 31, 63];
    var legacy = new RawImage {
      Width = 4,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = new byte[64 * 3],
      PaletteCount = 64,
    };

    var typed = RawImage<Indexed6>.FromUntyped(legacy);

    Assert.Multiple(() => {
      Assert.That(typed.Untyped, Is.SameAs(legacy));
      Assert.That(typed.PixelData, Is.SameAs(pixels));
      Assert.That(typed.Format, Is.EqualTo(PixelFormat.Indexed8));
    });
  }

  [Test]
  public void TryFromUntyped_RejectsIncompatibleLegacyFormat() {
    var legacy = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0, 0, 0, 255],
    };

    var success = RawImage<Rgb24>.TryFromUntyped(legacy, out var typed);

    Assert.Multiple(() => {
      Assert.That(success, Is.False);
      Assert.That(typed, Is.Null);
    });
  }
}

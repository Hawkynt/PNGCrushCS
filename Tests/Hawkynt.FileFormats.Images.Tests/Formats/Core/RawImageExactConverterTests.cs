using FileFormat.Core.PixelFormats;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class RawImageExactConverterTests {

  [Test]
  public void Indexed8ToIndexed6_FittingImageNarrowsZeroCopy() {
    byte[] pixels = [0, 7, 31, 63];
    byte[] palette = new byte[64 * 3];
    byte[] alpha = new byte[64];
    var source = new RawImage<Indexed8>(4, 1, pixels, palette: palette, paletteCount: 64, alphaTable: alpha);

    var success = RawImageExactConverter.TryConvertIndexed<Indexed8, Indexed6>(source, out var target);

    Assert.That(success, Is.True);
    Assert.That(target, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(target!.PixelData, Is.SameAs(pixels));
      Assert.That(target.Palette, Is.SameAs(palette));
      Assert.That(target.AlphaTable, Is.SameAs(alpha));
      Assert.That(RawImage<Indexed6>.PixelTraits.IndexBitDepth, Is.EqualTo(6));
    });
  }

  [Test]
  public void Indexed8ToIndexed6_OutOfRangeIndexFailsWithoutQuantizing() {
    var source = new RawImage<Indexed8>(2, 1, [0, 64], palette: new byte[64 * 3], paletteCount: 64);

    var success = RawImageExactConverter.TryConvertIndexed<Indexed8, Indexed6>(source, out var target);

    Assert.Multiple(() => {
      Assert.That(success, Is.False);
      Assert.That(target, Is.Null);
    });
  }

  [Test]
  public void Indexed8ToIndexed4_RepacksIndicesAndPreservesPaletteIdentity() {
    byte[] palette = new byte[16 * 3];
    byte[] alpha = new byte[16];
    var source = new RawImage<Indexed8>(5, 1, [1, 2, 15, 0, 3], palette: palette, paletteCount: 16, alphaTable: alpha);

    var success = RawImageExactConverter.TryConvertIndexed<Indexed8, Indexed4>(source, out var target);

    Assert.That(success, Is.True);
    Assert.That(target, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(target!.PixelData, Is.EqualTo(new byte[] { 0x12, 0xF0, 0x30 }));
      Assert.That(target.Palette, Is.SameAs(palette));
      Assert.That(target.AlphaTable, Is.SameAs(alpha));
      Assert.That(target.PaletteCount, Is.EqualTo(16));
    });
  }

  [Test]
  public void Indexed4ToIndexed8_UnpacksWithoutExpandingThroughRgba() {
    byte[] palette = new byte[16 * 3];
    var source = new RawImage<Indexed4>(5, 1, [0x12, 0xF0, 0x30], palette: palette, paletteCount: 16);

    var success = RawImageExactConverter.TryConvertIndexed<Indexed4, Indexed8>(source, out var target);

    Assert.That(success, Is.True);
    Assert.That(target, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(target!.PixelData, Is.EqualTo(new byte[] { 1, 2, 15, 0, 3 }));
      Assert.That(target.Palette, Is.SameAs(palette));
      Assert.That(target.PaletteCount, Is.EqualTo(16));
    });
  }

  [Test]
  public void Indexed16ToIndexed8_FittingIndicesReduceStorageExactly() {
    byte[] palette = new byte[256 * 3];
    var source = new RawImage<Indexed16>(3, 1, [0, 0, 1, 0, 255, 0], palette: palette, paletteCount: 256);

    var success = RawImageExactConverter.TryConvertIndexed<Indexed16, Indexed8>(source, out var target);

    Assert.That(success, Is.True);
    Assert.That(target, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(target!.PixelData, Is.EqualTo(new byte[] { 0, 1, 255 }));
      Assert.That(target.Palette, Is.SameAs(palette));
    });
  }

  [Test]
  public void Indexed8ToIndexed4_UnusedExtraPaletteEntriesStillPreventStructuralNarrowing() {
    var source = new RawImage<Indexed8>(1, 1, [0], palette: new byte[17 * 3], paletteCount: 17);

    var success = RawImageExactConverter.TryConvertIndexed<Indexed8, Indexed4>(source, out var target);

    Assert.Multiple(() => {
      Assert.That(success, Is.False);
      Assert.That(target, Is.Null);
    });
  }

  [Test]
  public void NonIndexedFormats_AreNotHandledByExactIndexedConverter() {
    var source = new RawImage<Rgb24>(1, 1, [1, 2, 3]);

    var success = RawImageExactConverter.TryConvertIndexed<Rgb24, Indexed8>(source, out var target);

    Assert.Multiple(() => {
      Assert.That(success, Is.False);
      Assert.That(target, Is.Null);
    });
  }
}

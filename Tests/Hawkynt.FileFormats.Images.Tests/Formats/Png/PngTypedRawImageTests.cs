using System;
using FileFormat.Core;
using FileFormat.Core.PixelFormats;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Png.Tests;

[TestFixture]
public sealed class PngTypedRawImageTests {

  private static byte[] _Palette4() => [
    0, 0, 0,
    255, 0, 0,
    0, 255, 0,
    0, 0, 255,
  ];

  private static RawImage<Indexed2> _Indexed2() => new(
    5,
    2,
    [0, 1, 2, 3, 0, 3, 2, 1, 0, 3],
    palette: _Palette4(),
    paletteCount: 4
  );

  [Test]
  public void Indexed2_IsPackedOnlyAtPngBoundaryAndRoundTripsAsIndices() {
    var source = _Indexed2();
    var png = PngFile.FromRawImage(source);

    Assert.Multiple(() => {
      Assert.That(png.ColorType, Is.EqualTo(PngColorType.Palette));
      Assert.That(png.BitDepth, Is.EqualTo(2));
      Assert.That(png.PixelData, Has.Length.EqualTo(2));
      Assert.That(png.PixelData![0], Is.EqualTo(new byte[] { 0x1B, 0x00 }));
      Assert.That(png.PixelData[1], Is.EqualTo(new byte[] { 0xE4, 0xC0 }));
    });

    var restoredFile = PngReader.FromBytes(PngWriter.ToBytes(png));
    var restored = PngFile.ToRawImage(restoredFile);

    Assert.Multiple(() => {
      Assert.That(restored.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(source.Palette));
      Assert.That(RawImage<Indexed2>.TryFromUntyped(restored, out _), Is.True);
    });
  }

  [Test]
  public void Indexed2_RejectsIndexOutsideActualPalette() {
    var source = new RawImage<Indexed2>(
      2,
      1,
      [0, 3],
      palette: [0, 0, 0, 255, 255, 255, 127, 127, 127],
      paletteCount: 3
    );

    Assert.That(() => PngFile.FromRawImage(source), Throws.ArgumentException);
  }

  [Test]
  public void Indexed8_DoesNotSilentlyChooseSmallerBitDepth() {
    var source = new RawImage<Indexed8>(
      2,
      2,
      [0, 1, 2, 3],
      palette: _Palette4(),
      paletteCount: 4
    );

    var png = PngFile.FromRawImage(source);

    Assert.That(png.BitDepth, Is.EqualTo(8));
  }

  [Test]
  public void Registry_AdvertisesAllExactPngInputsIncludingIndexed2() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Png);
    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.TypedWriteCapabilities, Has.Length.EqualTo(12));

    var indexed2 = Array.Find(
      entry.TypedWriteCapabilities,
      capability => capability.PixelFormat.IsIndexed && capability.PixelFormat.IndexBitDepth == 2
    );

    Assert.That(indexed2, Is.Not.Null);
    var encoded = indexed2!.Encode(_Indexed2().Untyped);
    var restored = PngReader.FromBytes(encoded);

    Assert.Multiple(() => {
      Assert.That(restored.ColorType, Is.EqualTo(PngColorType.Palette));
      Assert.That(restored.BitDepth, Is.EqualTo(2));
      Assert.That(restored.PaletteCount, Is.EqualTo(4));
    });
  }
}

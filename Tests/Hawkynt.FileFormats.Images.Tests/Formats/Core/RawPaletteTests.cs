namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class RawPaletteTests {

  [Test]
  public void FromRgb24_RoundTripsLegacyPaletteExactly() {
    byte[] rgb = [0, 128, 255, 17, 34, 51];
    byte[] alpha = [255, 64];

    var palette = RawPalette.FromRgb24(rgb, 2, alpha);
    var roundTrip = palette.ToRgb24Exact();

    Assert.Multiple(() => {
      Assert.That(palette.Count, Is.EqualTo(2));
      Assert.That(palette[0], Is.EqualTo(new RawPaletteEntry(0, 128 * 257, ushort.MaxValue, ushort.MaxValue)));
      Assert.That(palette[1], Is.EqualTo(new RawPaletteEntry(17 * 257, 34 * 257, 51 * 257, 64 * 257)));
      Assert.That(palette.HasAlpha, Is.True);
      Assert.That(roundTrip.Rgb, Is.EqualTo(rgb));
      Assert.That(roundTrip.Alpha, Is.EqualTo(alpha));
    });
  }

  [Test]
  public void ToRgb24Exact_RejectsPrecisionReduction() {
    var palette = new RawPalette([new RawPaletteEntry(1, 0, 0)]);

    var success = palette.TryToRgb24Exact(out var rgb, out var alpha);

    Assert.Multiple(() => {
      Assert.That(success, Is.False);
      Assert.That(rgb, Is.Empty);
      Assert.That(alpha, Is.Null);
      Assert.That(() => palette.ToRgb24Exact(), Throws.InvalidOperationException);
    });
  }

  [Test]
  public void FromRawImage_UsesDeclaredPaletteCountWithoutReordering() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
      Palette = [1, 2, 3, 4, 5, 6],
      PaletteCount = 1,
      AlphaTable = [7, 8],
    };

    var palette = RawPalette.FromRawImage(image);

    Assert.That(palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(palette!.Count, Is.EqualTo(1));
      Assert.That(palette[0], Is.EqualTo(new RawPaletteEntry(257, 514, 771, 1799)));
    });
  }

  [Test]
  public void OpaquePalette_OmitsLegacyAlphaTable() {
    var palette = new RawPalette([
      new RawPaletteEntry(0, 0, 0),
      new RawPaletteEntry(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue),
    ]);

    var legacy = palette.ToRgb24Exact();

    Assert.Multiple(() => {
      Assert.That(palette.HasAlpha, Is.False);
      Assert.That(legacy.Rgb, Is.EqualTo(new byte[] { 0, 0, 0, 255, 255, 255 }));
      Assert.That(legacy.Alpha, Is.Null);
    });
  }
}

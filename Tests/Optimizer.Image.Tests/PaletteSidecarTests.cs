using System;
using System.IO;
using FileFormat.Core;

namespace Optimizer.Image.Tests;

[TestFixture]
public sealed class PaletteSidecarTests {

  private static RawImage _MakeIndexed(byte[] palette, int paletteCount = -1) => new() {
    Width = 4,
    Height = 4,
    Format = PixelFormat.Indexed8,
    PixelData = new byte[16],
    Palette = palette,
    PaletteCount = paletteCount < 0 ? palette.Length / 3 : paletteCount,
  };

  private static RawImage _MakeRgba() => new() {
    Width = 4,
    Height = 4,
    Format = PixelFormat.Rgba32,
    PixelData = new byte[64],
  };

  [Test]
  [Category("Unit")]
  public void TryWrite_NullImage_ReturnsFalse() {
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");
    Assert.That(PaletteSidecar.TryWrite(path, null), Is.False);
    Assert.That(File.Exists(path + PaletteSidecar.SidecarSuffix), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TryWrite_NonIndexedImage_ReturnsFalse() {
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");
    Assert.That(PaletteSidecar.TryWrite(path, _MakeRgba()), Is.False);
    Assert.That(File.Exists(path + PaletteSidecar.SidecarSuffix), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TryWrite_IndexedWithPalette_WritesSidecar() {
    var palette = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    var img = _MakeIndexed(palette);
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");
    try {
      Assert.That(PaletteSidecar.TryWrite(path, img), Is.True);
      var written = File.ReadAllBytes(path + PaletteSidecar.SidecarSuffix);
      Assert.That(written, Is.EqualTo(palette));
    } finally {
      File.Delete(path + PaletteSidecar.SidecarSuffix);
    }
  }

  [Test]
  [Category("Unit")]
  public void TryWrite_TruncatesToPaletteCount() {
    // Caller may pass a palette buffer larger than PaletteCount (with trailing slack); only the
    // first PaletteCount * 3 bytes should be persisted.
    var palette = new byte[12 * 3]; // 12 entries' worth of bytes
    for (var i = 0; i < palette.Length; ++i) palette[i] = (byte)i;
    var img = _MakeIndexed(palette, paletteCount: 4); // only first 12 bytes are real
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");
    try {
      Assert.That(PaletteSidecar.TryWrite(path, img), Is.True);
      var written = File.ReadAllBytes(path + PaletteSidecar.SidecarSuffix);
      Assert.That(written.Length, Is.EqualTo(12));
      for (var i = 0; i < 12; ++i) Assert.That(written[i], Is.EqualTo(i));
    } finally {
      File.Delete(path + PaletteSidecar.SidecarSuffix);
    }
  }

  [Test]
  [Category("Unit")]
  public void Apply_NoSidecarFile_ReturnsImageUnchanged() {
    var img = _MakeIndexed(new byte[] { 0, 0, 0, 255, 255, 255 });
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");
    var result = PaletteSidecar.Apply(path, img);
    Assert.That(result, Is.SameAs(img));
  }

  [Test]
  [Category("Unit")]
  public void Apply_SidecarExists_ReplacesPaletteAndCount() {
    var original = new byte[] { 0, 0, 0, 255, 255, 255 };
    var sidecar = new byte[] {
      10, 20, 30,
      40, 50, 60,
      70, 80, 90,
      100, 110, 120,
    };
    var img = _MakeIndexed(original);
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");
    try {
      File.WriteAllBytes(path + PaletteSidecar.SidecarSuffix, sidecar);
      var result = PaletteSidecar.Apply(path, img);
      Assert.That(result, Is.Not.SameAs(img));
      Assert.That(result.Palette, Is.EqualTo(sidecar));
      Assert.That(result.PaletteCount, Is.EqualTo(4));
      Assert.That(result.PixelData, Is.SameAs(img.PixelData), "Pixel data should not be re-allocated");
    } finally {
      File.Delete(path + PaletteSidecar.SidecarSuffix);
    }
  }

  [Test]
  [Category("Unit")]
  public void Apply_NonIndexedImage_ReturnsUnchangedEvenIfSidecarExists() {
    var img = _MakeRgba();
    var sidecar = new byte[] { 1, 2, 3, 4, 5, 6 };
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");
    try {
      File.WriteAllBytes(path + PaletteSidecar.SidecarSuffix, sidecar);
      Assert.That(PaletteSidecar.Apply(path, img), Is.SameAs(img));
    } finally {
      File.Delete(path + PaletteSidecar.SidecarSuffix);
    }
  }

  [Test]
  [Category("Unit")]
  public void Apply_MalformedSidecar_ReturnsImageUnchanged() {
    var img = _MakeIndexed(new byte[] { 0, 0, 0, 255, 255, 255 });
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");
    try {
      // 7 bytes — not a multiple of 3, so invalid.
      File.WriteAllBytes(path + PaletteSidecar.SidecarSuffix, new byte[] { 1, 2, 3, 4, 5, 6, 7 });
      Assert.That(PaletteSidecar.Apply(path, img), Is.SameAs(img));
    } finally {
      File.Delete(path + PaletteSidecar.SidecarSuffix);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriteThenApply_PreservesPalette() {
    var picked = new byte[] {
      0x9B, 0xBC, 0x0F,  // DMG Classic
      0x8B, 0xAC, 0x0F,
      0x30, 0x62, 0x30,
      0x0F, 0x38, 0x0F,
    };
    var saved = _MakeIndexed(picked);
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");
    try {
      Assert.That(PaletteSidecar.TryWrite(path, saved), Is.True);

      // Simulate a reader that has no idea about the palette (e.g., NES CHR after read-back).
      var defaultPalette = new byte[] { 0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255 };
      var reloaded = _MakeIndexed(defaultPalette);

      var withSidecar = PaletteSidecar.Apply(path, reloaded);
      Assert.That(withSidecar.Palette, Is.EqualTo(picked));
      Assert.That(withSidecar.PaletteCount, Is.EqualTo(4));
    } finally {
      File.Delete(path + PaletteSidecar.SidecarSuffix);
    }
  }
}

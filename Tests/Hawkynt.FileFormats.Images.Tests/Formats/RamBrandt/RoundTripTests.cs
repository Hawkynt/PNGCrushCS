using System;
using System.IO;
using FileFormat.Core;
using FileFormat.RamBrandt;

namespace FileFormat.RamBrandt.Tests;

[TestFixture]
public sealed class RoundTripTests {

  private static RamBrandtFile _Sample(RamBrandtMode mode, Func<int, byte> fill) {
    var bitmap = new byte[RamBrandtFile.BitmapDataSize];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = fill(i);

    return new() {
      Mode = mode,
      BitmapData = bitmap,
      Colors = [0x00, 0x02, 0x04, 0x06, 0x28, 0x54, 0x86, 0xB8, 0x0E],
      DisplayList = new byte[RamBrandtFile.DisplayListSize],
    };
  }

  private static RawImage _Gradient() {
    var data = new byte[RamBrandtFile.DisplayWidth * RamBrandtFile.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 1] = (byte)(i % 199);
      data[i + 3] = 255;
    }

    return new() {
      Width = RamBrandtFile.DisplayWidth,
      Height = RamBrandtFile.DisplayHeight,
      Format = PixelFormat.Rgba32,
      PixelData = data,
    };
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesTheFixedFileSize()
    => Assert.That(RamBrandtWriter.ToBytes(_Sample(RamBrandtMode.Graphics7, _ => 0)), Has.Length.EqualTo(8192));

  [Test]
  [Category("Unit")]
  public void ToBytes_PlacesTheSectionsAtTheDocumentedOffsets() {
    var bytes = RamBrandtWriter.ToBytes(_Sample(RamBrandtMode.Graphics7, _ => 0xFF));

    Assert.Multiple(() => {
      Assert.That(bytes[RamBrandtFile.BitmapDataSize - 1], Is.EqualTo(0xFF));
      Assert.That(bytes[RamBrandtFile.ColorsOffset + 4], Is.EqualTo(0x28));
      Assert.That(bytes[RamBrandtFile.DisplayListOffset], Is.EqualTo(0x00));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesEverySection() {
    var file = _Sample(RamBrandtMode.Graphics7, i => (byte)(i * 13 % 256));
    var restored = RamBrandtReader.FromBytes(RamBrandtWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.BitmapData, Is.EqualTo(file.BitmapData));
      Assert.That(restored.Colors, Is.EqualTo(file.Colors));
      Assert.That(restored.DisplayList, Is.EqualTo(file.DisplayList));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAWronglySizedFile()
    => Assert.Throws<InvalidDataException>(() => RamBrandtReader.FromBytes(new byte[7680]));

  [TestCase(".rm0", RamBrandtMode.Graphics7)]
  [TestCase(".rm1", RamBrandtMode.Graphics9)]
  [TestCase(".rm2", RamBrandtMode.Graphics10)]
  [TestCase(".RM3", RamBrandtMode.Graphics11)]
  [TestCase(".rm4", RamBrandtMode.Graphics15)]
  [Category("Unit")]
  public void ModeFromExtension_NamesTheAnticMode(string extension, RamBrandtMode expected)
    => Assert.That(RamBrandtReader.ModeFromExtension(extension), Is.EqualTo(expected));

  [TestCase(RamBrandtMode.Graphics7)]
  [TestCase(RamBrandtMode.Graphics9)]
  [TestCase(RamBrandtMode.Graphics10)]
  [TestCase(RamBrandtMode.Graphics11)]
  [TestCase(RamBrandtMode.Graphics15)]
  [Category("Unit")]
  public void ToRawImage_DecodesEveryModeAtTheDisplayedSize(RamBrandtMode mode) {
    var raw = RamBrandtFile.ToRawImage(_Sample(mode, i => (byte)(i * 7 % 256)));

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(RamBrandtFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(RamBrandtFile.DisplayHeight));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.PaletteCount, Is.GreaterThan(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Graphics7ShowsEachStoredRowTwice() {
    var raw = RamBrandtFile.ToRawImage(_Sample(RamBrandtMode.Graphics7, i => (byte)(i * 7 % 256)));
    var stride = RamBrandtFile.DisplayWidth;

    Assert.That(raw.PixelData[stride..(2 * stride)], Is.EqualTo(raw.PixelData[..stride]));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AppliesADisplayListInterrupt() {
    var file = _Sample(RamBrandtMode.Graphics7, _ => 0);

    // Row 10 is spelled 15 in the table: entries are biased by five so that zero can mean "none".
    file.DisplayList[0] = 15;
    file.DisplayList[RamBrandtFile.DisplayListEntries + 16 + 10] = 8;
    file.DisplayList[RamBrandtFile.DisplayListEntries * 2 + 16 + 10] = 0x9A;

    var raw = RamBrandtFile.ToRawImage(file);
    var stride = RamBrandtFile.DisplayWidth;

    // A blank bitmap draws the background register everywhere, so reloading it must change the
    // colour from the interrupt line downwards but not above it.
    Assert.Multiple(() => {
      Assert.That(raw.PixelData[10 * 2 * stride], Is.EqualTo(raw.PixelData[0]));
      Assert.That(raw.PixelData[11 * 2 * stride], Is.Not.EqualTo(raw.PixelData[0]));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAReadableGraphics7Screen() {
    var file = RamBrandtFile.FromRawImage(_Gradient());

    Assert.Multiple(() => {
      Assert.That(file.Mode, Is.EqualTo(RamBrandtMode.Graphics7));
      Assert.That(file.DisplayList, Is.All.Zero, "a converted picture uses one palette throughout");
      Assert.That(() => RamBrandtReader.FromBytes(RamBrandtWriter.ToBytes(file)), Throws.Nothing);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsOtherSizes() {
    var raw = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgba32, PixelData = new byte[320 * 200 * 4] };

    Assert.Throws<ArgumentException>(() => RamBrandtFile.FromRawImage(raw));
  }
}

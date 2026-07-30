using System;
using System.IO;
using FileFormat.Core;
using FileFormat.ZxAttributesGigascreen;

namespace FileFormat.ZxAttributesGigascreen.Tests;

[TestFixture]
public sealed class ZxAttributesGigascreenTests {

  private static ZxAttributesGigascreenFile _Sample() {
    var first = new byte[ZxAttributesGigascreenFile.AttributesSize];
    var second = new byte[ZxAttributesGigascreenFile.AttributesSize];
    for (var i = 0; i < first.Length; ++i) {
      first[i] = (byte)(i & 0x7F);
      second[i] = (byte)((i * 3) & 0x7F);
    }

    var dither = new byte[8];
    Array.Fill(dither, (byte)0b10101010);

    return new() { Dither = dither, FirstAttributes = first, SecondAttributes = second };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is1628() {
    // 92-byte header (including the loader and dither) plus two 768-byte attribute sets.
    Assert.That(ZxAttributesGigascreenFile.FileSize, Is.EqualTo(1628));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmitsTheLoaderStub() {
    var bytes = ZxAttributesGigascreenWriter.ToBytes(_Sample());

    Assert.That(bytes[..ZxAttributesGigascreenFile.LoaderSignature.Length],
      Is.EqualTo(ZxAttributesGigascreenFile.LoaderSignature.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesBothAttributeSetsAndTheDither() {
    var original = _Sample();
    var restored = ZxAttributesGigascreenReader.FromBytes(ZxAttributesGigascreenWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Dither, Is.EqualTo(original.Dither));
      Assert.That(restored.FirstAttributes, Is.EqualTo(original.FirstAttributes));
      Assert.That(restored.SecondAttributes, Is.EqualTo(original.SecondAttributes));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => ZxAttributesGigascreenReader.FromBytes(new byte[1627]));

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsDataWithoutTheLoaderStub()
    => Assert.Throws<InvalidDataException>(
      () => ZxAttributesGigascreenReader.FromBytes(new byte[ZxAttributesGigascreenFile.FileSize]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_AveragesTheTwoAttributeSets() {
    // One cell set to black in one frame and white in the other must land on mid-grey.
    var file = _Sample();
    Array.Clear(file.FirstAttributes);
    Array.Clear(file.SecondAttributes);
    file.FirstAttributes[0] = ZxSpectrumGraphics.Attribute(0, 0);
    file.SecondAttributes[0] = ZxSpectrumGraphics.Attribute(15, 15);

    var raw = ZxAttributesGigascreenFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(ZxSpectrumGraphics.ScreenWidth));
      Assert.That(raw.PixelData[0], Is.EqualTo(127).Within(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var w = ZxSpectrumGraphics.ScreenWidth;
    var h = ZxSpectrumGraphics.ScreenHeight;
    var data = new byte[w * h * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 1] = (byte)(i % 199);
      data[i + 3] = 255;
    }

    var raw = new RawImage { Width = w, Height = h, Format = PixelFormat.Rgba32, PixelData = data };

    Assert.That(ZxAttributesGigascreenWriter.ToBytes(ZxAttributesGigascreenFile.FromRawImage(raw)),
      Has.Length.EqualTo(ZxAttributesGigascreenFile.FileSize));
  }
}

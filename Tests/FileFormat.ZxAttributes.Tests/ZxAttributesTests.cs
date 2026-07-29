using System;
using System.IO;
using FileFormat.Core;
using FileFormat.ZxAttributes;

namespace FileFormat.ZxAttributes.Tests;

[TestFixture]
public sealed class ZxAttributesTests {

  private static ZxAttributesFile _Sample() {
    var data = new byte[ZxAttributesFile.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0x7F);

    return new() { AttributeData = data };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is768() {
    // 32 x 24 attribute cells, one byte each.
    Assert.That(ZxAttributesFile.FileSize, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAttributes() {
    var original = _Sample();
    var restored = ZxAttributesReader.FromBytes(ZxAttributesWriter.ToBytes(original));

    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => ZxAttributesReader.FromBytes(new byte[767]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ShowsBothColoursOfEachCell() {
    // The dither must expose ink and paper; a flat fill would hide half the information.
    var file = new ZxAttributesFile { AttributeData = new byte[ZxAttributesFile.FileSize] };
    file.AttributeData[0] = ZxSpectrumGraphics.Attribute(ink: 2, paper: 5);

    var raw = ZxAttributesFile.ToRawImage(file);
    var distinct = new System.Collections.Generic.HashSet<byte>();
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      distinct.Add(raw.PixelData[y * ZxSpectrumGraphics.ScreenWidth + x]);

    Assert.That(distinct, Has.Count.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheScreenResolution() {
    var raw = ZxAttributesFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(ZxSpectrumGraphics.ScreenWidth));
      Assert.That(raw.Height, Is.EqualTo(ZxSpectrumGraphics.ScreenHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(ZxSpectrumGraphics.PaletteEntryCount));
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
      data[i + 2] = (byte)(i % 199);
      data[i + 3] = 255;
    }

    var raw = new RawImage { Width = w, Height = h, Format = PixelFormat.Rgba32, PixelData = data };

    Assert.That(ZxAttributesWriter.ToBytes(ZxAttributesFile.FromRawImage(raw)),
      Has.Length.EqualTo(ZxAttributesFile.FileSize));
  }
}

using System;
using FileFormat.Png;

namespace FileFormat.Png.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PngColorType_HasSpecValues() {
    Assert.That((byte)PngColorType.Grayscale, Is.EqualTo(0));
    Assert.That((byte)PngColorType.RGB, Is.EqualTo(2));
    Assert.That((byte)PngColorType.Palette, Is.EqualTo(3));
    Assert.That((byte)PngColorType.GrayscaleAlpha, Is.EqualTo(4));
    Assert.That((byte)PngColorType.RGBA, Is.EqualTo(6));
    Assert.That(Enum.GetValues<PngColorType>(), Has.Length.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void PngFilterType_HasExpectedValues() {
    Assert.That((int)PngFilterType.None, Is.EqualTo(0));
    Assert.That((int)PngFilterType.Sub, Is.EqualTo(1));
    Assert.That((int)PngFilterType.Up, Is.EqualTo(2));
    Assert.That((int)PngFilterType.Average, Is.EqualTo(3));
    Assert.That((int)PngFilterType.Paeth, Is.EqualTo(4));
    Assert.That(Enum.GetValues<PngFilterType>(), Has.Length.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void PngInterlaceMethod_HasExpectedValues() {
    Assert.That((byte)PngInterlaceMethod.None, Is.EqualTo(0));
    Assert.That((byte)PngInterlaceMethod.Adam7, Is.EqualTo(1));
    Assert.That(Enum.GetValues<PngInterlaceMethod>(), Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void PngChunk_RecordStruct_StoresData() {
    var data = new byte[] { 1, 2, 3, 4 };
    var chunk = new PngChunk("tESt", data);

    Assert.That(chunk.Type, Is.EqualTo("tESt"));
    Assert.That(chunk.Data, Is.EqualTo(data));
  }
}

using System;
using FileFormat.Nrrd;

namespace FileFormat.Nrrd.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void NrrdEncoding_HasExpectedValues() {
    Assert.That((int)NrrdEncoding.Raw, Is.EqualTo(0));
    Assert.That((int)NrrdEncoding.Ascii, Is.EqualTo(1));
    Assert.That((int)NrrdEncoding.Hex, Is.EqualTo(2));
    Assert.That((int)NrrdEncoding.Gzip, Is.EqualTo(3));

    var values = Enum.GetValues<NrrdEncoding>();
    Assert.That(values, Has.Length.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void NrrdType_HasExpectedValues() {
    Assert.That((int)NrrdType.Int8, Is.EqualTo(0));
    Assert.That((int)NrrdType.UInt8, Is.EqualTo(1));
    Assert.That((int)NrrdType.Int16, Is.EqualTo(2));
    Assert.That((int)NrrdType.UInt16, Is.EqualTo(3));
    Assert.That((int)NrrdType.Int32, Is.EqualTo(4));
    Assert.That((int)NrrdType.UInt32, Is.EqualTo(5));
    Assert.That((int)NrrdType.Float, Is.EqualTo(6));
    Assert.That((int)NrrdType.Double, Is.EqualTo(7));

    var values = Enum.GetValues<NrrdType>();
    Assert.That(values, Has.Length.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void NrrdFile_DefaultValues() {
    var file = new NrrdFile();

    Assert.That(file.Sizes, Is.Empty);
    Assert.That(file.DataType, Is.EqualTo(NrrdType.Int8));
    Assert.That(file.Encoding, Is.EqualTo(NrrdEncoding.Raw));
    Assert.That(file.Endian, Is.EqualTo("little"));
    Assert.That(file.Spacings, Is.Empty);
    Assert.That(file.PixelData, Is.Empty);
    Assert.That(file.Labels, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void NrrdFile_PropertiesRoundTrip() {
    var sizes = new[] { 10, 20 };
    var spacings = new[] { 1.5, 2.0 };
    var pixels = new byte[] { 1, 2, 3 };
    var labels = new[] { "x", "y" };

    var file = new NrrdFile {
      Sizes = sizes,
      DataType = NrrdType.Float,
      Encoding = NrrdEncoding.Gzip,
      Endian = "big",
      Spacings = spacings,
      PixelData = pixels,
      Labels = labels
    };

    Assert.That(file.Sizes, Is.SameAs(sizes));
    Assert.That(file.DataType, Is.EqualTo(NrrdType.Float));
    Assert.That(file.Encoding, Is.EqualTo(NrrdEncoding.Gzip));
    Assert.That(file.Endian, Is.EqualTo("big"));
    Assert.That(file.Spacings, Is.SameAs(spacings));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Labels, Is.SameAs(labels));
  }
}

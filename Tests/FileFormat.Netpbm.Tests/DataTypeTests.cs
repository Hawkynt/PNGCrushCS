using System;
using FileFormat.Netpbm;

namespace FileFormat.Netpbm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void NetpbmFormat_HasExpectedValues() {
    Assert.That((int)NetpbmFormat.PbmAscii, Is.EqualTo(1));
    Assert.That((int)NetpbmFormat.PgmAscii, Is.EqualTo(2));
    Assert.That((int)NetpbmFormat.PpmAscii, Is.EqualTo(3));
    Assert.That((int)NetpbmFormat.PbmBinary, Is.EqualTo(4));
    Assert.That((int)NetpbmFormat.PgmBinary, Is.EqualTo(5));
    Assert.That((int)NetpbmFormat.PpmBinary, Is.EqualTo(6));
    Assert.That((int)NetpbmFormat.Pam, Is.EqualTo(7));

    var values = Enum.GetValues<NetpbmFormat>();
    Assert.That(values, Has.Length.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void NetpbmFile_DefaultPixelData_IsEmpty() {
    var file = new NetpbmFile {
      Format = NetpbmFormat.PpmBinary,
      Width = 0,
      Height = 0,
      MaxValue = 255,
      Channels = 3
    };

    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void NetpbmFile_TupleType_DefaultsToNull() {
    var file = new NetpbmFile {
      Format = NetpbmFormat.PpmBinary,
      Width = 1,
      Height = 1,
      MaxValue = 255,
      Channels = 3
    };

    Assert.That(file.TupleType, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void NetpbmFile_InitProperties_StoreCorrectly() {
    var pixelData = new byte[] { 1, 2, 3 };
    var file = new NetpbmFile {
      Format = NetpbmFormat.Pam,
      Width = 10,
      Height = 20,
      MaxValue = 65535,
      Channels = 4,
      PixelData = pixelData,
      TupleType = "RGB_ALPHA"
    };

    Assert.That(file.Format, Is.EqualTo(NetpbmFormat.Pam));
    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(20));
    Assert.That(file.MaxValue, Is.EqualTo(65535));
    Assert.That(file.Channels, Is.EqualTo(4));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
    Assert.That(file.TupleType, Is.EqualTo("RGB_ALPHA"));
  }
}

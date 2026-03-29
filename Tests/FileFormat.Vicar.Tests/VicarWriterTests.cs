using System;
using System.Text;
using FileFormat.Vicar;

namespace FileFormat.Vicar.Tests;

[TestFixture]
public sealed class VicarWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithLblsize() {
    var file = new VicarFile {
      Width = 4,
      Height = 2,
      Bands = 1,
      PixelType = VicarPixelType.Byte,
      Organization = VicarOrganization.Bsq,
      PixelData = new byte[4 * 2]
    };

    var bytes = VicarWriter.ToBytes(file);
    var prefix = Encoding.ASCII.GetString(bytes, 0, 8);

    Assert.That(prefix, Is.EqualTo("LBLSIZE="));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VicarWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsPixelData() {
    var pixels = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
    var file = new VicarFile {
      Width = 2,
      Height = 2,
      Bands = 1,
      PixelType = VicarPixelType.Byte,
      Organization = VicarOrganization.Bsq,
      PixelData = pixels
    };

    var bytes = VicarWriter.ToBytes(file);

    var headerText = Encoding.ASCII.GetString(bytes, 0, 8);
    Assert.That(headerText, Does.StartWith("LBLSIZE="));

    var parsed = VicarReader.FromBytes(bytes);
    var lblSize = int.Parse(parsed.Labels["LBLSIZE"]);

    Assert.That(bytes[lblSize], Is.EqualTo(0xDE));
    Assert.That(bytes[lblSize + 1], Is.EqualTo(0xAD));
    Assert.That(bytes[lblSize + 2], Is.EqualTo(0xBE));
    Assert.That(bytes[lblSize + 3], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsFormatLabel() {
    var file = new VicarFile {
      Width = 4,
      Height = 2,
      Bands = 1,
      PixelType = VicarPixelType.Half,
      Organization = VicarOrganization.Bsq,
      PixelData = new byte[4 * 2 * 2]
    };

    var bytes = VicarWriter.ToBytes(file);
    var parsed = VicarReader.FromBytes(bytes);
    var lblSize = int.Parse(parsed.Labels["LBLSIZE"]);
    var headerText = Encoding.ASCII.GetString(bytes, 0, lblSize);

    Assert.That(headerText, Does.Contain("FORMAT=HALF"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsOrgLabel() {
    var file = new VicarFile {
      Width = 4,
      Height = 2,
      Bands = 1,
      PixelType = VicarPixelType.Byte,
      Organization = VicarOrganization.Bil,
      PixelData = new byte[4 * 2]
    };

    var bytes = VicarWriter.ToBytes(file);
    var parsed = VicarReader.FromBytes(bytes);
    var lblSize = int.Parse(parsed.Labels["LBLSIZE"]);
    var headerText = Encoding.ASCII.GetString(bytes, 0, lblSize);

    Assert.That(headerText, Does.Contain("ORG=BIL"));
  }
}

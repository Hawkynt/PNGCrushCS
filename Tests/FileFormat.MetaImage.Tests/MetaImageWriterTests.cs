using System;
using System.Text;
using FileFormat.MetaImage;

namespace FileFormat.MetaImage.Tests;

[TestFixture]
public sealed class MetaImageWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => MetaImageWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsObjectType() {
    var file = new MetaImageFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[4]
    };

    var bytes = MetaImageWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("ObjectType = Image"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsDimSize() {
    var file = new MetaImageFile {
      Width = 10,
      Height = 20,
      PixelData = new byte[200]
    };

    var bytes = MetaImageWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("DimSize = 10 20"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsElementType() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      ElementType = MetaImageElementType.MetUChar,
      PixelData = new byte[1]
    };

    var bytes = MetaImageWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("ElementType = MET_UCHAR"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsElementDataFileLocal() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[1]
    };

    var bytes = MetaImageWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("ElementDataFile = LOCAL"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataFollowsHeader() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      PixelData = [0xAB]
    };

    var bytes = MetaImageWriter.ToBytes(file);

    Assert.That(bytes[^1], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RgbIncludesChannels() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      Channels = 3,
      PixelData = new byte[3]
    };

    var bytes = MetaImageWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("ElementNumberOfChannels = 3"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleChannelOmitsChannelTag() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      Channels = 1,
      PixelData = new byte[1]
    };

    var bytes = MetaImageWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Not.Contain("ElementNumberOfChannels"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CompressedIncludesFlag() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      IsCompressed = true,
      PixelData = new byte[1]
    };

    var bytes = MetaImageWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("CompressedData = True"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MetShortElementType() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      ElementType = MetaImageElementType.MetShort,
      PixelData = new byte[2]
    };

    var bytes = MetaImageWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("ElementType = MET_SHORT"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MetFloatElementType() {
    var file = new MetaImageFile {
      Width = 1,
      Height = 1,
      ElementType = MetaImageElementType.MetFloat,
      PixelData = new byte[4]
    };

    var bytes = MetaImageWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("ElementType = MET_FLOAT"));
  }
}

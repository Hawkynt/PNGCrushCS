using System;
using System.IO;
using System.Text;
using FileFormat.XvThumbnail;

namespace FileFormat.XvThumbnail.Tests;

[TestFixture]
public sealed class XvThumbnailConformanceTests {

  [Test]
  [Category("Unit")]
  public void Reader_AcceptsCrLfCommentsAndWhitespaceSeparatedDimensions() {
    var header = Encoding.ASCII.GetBytes("P7 332\r\n#XVVERSION:Version 3.10a\r\n#END_OF_COMMENTS\r\n2\t2  255\r\n");
    byte[] raster = [0xE0, 0x1C, 0x03, 0xFF];
    var data = new byte[header.Length + raster.Length];
    header.CopyTo(data, 0);
    raster.CopyTo(data, header.Length);

    var file = XvThumbnailReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.PixelData, Is.EqualTo(raster));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_TruncatedRaster_IsRejectedInsteadOfBlackPadding() {
    var data = Encoding.ASCII.GetBytes("P7 332\n2 2 255\n");
    var truncated = new byte[data.Length + 3];
    data.CopyTo(truncated, 0);
    truncated[^3] = 0xE0;
    truncated[^2] = 0x1C;
    truncated[^1] = 0x03;

    var exception = Assert.Throws<InvalidDataException>(() => XvThumbnailReader.FromBytes(truncated));
    Assert.That(exception!.Message, Does.Contain("Truncated XV thumbnail raster"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_Non255Maxval_IsRejected() {
    var data = Encoding.ASCII.GetBytes("P7 332\n1 1 31\n\0");

    var exception = Assert.Throws<InvalidDataException>(() => XvThumbnailReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("maxval must be 255"));
  }

  [Test]
  [Category("Unit")]
  public void Writer_UndersizedRaster_IsRejectedInsteadOfBlackPadding() {
    var file = new XvThumbnailFile {
      Width = 2,
      Height = 2,
      PixelData = [0xE0, 0x1C, 0x03],
    };

    Assert.Throws<ArgumentException>(() => XvThumbnailWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void Writer_EmitsCanonicalCommentTerminatorAndMaxval() {
    var file = new XvThumbnailFile {
      Width = 2,
      Height = 1,
      PixelData = [0xE0, 0x03],
    };

    var data = XvThumbnailWriter.ToBytes(file);
    var prefix = Encoding.ASCII.GetBytes("P7 332\n#END_OF_COMMENTS\n2 1 255\n");

    Assert.Multiple(() => {
      Assert.That(data.AsSpan(0, prefix.Length).ToArray(), Is.EqualTo(prefix));
      Assert.That(data[^2..], Is.EqualTo(file.PixelData));
    });
  }
}

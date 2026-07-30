using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Jpeg;
using FileFormat.Mpo;

namespace FileFormat.Mpo.Tests;

[TestFixture]
public sealed class MpoReaderTests {

  private static byte[] _CreateMinimalJpegBytes(int width = 4, int height = 4) {
    var rgb = new byte[width * height * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i * 7 % 256);

    var jpeg = new JpegFile {
      Width = width,
      Height = height,
      IsGrayscale = false,
      RgbPixelData = rgb,
    };
    return JpegWriter.ToBytes(jpeg);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MpoReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MpoReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => MpoReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mpo"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MpoReader.FromBytes(new byte[] { 0xFF, 0xD8 }));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MpoReader.FromBytes(new byte[] { 0x00, 0x00, 0x00, 0x00 }));

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleJpeg_ParsesOneImage() {
    var jpegBytes = _CreateMinimalJpegBytes();

    var result = MpoReader.FromBytes(jpegBytes);

    Assert.That(result.Images.Count, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TwoConcatenatedJpegs_ParsesTwoImages() {
    var jpeg1 = _CreateMinimalJpegBytes(4, 4);
    var jpeg2 = _CreateMinimalJpegBytes(8, 8);
    var combined = new byte[jpeg1.Length + jpeg2.Length];
    Array.Copy(jpeg1, 0, combined, 0, jpeg1.Length);
    Array.Copy(jpeg2, 0, combined, jpeg1.Length, jpeg2.Length);

    var result = MpoReader.FromBytes(combined);

    Assert.That(result.Images.Count, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MpoWithMpfMarker_ParsesCorrectly() {
    var jpeg1 = _CreateMinimalJpegBytes(4, 4);
    var jpeg2 = _CreateMinimalJpegBytes(8, 8);

    var mpoBytes = MpoWriter.ToBytes(new MpoFile { Images = [jpeg1, jpeg2] });
    var result = MpoReader.FromBytes(mpoBytes);

    Assert.That(result.Images.Count, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MpoReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var jpegBytes = _CreateMinimalJpegBytes();
    using var ms = new MemoryStream(jpegBytes);

    var result = MpoReader.FromStream(ms);

    Assert.That(result.Images.Count, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_EachImageIsValidJpeg() {
    var jpeg1 = _CreateMinimalJpegBytes(4, 4);
    var jpeg2 = _CreateMinimalJpegBytes(8, 8);

    var mpoBytes = MpoWriter.ToBytes(new MpoFile { Images = [jpeg1, jpeg2] });
    var result = MpoReader.FromBytes(mpoBytes);

    foreach (var image in result.Images) {
      Assert.That(image.Length, Is.GreaterThan(2));
      Assert.That(image[0], Is.EqualTo(0xFF));
      Assert.That(image[1], Is.EqualTo(0xD8));
    }
  }
}

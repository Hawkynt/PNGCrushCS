using System;
using System.Buffers.Binary;
using System.IO;

namespace Optimizer.Image.Tests;

[TestFixture]
public sealed class HeifDetectionTests {

  [TestCase("heis")]
  [TestCase("heim")]
  [TestCase("hevm")]
  [TestCase("hevs")]
  [TestCase("avci")]
  [TestCase("avcs")]
  public void DetectFromSignature_RegisteredHeifMajorBrands_ReturnHeif(string brand) {
    Assert.That(ImageFormatDetector.DetectFromSignature(_Ftyp(brand, "mif1")), Is.EqualTo(ImageFormat.Heif));
  }

  [Test]
  public void DetectFromSignature_ExtendedSizeFtypWithHeicBrand_ReturnsHeif() {
    var body = new byte[12];
    System.Text.Encoding.ASCII.GetBytes("heic", 0, 4, body, 0);
    System.Text.Encoding.ASCII.GetBytes("mif1", 0, 4, body, 8);

    var data = new byte[16 + body.Length];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 1);
    System.Text.Encoding.ASCII.GetBytes("ftyp", 0, 4, data, 4);
    BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(8), checked((ulong)data.Length));
    body.CopyTo(data.AsSpan(16));

    Assert.That(ImageFormatDetector.DetectFromSignature(data), Is.EqualTo(ImageFormat.Heif));
  }

  [Test]
  public void DetectFromSignature_AvifMajorBrandIsNotClaimedAsHeif() {
    Assert.That(ImageFormatDetector.DetectFromSignature(_Ftyp("avif", "mif1")), Is.EqualTo(ImageFormat.Avif));
  }

  [Test]
  public void DetectFromExtension_HifExtension_ReturnsHeif() {
    Assert.That(ImageFormatDetector.DetectFromExtension(new FileInfo("picture.hif")), Is.EqualTo(ImageFormat.Heif));
  }

  private static byte[] _Ftyp(string majorBrand, string compatibleBrand) {
    var data = new byte[24];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), checked((uint)data.Length));
    System.Text.Encoding.ASCII.GetBytes("ftyp", 0, 4, data, 4);
    System.Text.Encoding.ASCII.GetBytes(majorBrand, 0, 4, data, 8);
    System.Text.Encoding.ASCII.GetBytes(compatibleBrand, 0, 4, data, 16);
    return data;
  }
}

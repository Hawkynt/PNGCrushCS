using System;
using System.Buffers.Binary;
using FileFormat.Core;
using FileFormat.Core.PixelFormats;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class CfaRawImageTests {

  [Test]
  public void Cfa16CarriesPhaseAndEffectivePrecision() {
    byte[] pixels = new byte[2 * 2 * 2];
    BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(0), 100);
    BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(2), 200);
    BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(4), 300);
    BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(6), 400);

    var info = new RawCfaInfo(RawCfaPattern.Rggb, 12);
    var image = new RawImage<Cfa16>(2, 2, pixels, cfaInfo: info);

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Cfa16));
      Assert.That(image.IsColorFilterArray, Is.True);
      Assert.That(image.CfaInfo, Is.EqualTo(info));
      Assert.That(image.PlaneCount, Is.EqualTo(1));
      Assert.That(image.MinimumPixelDataLength, Is.EqualTo(8));
      Assert.That(image.Untyped.IsColorFilterArray, Is.True);
      Assert.That(image.Untyped.CfaInfo, Is.EqualTo(info));
    });
  }

  [Test]
  public void Cfa16RequiresSemanticDescription() {
    Assert.That(
      () => new RawImage<Cfa16>(2, 2, new byte[8]),
      Throws.ArgumentException.With.Message.Contains("CfaInfo")
    );
  }

  [Test]
  public void Cfa16RejectsSampleOutsideDeclaredBitDepth() {
    byte[] pixels = new byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(pixels, 4096);

    Assert.That(
      () => new RawImage<Cfa16>(1, 1, pixels, cfaInfo: new(RawCfaPattern.Bggr, 12)),
      Throws.ArgumentException.With.Message.Contains("4096")
    );
  }

  [TestCase(RawCfaPattern.Rggb)]
  [TestCase(RawCfaPattern.Grbg)]
  [TestCase(RawCfaPattern.Gbrg)]
  [TestCase(RawCfaPattern.Bggr)]
  public void EveryBayerPhaseIsRepresentable(RawCfaPattern pattern) {
    var image = new RawImage<Cfa16>(
      2,
      2,
      [0, 0, 1, 0, 2, 0, 3, 0],
      cfaInfo: new(pattern, 16)
    );

    Assert.That(image.CfaInfo!.Value.Pattern, Is.EqualTo(pattern));
  }

  [Test]
  public void TypedViewPreservesCfaInfoWithoutCopying() {
    var pixels = new byte[8];
    var source = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Cfa16,
      PixelData = pixels,
      CfaInfo = new(RawCfaPattern.Grbg, 10),
    };

    var typed = RawImage<Cfa16>.FromUntyped(source);

    Assert.Multiple(() => {
      Assert.That(typed.Untyped, Is.SameAs(source));
      Assert.That(typed.PixelData, Is.SameAs(pixels));
      Assert.That(typed.CfaInfo, Is.EqualTo(source.CfaInfo));
    });
  }

  [Test]
  public void RawCfaInfoRejectsInvalidPrecision() {
    Assert.Throws<ArgumentOutOfRangeException>(() => new RawCfaInfo(RawCfaPattern.Rggb, 0));
    Assert.Throws<ArgumentOutOfRangeException>(() => new RawCfaInfo(RawCfaPattern.Rggb, 17));
  }
}

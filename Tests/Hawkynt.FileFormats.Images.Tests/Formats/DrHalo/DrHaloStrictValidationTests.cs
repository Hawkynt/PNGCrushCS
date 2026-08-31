using System.IO;

namespace FileFormat.DrHalo.Tests;

[TestFixture]
public sealed class DrHaloStrictValidationTests {

  [Test]
  [Category("Unit")]
  public void Reader_NonZeroReservedField_IsRejected() {
    Assert.Throws<InvalidDataException>(() => DrHaloReader.FromBytes([
      1, 0, 1, 0, 1, 0,
      3, 0, 1, 0x2A, 0,
    ]));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TruncatedScanlineLength_IsRejected() {
    Assert.Throws<InvalidDataException>(() => DrHaloReader.FromBytes([
      1, 0, 1, 0, 0, 0,
      3,
    ]));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TruncatedScanlinePayload_IsRejected() {
    Assert.Throws<InvalidDataException>(() => DrHaloReader.FromBytes([
      1, 0, 1, 0, 0, 0,
      3, 0, 1, 0x2A,
    ]));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TrailingData_IsRejected() {
    Assert.Throws<InvalidDataException>(() => DrHaloReader.FromBytes([
      1, 0, 1, 0, 0, 0,
      3, 0, 1, 0x2A, 0,
      0x99,
    ]));
  }

  [Test]
  [Category("Unit")]
  public void Reader_MicroDesignLikeCut_IsNotMisdetectedAsDrHalo() {
    Assert.Throws<InvalidDataException>(() => DrHaloReader.FromBytes([
      5, 0, 6, 0,
      0, 0, 0, 0, 0, 0, 0, 0,
    ]));
  }

  [Test]
  [Category("Unit")]
  public void Rle_80Terminator_IsAcceptedBySpecification() {
    var decoded = DrHaloRleCompressor.DecompressScanline([1, 0x2A, 0x80], 1);
    Assert.That(decoded, Is.EqualTo(new byte[] { 0x2A }));
  }

  [Test]
  [Category("Unit")]
  public void Rle_EarlyTerminator_IsRejected() {
    Assert.Throws<InvalidDataException>(() => DrHaloRleCompressor.DecompressScanline([0], 1));
  }

  [Test]
  [Category("Unit")]
  public void Rle_PacketOverrun_IsRejected() {
    Assert.Throws<InvalidDataException>(() => DrHaloRleCompressor.DecompressScanline([0x82, 0x2A, 0], 1));
  }

  [Test]
  [Category("Unit")]
  public void Rle_TruncatedRepeatPacket_IsRejected() {
    Assert.Throws<InvalidDataException>(() => DrHaloRleCompressor.DecompressScanline([0x81], 1));
  }

  [Test]
  [Category("Unit")]
  public void Rle_TruncatedLiteralPacket_IsRejected() {
    Assert.Throws<InvalidDataException>(() => DrHaloRleCompressor.DecompressScanline([2, 0x11], 2));
  }

  [Test]
  [Category("Unit")]
  public void Rle_MissingTerminator_IsRejected() {
    Assert.Throws<InvalidDataException>(() => DrHaloRleCompressor.DecompressScanline([1, 0x2A], 1));
  }

  [Test]
  [Category("Unit")]
  public void Rle_DataAfterTerminator_IsRejected() {
    Assert.Throws<InvalidDataException>(() => DrHaloRleCompressor.DecompressScanline([1, 0x2A, 0, 0], 1));
  }
}

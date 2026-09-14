using System.IO;

namespace FileFormat.Codecs.H263.Tests;

/// <summary>Malformed-input and boundary checks found during the Annex O diff audit.</summary>
[TestFixture]
public sealed class H263AnnexOValidationTests {

  private const int _PICTURE_START_CODE = 1 << 5;

  [Test]
  [Category("Unit")]
  public void ForwardVectorMaySelectExactlyFifteenPixelsOutsideThePicture() {
    var bits = new H263TestStream()
      .Coded()
      .Code("100")              // Table O.1: Forward, no texture
      .Code("0000 0000 0101")  // MVD x = -30 half-pixels = -15 pixels
      .Code("1")                // MVD y = 0
      .ToArray();

    Assert.DoesNotThrow(() => _DecodeForwardMacroblock(bits));
  }

  [Test]
  [Category("Unit")]
  public void ForwardVectorMayNotSelectAFullSixteenthPixelOutsideThePicture() {
    var bits = new H263TestStream()
      .Coded()
      .Code("100")                // Table O.1: Forward, no texture
      .Code("0000 0000 0010 1")  // MVD x = -32 half-pixels = -16 pixels
      .Code("1")                  // MVD y = 0
      .ToArray();

    var failure = Assert.Throws<InvalidDataException>(() => _DecodeForwardMacroblock(bits));
    Assert.That(failure!.Message, Does.Contain("15 pixels"));
  }

  [TestCase(H263PictureKind.ImprovedPb, "Improved PB")]
  [TestCase(H263PictureKind.EnhancementPredicted, "EnhancementPredicted")]
  [Category("Unit")]
  public void LegalRtypeOneOnUnsupportedPictureKindsReachesTheUnsupportedModeRefusal(
    H263PictureKind pictureKind,
    string expectedMessage) {
    var data = _PlusHeaderWithRtypeOne(pictureKind);

    var failure = Assert.Throws<NotSupportedException>(() => _ParseAfterPictureStart(data));
    Assert.That(failure!.Message, Does.Contain(expectedMessage));
  }

  private static void _DecodeForwardMacroblock(byte[] bits) {
    var past = _Reference(temporalReference: 10);
    var future = _Reference(temporalReference: 12);
    var target = new H263Frame(1, 1);
    var reader = new H263BitReader(bits);
    var decoder = new H263BidirectionalPictureDecoder(_BHeader(), target, past, future);
    decoder.DecodePicture(ref reader);
  }

  private static H263Frame _Reference(int temporalReference) {
    var result = new H263Frame(1, 1) { TemporalReference = temporalReference };
    result.Luma.AsSpan().Fill(96);
    result.Cb.AsSpan().Fill(128);
    result.Cr.AsSpan().Fill(128);
    return result;
  }

  private static H263PictureHeader _BHeader() => new() {
    Width = 16,
    Height = 16,
    MacroblockRowsPerGroup = 1,
    IsIntra = false,
    IsReference = false,
    Quantiser = 1,
    HasWideEscapeLevel = false,
    HasGroupLayer = true,
    AllowsVectorsOutsidePicture = true,
    TemporalReference = 11,
    PictureKind = H263PictureKind.Bidirectional,
    RoundingType = 0,
    EnhancementLayerNumber = 2,
    ReferenceLayerNumber = 1,
  };

  private static H263PictureHeader _ParseAfterPictureStart(byte[] data) {
    var reader = new H263BitReader(data);
    reader.Skip(22);
    return H263PictureHeader.Parse(ref reader);
  }

  /// <summary>
  /// Writes enough of a full-UFEP PLUSPTYPE to reach MPPTYPE's RTYPE field and the mode-specific
  /// refusal. Keeping this independent of the production header writer makes it a syntax oracle.
  /// </summary>
  private static byte[] _PlusHeaderWithRtypeOne(H263PictureKind pictureKind) {
    var writer = new H263BitWriter();
    writer.Write(_PICTURE_START_CODE, 22);
    writer.Write(0, 8); // TR
    writer.WriteBit(1);
    writer.WriteBit(0);
    writer.Write(0, 3); // display flags
    writer.Write(7, 3); // PLUSPTYPE

    writer.Write(1, 3); // UFEP=001
    writer.Write(1, 3); // sub-QCIF source format
    writer.WriteBit(0); // custom PCF
    writer.Write(0, 10); // optional coding modes
    writer.WriteBit(1); // OPPTYPE marker
    writer.Write(0, 3); // reserved

    writer.Write((int)pictureKind, 3);
    writer.Write(0, 2); // RPR/RRU
    writer.WriteBit(1); // RTYPE
    writer.Write(0, 2); // reserved
    writer.WriteBit(1); // MPPTYPE marker

    writer.WriteBit(0); // CPM
    writer.Write(1, 5); // PQUANT
    writer.WriteBit(0); // PEI
    return writer.ToArray();
  }
}

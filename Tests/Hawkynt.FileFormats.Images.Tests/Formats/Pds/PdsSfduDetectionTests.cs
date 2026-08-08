using System.Text;
using FileFormat.Pds;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Pds.Tests;

/// <summary>
/// An archived PDS label opens with an SFDU label rather than the keyword, and only the keyword was
/// ever stated as a signature — so those files were found by their extension or not at all. Two
/// radar images named <c>.ibg</c> decoded perfectly once handed to the reader and were refused
/// before, because nothing recognised what they open with.
/// </summary>
[TestFixture]
public sealed class PdsSfduDetectionTests {

  private const string _SfduLabel = "CCSD3ZF0000100000001NJPL3IF0PDS200000001 = SFDU_LABEL\r\n";

  [Test]
  [Category("Unit")]
  public void DetectFromBytes_AnSfduLabelIsRecognisedWhateverTheFileIsNamed() {
    var data = Encoding.ASCII.GetBytes(_SfduLabel + "RECORD_TYPE = FIXED_LENGTH\r\n");

    Assert.That(FormatRegistry.DetectFromBytes(data), Is.EqualTo(ImageFormat.Pds));
  }

  /// <summary>VICAR carries the same kind of label and is settled on its own keyword; the authority
  /// code is what tells the two apart, so one without the PDS registration is not claimed.</summary>
  [Test]
  [Category("Unit")]
  public void DetectFromBytes_AnSfduLabelWithoutThePdsRegistrationIsNotClaimed() {
    var data = Encoding.ASCII.GetBytes("CCSD3ZF0000100000001NJPL3IF0MGN100000001 = SFDU_LABEL\r\nLBLSIZE=1024\r\n");

    Assert.That(FormatRegistry.DetectFromBytes(data), Is.Not.EqualTo(ImageFormat.Pds));
  }

  [Test]
  [Category("Unit")]
  public void DetectFromBytes_TheKeywordFormIsStillRecognised() {
    var data = Encoding.ASCII.GetBytes("PDS_VERSION_ID = PDS3\r\nRECORD_TYPE = FIXED_LENGTH\r\n");

    Assert.That(FormatRegistry.DetectFromBytes(data), Is.EqualTo(ImageFormat.Pds));
  }
}

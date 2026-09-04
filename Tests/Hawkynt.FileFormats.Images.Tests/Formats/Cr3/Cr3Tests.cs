using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Cr3;
using NUnit.Framework;

namespace FileFormat.Cr3.Tests;

/// <summary>Canon CR3 files, read for the pictures beside their sensor data.</summary>
/// <remarks>
/// No real CR3 was available, so the fixture was built to the layout ExifTool
/// reads and ExifTool is what judges it: given a file written here it reports the
/// type as CR3, states the codec version out of the Canon box, and extracts the
/// preview and the thumbnail byte for byte as they went in.
///
/// <para>The sensor data itself is coded with CRX, which is not implemented, and
/// a CR3 carrying nothing but sensor data is refused by name rather than
/// approximated from the little that could be guessed.</para>
/// </remarks>
[TestFixture]
public sealed class Cr3Tests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Cr3", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  private static Cr3File _Read() => Cr3Reader.FromBytes(_Fixture("canon_style.cr3"));

  [Test]
  public void TheCodecVersionAndBothPicturesComeOutOfTheBoxesTheyLiveIn() {
    var file = _Read();
    Assert.Multiple(() => {
      Assert.That(file.CodecVersion, Is.EqualTo("CanonCR3_001/00.09.00/00.00.00"));
      Assert.That(file.PreviewJpeg, Is.Not.Null);
      Assert.That(file.ThumbnailJpeg, Is.Not.Null);
      Assert.That(file.PreviewWidth, Is.EqualTo(320));
      Assert.That(file.PreviewHeight, Is.EqualTo(240));
      Assert.That(file.ThumbnailWidth, Is.EqualTo(160));
      Assert.That(file.ThumbnailHeight, Is.EqualTo(120));
    });

    // Both are ordinary JPEGs, start-of-image marker and all.
    Assert.That(file.PreviewJpeg![0], Is.EqualTo(0xFF));
    Assert.That(file.PreviewJpeg[1], Is.EqualTo(0xD8));
    Assert.That(file.ThumbnailJpeg![0], Is.EqualTo(0xFF));
    Assert.That(file.ThumbnailJpeg[1], Is.EqualTo(0xD8));
  }

  /// <summary>The picture handed back is the larger of the two, not the first one met.</summary>
  [Test]
  public void ThePictureIsTheFullSizePreview() {
    var image = Cr3File.ToRawImage(_Read());
    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(320));
      Assert.That(image.Height, Is.EqualTo(240));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  public void AFileWrittenHereReadsBackWithBothPicturesUnchanged() {
    var file = _Read();
    var again = Cr3Reader.FromBytes(Cr3Writer.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(again.CodecVersion, Is.EqualTo(file.CodecVersion));
      Assert.That(again.PreviewJpeg, Is.EqualTo(file.PreviewJpeg));
      Assert.That(again.ThumbnailJpeg, Is.EqualTo(file.ThumbnailJpeg));
      Assert.That(again.PreviewWidth, Is.EqualTo(file.PreviewWidth));
      Assert.That(again.ThumbnailHeight, Is.EqualTo(file.ThumbnailHeight));
    });
  }

  [Test]
  public void AnIsoBaseMediaFileThatIsNotCanonsIsRefused() {
    var bytes = _Fixture("canon_style.cr3");
    // Turn the brand into an MP4's.
    bytes[8] = (byte)'i';
    bytes[9] = (byte)'s';
    bytes[10] = (byte)'o';
    bytes[11] = (byte)'m';
    Assert.Throws<InvalidDataException>(() => Cr3Reader.FromBytes(bytes));
  }

  /// <summary>
  /// A CR3 whose only picture is its sensor data says so, because CRX is not decoded here.
  /// </summary>
  [Test]
  public void ACr3CarryingOnlySensorDataIsRefusedByName() {
    var file = _Read();
    var stripped = new Cr3File { CodecVersion = file.CodecVersion, ThumbnailJpeg = file.ThumbnailJpeg, ThumbnailWidth = 160, ThumbnailHeight = 120 };
    var bytes = Cr3Writer.ToBytes(stripped);

    // Blank the thumbnail's four-character code so nothing is left to find.
    var at = System.Text.Encoding.ASCII.GetString(bytes).IndexOf("THMB", StringComparison.Ordinal);
    Assert.That(at, Is.GreaterThan(0));
    bytes[at] = (byte)'X';

    var failure = Assert.Throws<NotSupportedException>(() => Cr3Reader.FromBytes(bytes));
    Assert.That(failure!.Message, Does.Contain("CRX"));
  }
}

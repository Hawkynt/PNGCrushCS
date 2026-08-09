using System;
using System.IO;
using FileFormat.Core;
using FileFormat.TiPicture;

namespace FileFormat.TiPicture.Tests;

[TestFixture]
public sealed class TiPictureTests {

  /// <summary>A transfer file holding one picture entry of the screen the signature implies.</summary>
  private static byte[] _File(string signature, byte type, int width, byte[]? bitmap = null) {
    var rowBytes = (width + 7) / 8;
    var pixels = bitmap ?? new byte[rowBytes * TiPictureFile.ScreenHeight];

    // entry header: the data length, the type byte and an eight byte name.
    var entryHeader = new byte[11];
    entryHeader[0] = (byte)(pixels.Length + 2);
    entryHeader[1] = (byte)((pixels.Length + 2) >> 8);
    entryHeader[2] = type;

    using var ms = new MemoryStream();
    var writer = new BinaryWriter(ms);
    writer.Write(System.Text.Encoding.ASCII.GetBytes(signature));
    writer.Write((byte)0x1A);
    writer.Write((byte)0x0A);
    writer.Write((byte)0x00);
    writer.Write(new byte[TiPictureFile.CommentSize]);

    // The data section is the entry header length, the header, the repeated length and the data.
    var dataSection = 2 + entryHeader.Length + 2 + pixels.Length + 2;
    writer.Write((ushort)dataSection);
    writer.Write((ushort)entryHeader.Length);
    writer.Write(entryHeader);
    writer.Write((ushort)(pixels.Length + 2));
    writer.Write((ushort)pixels.Length);
    writer.Write(pixels);
    writer.Write((ushort)0);

    return ms.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TiPictureReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => TiPictureReader.FromBytes(new byte[128]));

  /// <summary>The TI-92 is a different container and its picture data is compressed.</summary>
  /// <remarks>
  /// This used to be asserted of the TI-73, which was wrong: that one is the TI-82's container and
  /// the TI-82's screen under another signature, and XnView's converter reads a picture built under
  /// both signatures identically. The TI-92 is the one that really is something else.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void FromBytes_ACalculatorThisDoesNotRead_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => TiPictureReader.FromBytes(_File("**TI92**", TiPictureFile.PictureType8283, TiPictureFile.Width8283)));

  [Test]
  [Category("Unit")]
  public void FromBytes_StatedLengthMustAccountForTheFile() {
    var data = _File("**TI83**", TiPictureFile.PictureType8283, TiPictureFile.Width8283);
    Array.Resize(ref data, data.Length + 1);

    Assert.Throws<InvalidDataException>(() => TiPictureReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AnEntryOfAnotherKind_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => TiPictureReader.FromBytes(_File("**TI83**", 0x01, TiPictureFile.Width8283)));

  [Test]
  [Category("Unit")]
  public void FromBytes_TheEightyTwoAndEightyThreeScreenIsNinetySixBySixtyThree(
    [Values("**TI73**", "**TI82**", "**TI83**")] string signature) {
    var decoded = TiPictureFile.ToRawImage(TiPictureReader.FromBytes(_File(signature, TiPictureFile.PictureType8283, TiPictureFile.Width8283)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(96));
      Assert.That(decoded.Height, Is.EqualTo(63));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed1));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheEightyFiveAndEightySixScreenIsAHundredAndTwentyEightBySixtyThree(
    [Values("**TI85**", "**TI86**")] string signature) {
    var decoded = TiPictureFile.ToRawImage(TiPictureReader.FromBytes(_File(signature, TiPictureFile.PictureType8586, TiPictureFile.Width8586)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(128));
      Assert.That(decoded.Height, Is.EqualTo(63));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_APictureOfTheWrongSizeForItsCalculator_ThrowsInvalidDataException() {
    // A TI-83 entry carrying the wider TI-85 screen is not a picture this calculator could hold, and
    // taking it anyway would draw it at the wrong width.
    var data = _File("**TI83**", TiPictureFile.PictureType8283, TiPictureFile.Width8586);

    Assert.Throws<InvalidDataException>(() => TiPictureReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ThePixelsComeBackAsTheyWereStored() {
    var bitmap = new byte[12 * TiPictureFile.ScreenHeight];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i * 7);

    var file = TiPictureReader.FromBytes(_File("**TI82**", TiPictureFile.PictureType8283, TiPictureFile.Width8283, bitmap));

    Assert.That(file.PixelData, Is.EqualTo(bitmap));
  }
}

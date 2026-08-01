using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Trs80.Tests;

/// <summary>The hi-res board's screen: 640 by 240 stored, 640 by 480 shown.</summary>
[TestFixture]
public sealed class Trs80Tests {

  private static RawImage _Picture() {
    var pixels = new byte[640 * 480 * 3];
    for (var y = 0; y < 480; ++y)
    for (var x = 0; x < 640; ++x) {
      var lit = ((x / 16) + (y / 16)) % 2 == 0;
      var at = (y * 640 + x) * 3;
      pixels[at] = pixels[at + 1] = pixels[at + 2] = (byte)(lit ? 255 : 0);
    }

    return new() { Width = 640, Height = 480, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_IsTheBoardsNineteenThousandTwoHundredBytes() {
    var bytes = Trs80Writer.ToBytes(Trs80File.FromRawImage(_Picture()));
    Assert.That(bytes, Has.Length.EqualTo(19200));
  }

  [TestCase(19200)]
  [TestCase(19328)]
  [TestCase(19456)]
  [Category("Unit")]
  public void Read_TakesTheThreeLengthsASavedScreenComesIn(int length)
    => Assert.That(Trs80Reader.FromBytes(new byte[length]).RawData, Has.Length.EqualTo(19200));

  [TestCase(6144)]
  [TestCase(19199)]
  [TestCase(19201)]
  [Category("Unit")]
  public void Read_RefusesAnythingElse(int length)
    => Assert.Throws<InvalidDataException>(() => Trs80Reader.FromBytes(new byte[length]));

  [Test]
  [Category("Unit")]
  public void Decoded_DrawsEachStoredRowTwice() {
    var raw = new byte[19200];
    // Top-left pixel of the first stored row only.
    raw[0] = 0x80;

    var image = Trs80File.ToRawImage(Trs80Reader.FromBytes(raw));
    Assert.Multiple(() => {
      Assert.That(image.Height, Is.EqualTo(480));
      Assert.That(image.PixelData[0], Is.EqualTo(1), "row zero");
      Assert.That(image.PixelData[640], Is.EqualTo(1), "row one, which is row zero again");
      Assert.That(image.PixelData[1280], Is.EqualTo(0), "row two is the next stored row");
    });
  }

  [Test]
  [Category("Unit")]
  public void Decoded_TreatsASetBitAsALitPixel() {
    var raw = new byte[19200];
    raw[0] = 0x80;

    var image = Trs80File.ToRawImage(Trs80Reader.FromBytes(raw));
    Assert.That(image.Palette![image.PixelData[0] * 3], Is.EqualTo(255));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheBitmap() {
    var original = Trs80File.FromRawImage(_Picture());
    Assert.That(Trs80Reader.FromBytes(Trs80Writer.ToBytes(original)).RawData, Is.EqualTo(original.RawData));
  }
}

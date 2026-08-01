using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariFalconXga.Tests;

/// <summary>
/// The XGA container, which is no container at all: two lengths of bare samples and nothing else.
/// </summary>
/// <remarks>
/// The tests this replaces asserted a four-byte width-and-height header that this project made up.
/// A file written that way is a picture no Falcon program can open, and one written by a Falcon
/// program was read here as a picture four bytes narrower than it is.
/// </remarks>
[TestFixture]
public sealed class AtariFalconXgaTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = (byte)(i % 251);
      pixels[i + 1] = (byte)(i % 241);
      pixels[i + 2] = (byte)(i % 239);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [TestCase(320, 240, 153600)]
  [TestCase(384, 480, 368640)]
  [Category("Unit")]
  public void Written_IsNothingButSamples(int width, int height, int expected) {
    var bytes = AtariFalconXgaWriter.ToBytes(AtariFalconXgaFile.FromRawImage(_Picture(width, height)));
    Assert.That(bytes, Has.Length.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void Written_ResamplesToWhicheverOfTheTwoScreensFits() {
    var bytes = AtariFalconXgaWriter.ToBytes(AtariFalconXgaFile.FromRawImage(_Picture(640, 400)));
    Assert.That(bytes, Has.Length.EqualTo(153600), "a landscape picture belongs on the small screen");

    bytes = AtariFalconXgaWriter.ToBytes(AtariFalconXgaFile.FromRawImage(_Picture(200, 600)));
    Assert.That(bytes, Has.Length.EqualTo(368640), "a portrait one belongs on the tall screen");
  }

  [TestCase(153598)]
  [TestCase(153604)]
  [TestCase(0)]
  [Category("Unit")]
  public void Read_RefusesALengthThatNamesNoScreen(int length)
    => Assert.Throws<InvalidDataException>(() => AtariFalconXgaReader.FromBytes(new byte[length]));

  [TestCase(320, 240)]
  [TestCase(384, 480)]
  [Category("Integration")]
  public void RoundTrip_KeepsEverySampleTheFiveSixFiveRangeHolds(int width, int height) {
    var file = AtariFalconXgaFile.FromRawImage(_Picture(width, height));
    var restored = AtariFalconXgaReader.FromBytes(AtariFalconXgaWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void Read_PutsTheFirstSampleInTheFirstPixel() {
    var bytes = new byte[153600];
    // Big-endian 5-6-5: all of red, none of the rest.
    bytes[0] = 0xF8;
    bytes[1] = 0x00;

    var image = AtariFalconXgaFile.ToRawImage(AtariFalconXgaReader.FromBytes(bytes));
    Assert.That((image.PixelData[0], image.PixelData[1], image.PixelData[2]), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
  }
}

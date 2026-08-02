using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.AwardBmp.Tests;

/// <summary>
/// The Award BIOS bitmap logo (AWBM), the second and unrelated thing a <c>.epa</c> file can be.
/// </summary>
/// <remarks>
/// The older EPA is a screenful of text-mode character cells; this one is a real bitmap of sixteen
/// colours in four bitplanes, with the palette at the end behind an <c>RGB </c> marker. Only the
/// first was implemented, so half of what the extension names could not be opened.
/// <para/>
/// The layout was read off a real file from a public archive of format samples. XnView names the
/// format and agrees on the size, but decodes the picture itself to noise — so what settled the
/// plane order was the picture coming out as a legible logo rather than any tool's agreement.
/// </remarks>
[TestFixture]
public sealed class AwardBmpTests {

  private static byte[] _Palette() {
    var palette = new byte[16 * 3];
    for (var i = 0; i < 16; ++i) {
      palette[i * 3] = (byte)(i * 17);
      palette[i * 3 + 1] = (byte)(255 - i * 17);
      palette[i * 3 + 2] = (byte)(i % 4 * 85);
    }

    return palette;
  }

  private static AwardBmpFile _Picture(int width, int height) {
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)((x / 3 + y / 2) % 16);

    return new() { Width = width, Height = height, PixelData = pixels, Palette = _Palette() };
  }

  [Test]
  [Category("Unit")]
  public void Written_HasTheSignatureAndTheSizeInTheHeader() {
    var bytes = AwardBmpWriter.ToBytes(_Picture(101, 125));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("AWBM"));
      Assert.That(bytes[4] | (bytes[5] << 8), Is.EqualTo(101));
      Assert.That(bytes[6] | (bytes[7] << 8), Is.EqualTo(125));
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_PutsThePaletteBehindItsMarkerAtTheEnd() {
    var bytes = AwardBmpWriter.ToBytes(_Picture(101, 125));

    // Four bitplanes at thirteen bytes a row, which is what leaves room for the marker.
    var at = 8 + 13 * 4 * 125;

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, at, 4), Is.EqualTo("RGB "));
      Assert.That(bytes, Has.Length.EqualTo(at + 4 + 48));
      for (var i = 0; i < 48; ++i)
        Assert.That(bytes[at + 4 + i], Is.LessThanOrEqualTo(63), $"channel {i} must fit in six bits");
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileThatDoesNotBeginWithTheSignature()
    => Assert.Throws<InvalidDataException>(() => AwardBmpReader.FromBytes(new byte[512]));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileTooShortForTheSizeItStates() {
    var data = new byte[64];
    Encoding.ASCII.GetBytes("AWBM").CopyTo(data, 0);
    data[4] = 200;
    data[6] = 200;

    Assert.Throws<InvalidDataException>(() => AwardBmpReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileWhosePaletteMarkerIsMissing() {
    var bytes = AwardBmpWriter.ToBytes(_Picture(16, 4));
    bytes[8 + 2 * 4 * 4] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => AwardBmpReader.FromBytes(bytes));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsEveryIndex() {
    var original = _Picture(101, 125);
    var restored = AwardBmpReader.FromBytes(AwardBmpWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(101));
      Assert.That(restored.Height, Is.EqualTo(125));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsThePaletteToTheSixBitsItStores() {
    var original = _Picture(32, 8);
    var restored = AwardBmpReader.FromBytes(AwardBmpWriter.ToBytes(original));

    Assert.Multiple(() => {
      for (var i = 0; i < 48; ++i)
        Assert.That(restored.Palette[i] >> 2, Is.EqualTo(original.Palette[i] >> 2), $"channel {i}");
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_TakesAPictureOfAnyColoursDownToSixteen() {
    var pixels = new byte[24 * 8 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    var file = AwardBmpFile.FromRawImage(
      new() { Width = 24, Height = 8, Format = PixelFormat.Rgb24, PixelData = pixels });

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(24));
      foreach (var index in file.PixelData)
        Assert.That(index, Is.LessThan(16));
    });
  }
}

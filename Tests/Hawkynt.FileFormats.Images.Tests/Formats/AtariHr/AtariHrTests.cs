using System;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.AtariHr.Tests;

/// <summary>Two interlaced Graphics 8 fields, which between them carry three levels.</summary>
[TestFixture]
public sealed class AtariHrTests {

  private static RawImage _Ramp() {
    var pixels = new byte[256 * 239 * 3];
    for (var y = 0; y < 239; ++y)
    for (var x = 0; x < 256; ++x) {
      var at = (y * 256 + x) * 3;
      pixels[at] = pixels[at + 1] = pixels[at + 2] = (byte)x;
    }

    return new() { Width = 256, Height = 239, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_IsTwoFieldsOfEightThousandOneHundredAndNinetyTwo() {
    var bytes = AtariHrWriter.ToBytes(AtariHrFile.FromRawImage(_Ramp()));
    Assert.That(bytes, Has.Length.EqualTo(16384));
  }

  [TestCase(7680)]
  [TestCase(8192)]
  [TestCase(16383)]
  [Category("Unit")]
  public void Read_RefusesAnythingButTheFullPair(int length)
    => Assert.Throws<InvalidDataException>(() => AtariHrReader.FromBytes(new byte[length]));

  [Test]
  [Category("Unit")]
  public void Decoded_HasThreeLevelsAndNotTwo() {
    var raw = new byte[16384];
    // First pixel set in both fields, second in one, third in neither.
    raw[0] = 0b1100_0000;
    raw[8192] = 0b1000_0000;

    var image = AtariHrFile.ToRawImage(AtariHrReader.FromBytes(raw));
    var levels = new[] { image.PixelData[0], image.PixelData[3], image.PixelData[6] };

    Assert.Multiple(() => {
      Assert.That(levels[0], Is.GreaterThan(levels[1]), "both fields is the brightest");
      Assert.That(levels[1], Is.GreaterThan(levels[2]), "one field is the grey between");
      Assert.That(levels[2], Is.EqualTo(0), "neither field is black");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsBothFields() {
    var original = AtariHrFile.FromRawImage(_Ramp());
    Assert.That(AtariHrReader.FromBytes(AtariHrWriter.ToBytes(original)).RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void Written_UsesAllThreeLevelsForAPictureThatNeedsThem() {
    var file = AtariHrFile.FromRawImage(_Ramp());
    var image = AtariHrFile.ToRawImage(file);

    var distinct = Enumerable.Range(0, 256).Select(x => image.PixelData[x * 3]).Distinct().ToArray();
    Assert.That(distinct, Has.Length.EqualTo(3), "a ramp across the screen should reach every level");
  }
}

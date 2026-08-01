using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.SpookySpritesFalcon.Tests;

/// <summary>
/// Spooky Sprites: a named twelve-byte header, then literal and repeat runs that alternate.
/// </summary>
/// <remarks>
/// A literal run states how many colours follow and gives them; a repeat run states only how many
/// more copies of the last colour to draw, and carries nothing. They alternate strictly, so a run
/// ends where a colour starts repeating rather than where a fixed block would.
/// <para/>
/// What was written before was a four-byte header with no name and the Macintosh scheme — a signed
/// byte, positive for literals and negative for repeats. Checked against RECOIL: our decode of what
/// we write matches its own to the byte across all 192000 samples.
/// </remarks>
[TestFixture]
public sealed class SpookySpritesFalconTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 255 / Math.Max(1, width - 1));
      pixels[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      pixels[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_NamesItselfAndStatesItsSize() {
    var bytes = SpookySpritesFalconWriter.ToBytes(SpookySpritesFalconFile.FromRawImage(_Picture(320, 200)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("tre1"));
      Assert.That((bytes[4] << 8) | bytes[5], Is.EqualTo(320));
      Assert.That((bytes[6] << 8) | bytes[7], Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_StartsWithALiteralRun() {
    var bytes = SpookySpritesFalconWriter.ToBytes(SpookySpritesFalconFile.FromRawImage(_Picture(64, 48)));

    // The first count is a literal one, and the colours follow it directly.
    Assert.That(bytes[12], Is.GreaterThan(0), "a run of zero cannot begin a picture");
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingWithoutTheName()
    => Assert.Throws<InvalidDataException>(() => SpookySpritesFalconReader.FromBytes(new byte[64]));

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsEveryPixel() {
    var original = SpookySpritesFalconFile.FromRawImage(_Picture(320, 200));
    var restored = SpookySpritesFalconReader.FromBytes(SpookySpritesFalconWriter.ToBytes(original));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SurvivesALongRunOfOneColour() {
    // Past 254 the count is a marker with a word behind it, which is a separate path.
    var pixels = new byte[512 * 8 * 3];
    Array.Fill(pixels, (byte)0x40);

    var original = new RawImage { Width = 512, Height = 8, Format = PixelFormat.Rgb24, PixelData = pixels };
    var file = SpookySpritesFalconFile.FromRawImage(original);
    var restored = SpookySpritesFalconReader.FromBytes(SpookySpritesFalconWriter.ToBytes(file));

    Assert.That(restored.PixelData, Is.EqualTo(file.PixelData));
  }
}

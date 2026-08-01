using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.UyvyRaw.Tests;

/// <summary>
/// UYVY: two pixels to four bytes, a shared chroma pair between two lumas.
/// </summary>
/// <remarks>
/// The byte order and the colour conversion here were both taken from files another tool wrote and
/// checked against its own decode of them, rather than from a description — a 4:2:2 stream has four
/// plausible orderings and two plausible ranges, and nothing in the file says which.
/// </remarks>
[TestFixture]
public sealed class UyvyRawTests {

  [Test]
  [Category("Unit")]
  public void Decoded_ReadsTheOrderTheNameSpellsOut() {
    // U, then the first luma, then V, then the second: red, twice.
    var data = new byte[] { 84, 76, 255, 76 };
    var file = new UyvyRawFile { Width = 2, Height = 1, PixelData = data };
    var image = UyvyRawFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.PixelData[0], Is.EqualTo(255).Within(2), "red");
      Assert.That(image.PixelData[1], Is.EqualTo(0).Within(2));
      Assert.That(image.PixelData[2], Is.EqualTo(0).Within(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void Encoded_ProducesTheBytesAnotherToolProducesForTheSameColour() {
    var pixels = new byte[] { 255, 0, 0, 255, 0, 0 };
    var image = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Rgb24, PixelData = pixels };
    var file = UyvyRawFile.FromRawImage(image);

    Assert.Multiple(() => {
      Assert.That(file.PixelData[0], Is.EqualTo(84).Within(1), "the blue difference");
      Assert.That(file.PixelData[1], Is.EqualTo(76).Within(1), "luma");
      Assert.That(file.PixelData[2], Is.EqualTo(255).Within(1), "the red difference");
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesALengthThatIsNoFrame()
    => Assert.Throws<InvalidDataException>(() => UyvyRawReader.FromBytes(new byte[1234]));

  [TestCase(720, 576)]
  [TestCase(352, 288)]
  [TestCase(64, 64)]
  [Category("Unit")]
  public void Read_PlacesAStreamByItsLength(int width, int height) {
    var file = UyvyRawReader.FromBytes(new byte[width * height * 2]);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsAPictureToWithinWhatHalvingTheChromaCosts() {
    var pixels = new byte[64 * 64 * 3];
    for (var y = 0; y < 64; ++y)
    for (var x = 0; x < 64; ++x) {
      var at = (y * 64 + x) * 3;
      // Bands rather than a per-pixel ramp: chroma is shared between neighbours, so a picture that
      // changes colour every pixel cannot survive, and asking it to would not be a fair test.
      pixels[at] = (byte)(x / 2 * 8);
      pixels[at + 1] = (byte)(y / 2 * 4);
      pixels[at + 2] = 64;
    }

    var original = new RawImage { Width = 64, Height = 64, Format = PixelFormat.Rgb24, PixelData = pixels };
    var restored = UyvyRawFile.ToRawImage(
      UyvyRawReader.FromBytes(UyvyRawWriter.ToBytes(UyvyRawFile.FromRawImage(original))));

    for (var i = 0; i < pixels.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixels[i]).Within(6), $"sample {i}");
  }
}

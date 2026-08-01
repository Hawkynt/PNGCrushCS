using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.NokiaNlm.Tests;

/// <summary>
/// A Nokia Logo Manager file: ten bytes of header, then the bitmap a bit a pixel.
/// </summary>
/// <remarks>
/// The size is stated in single bytes, so a side cannot exceed 255 — the phones this was written
/// for had rather less. What was written here before was a bare bitmap with no header at all,
/// locked to 84 by 48; the header is what tells the size, and the size is whatever it says.
/// </remarks>
[TestFixture]
public sealed class NokiaNlmTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var ink = (x / 3 + y / 3) % 2 == 0;
      var at = (y * width + x) * 3;
      pixels[at] = pixels[at + 1] = pixels[at + 2] = (byte)(ink ? 0 : 255);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_HasTheHeaderTheFormatStates() {
    var bytes = NokiaNlmWriter.ToBytes(NokiaNlmFile.FromRawImage(_Picture(84, 48)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("NLM "));
      Assert.That(bytes[7], Is.EqualTo(84), "width sits in a single byte");
      Assert.That(bytes[8], Is.EqualTo(48));
      Assert.That(bytes, Has.Length.EqualTo(10 + (84 + 7) / 8 * 48));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesAnySizeTheHeaderStates() {
    var stride = (100 + 7) / 8;
    var data = new byte[10 + stride * 40];
    Encoding.ASCII.GetBytes("NLM ").CopyTo(data, 0);
    data[7] = 100;
    data[8] = 40;

    var file = NokiaNlmReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(100), "the size is not fixed");
      Assert.That(file.Height, Is.EqualTo(40));
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_BringsAnOversizePictureWithinWhatAByteCanState() {
    var bytes = NokiaNlmWriter.ToBytes(NokiaNlmFile.FromRawImage(_Picture(400, 300)));

    Assert.Multiple(() => {
      Assert.That(bytes[7], Is.EqualTo(255));
      Assert.That(bytes[8], Is.EqualTo(255));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingElse()
    => Assert.Throws<InvalidDataException>(() => NokiaNlmReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileShorterThanItsHeaderClaims() {
    var data = new byte[10 + 4];
    Encoding.ASCII.GetBytes("NLM ").CopyTo(data, 0);
    data[7] = 200;
    data[8] = 200;

    Assert.Throws<InvalidDataException>(() => NokiaNlmReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsEveryPixel() {
    var original = NokiaNlmFile.FromRawImage(_Picture(84, 48));
    var restored = NokiaNlmReader.FromBytes(NokiaNlmWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(84));
      Assert.That(restored.Height, Is.EqualTo(48));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}

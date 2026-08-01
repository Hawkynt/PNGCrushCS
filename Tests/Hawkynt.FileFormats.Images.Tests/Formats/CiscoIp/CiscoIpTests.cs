using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.CiscoIp.Tests;

/// <summary>
/// A Cisco IP Phone image, which is a small XML document rather than a binary file.
/// </summary>
/// <remarks>
/// The phone fetches one over HTTP and reads the picture out of it as hexadecimal text at two bits
/// a pixel, those two bits being the four shades its screen has. What was written here before was
/// eighty bytes of binary header and 24-bit pixels — a shape nothing would open.
/// <para/>
/// The layout came from a document a conversion service produced on request, and was checked by
/// sending our own back to it.
/// </remarks>
[TestFixture]
public sealed class CiscoIpTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var level = (byte)(x * 3 / Math.Max(1, width - 1) * 85);
      var at = (y * width + x) * 3;
      pixels[at] = pixels[at + 1] = pixels[at + 2] = level;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_IsADocumentAndNotABinaryFile() {
    var text = Encoding.ASCII.GetString(CiscoIpWriter.ToBytes(CiscoIpFile.FromRawImage(_Picture(64, 48))));

    Assert.Multiple(() => {
      Assert.That(text, Does.StartWith("<CiscoIPPhoneImage>"));
      Assert.That(text, Does.Contain("<Width>64</Width>"));
      Assert.That(text, Does.Contain("<Height>48</Height>"));
      Assert.That(text, Does.Contain("<Depth>2</Depth>"), "two bits give the four shades the screen has");
      Assert.That(text, Does.Contain("<Data>"));
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_SpendsExactlyTwoBitsAPixel() {
    var text = Encoding.ASCII.GetString(CiscoIpWriter.ToBytes(CiscoIpFile.FromRawImage(_Picture(64, 48))));
    var data = text[(text.IndexOf("<Data>", StringComparison.Ordinal) + 6)..text.IndexOf("</Data>", StringComparison.Ordinal)];

    // Sixteen bytes a row for sixty-four pixels, and two hexadecimal characters a byte.
    Assert.That(data.Trim(), Has.Length.EqualTo(16 * 48 * 2));
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesItsSizeFromTheDocument() {
    var document = "<CiscoIPPhoneImage><Title>t</Title><LocationX>3</LocationX><LocationY>4</LocationY>"
      + "<Width>4</Width><Height>2</Height><Depth>2</Depth><Data>" + new string('0', 2 * 2 * 2) + "</Data></CiscoIPPhoneImage>";

    var file = CiscoIpReader.FromBytes(Encoding.ASCII.GetBytes(document));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.Title, Is.EqualTo("t"));
      Assert.That(file.LocationX, Is.EqualTo(3));
      Assert.That(file.LocationY, Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingThatIsNotOne()
    => Assert.Throws<InvalidDataException>(() => CiscoIpReader.FromBytes(Encoding.ASCII.GetBytes("<html/>")));

  [Test]
  [Category("Unit")]
  public void Read_RefusesADocumentWhoseDataIsShortOfItsSize() {
    var document = "<CiscoIPPhoneImage><Width>64</Width><Height>48</Height><Depth>2</Depth>"
      + "<Data>0000</Data></CiscoIPPhoneImage>";

    Assert.Throws<InvalidDataException>(() => CiscoIpReader.FromBytes(Encoding.ASCII.GetBytes(document)));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsThePicture() {
    var original = CiscoIpFile.FromRawImage(_Picture(64, 48));
    var restored = CiscoIpReader.FromBytes(CiscoIpWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(64));
      Assert.That(restored.Height, Is.EqualTo(48));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}

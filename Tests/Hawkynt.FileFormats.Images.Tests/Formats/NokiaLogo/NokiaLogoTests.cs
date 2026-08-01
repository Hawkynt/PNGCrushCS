using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.NokiaLogo.Tests;

/// <summary>
/// A Nokia operator logo: twenty bytes of header, then a character a pixel.
/// </summary>
/// <remarks>
/// The body is text — the letter zero or the letter one — not bits. It is an extravagant way to
/// store a two-colour picture and it is what the format does.
/// <para/>
/// The layout here came from a file a conversion service produced on request, and was checked by
/// sending our own output back to it. What was here before had no header at all and was locked to
/// 72 by 14; neither was real.
/// </remarks>
[TestFixture]
public sealed class NokiaLogoTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var ink = (x / 4 + y / 4) % 2 == 0;
      var at = (y * width + x) * 3;
      pixels[at] = pixels[at + 1] = pixels[at + 2] = (byte)(ink ? 0 : 255);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_HasTheHeaderTheFormatStates() {
    var bytes = NokiaLogoWriter.ToBytes(NokiaLogoFile.FromRawImage(_Picture(72, 14)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 3), Is.EqualTo("NOL"));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10)), Is.EqualTo(72));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12)), Is.EqualTo(14));
      Assert.That(bytes, Has.Length.EqualTo(20 + 72 * 14));
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_SpendsACharacterAPixelRatherThanABit() {
    var bytes = NokiaLogoWriter.ToBytes(NokiaLogoFile.FromRawImage(_Picture(8, 2)));

    for (var i = 20; i < bytes.Length; ++i)
      Assert.That(bytes[i], Is.AnyOf((byte)'0', (byte)'1'), $"byte {i}");
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesAnySizeTheHeaderStates() {
    var data = new byte[20 + 640 * 480];
    Encoding.ASCII.GetBytes("NOL").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 640);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 480);
    Array.Fill(data, (byte)'0', 20, 640 * 480);

    var file = NokiaLogoReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(640), "the size is not fixed at the operator-logo one");
      Assert.That(file.Height, Is.EqualTo(480));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingElse()
    => Assert.Throws<InvalidDataException>(() => NokiaLogoReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileShorterThanItsHeaderClaims() {
    var data = new byte[20 + 100];
    Encoding.ASCII.GetBytes("NOL").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 640);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 480);

    Assert.Throws<InvalidDataException>(() => NokiaLogoReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsEveryPixel() {
    var original = NokiaLogoFile.FromRawImage(_Picture(72, 14));
    var restored = NokiaLogoReader.FromBytes(NokiaLogoWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(72));
      Assert.That(restored.Height, Is.EqualTo(14));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}

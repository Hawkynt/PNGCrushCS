using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.LViewPro;

namespace FileFormat.LViewPro.Tests;

[TestFixture]
public sealed class LViewProTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i)
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = (byte)(i * 3);

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => LViewProReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => LViewProReader.FromBytes(new byte[128]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongTitle_ThrowsInvalidDataException() {
    var data = new byte[128];
    LViewProFile.Magic.CopyTo(data);
    Encoding.ASCII.GetBytes("Some other program!!").CopyTo(data, LViewProFile.TitleAt);

    Assert.Throws<InvalidDataException>(() => LViewProReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesTheMagicTheTitleAndTheSize() {
    var bytes = LViewProWriter.ToBytes(LViewProFile.FromRawImage(_Picture(32, 16)));

    Assert.Multiple(() => {
      Assert.That(bytes[..2], Is.EqualTo(LViewProFile.Magic.ToArray()));
      Assert.That(Encoding.ASCII.GetString(bytes, LViewProFile.TitleAt, LViewProFile.Title.Length), Is.EqualTo(LViewProFile.Title));
      Assert.That(BitConverter.ToInt32(bytes, LViewProFile.WidthAt), Is.EqualTo(32));
      Assert.That(BitConverter.ToInt32(bytes, LViewProFile.HeightAt), Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SizeDisagreesWithTheJpeg_ThrowsInvalidDataException() {
    var data = LViewProWriter.ToBytes(LViewProFile.FromRawImage(_Picture(32, 16)));
    data[LViewProFile.WidthAt] = 99;

    Assert.Throws<InvalidDataException>(() => LViewProReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TheSizeSurvivesAndThePictureDecodes() {
    var restored = LViewProReader.FromBytes(LViewProWriter.ToBytes(LViewProFile.FromRawImage(_Picture(32, 16))));
    var decoded = LViewProFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(32));
      Assert.That(restored.Height, Is.EqualTo(16));
      Assert.That(decoded.Width, Is.EqualTo(32));
      Assert.That(decoded.Height, Is.EqualTo(16));
    });
  }
}

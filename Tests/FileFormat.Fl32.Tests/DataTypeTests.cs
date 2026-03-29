using System;
using FileFormat.Fl32;

namespace FileFormat.Fl32.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Magic_EqualsFL32AsUint32() {
    Assert.That(Fl32File.Magic, Is.EqualTo(0x32334C46U));
    var bytes = BitConverter.GetBytes(Fl32File.Magic);
    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo((byte)'F'));
      Assert.That(bytes[1], Is.EqualTo((byte)'L'));
      Assert.That(bytes[2], Is.EqualTo((byte)'3'));
      Assert.That(bytes[3], Is.EqualTo((byte)'2'));
    });
  }

  [Test]
  [Category("Unit")]
  public void HeaderSize_Is16()
    => Assert.That(Fl32File.HeaderSize, Is.EqualTo(16));

  [Test]
  [Category("Unit")]
  public void Fl32File_Defaults() {
    var file = new Fl32File();
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(0));
      Assert.That(file.Height, Is.EqualTo(0));
      Assert.That(file.Channels, Is.EqualTo(0));
      Assert.That(file.PixelData, Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void Fl32File_InitProperties() {
    var file = new Fl32File {
      Width = 10, Height = 20, Channels = 3,
      PixelData = new float[600]
    };

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(10));
      Assert.That(file.Height, Is.EqualTo(20));
      Assert.That(file.Channels, Is.EqualTo(3));
      Assert.That(file.PixelData, Has.Length.EqualTo(600));
    });
  }

  [Test]
  [Category("Unit")]
  public void Fl32File_FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => Fl32File.FromRawImage(null!));
}

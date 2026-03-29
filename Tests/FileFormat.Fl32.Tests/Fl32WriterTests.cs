using System;
using System.Buffers.Binary;
using FileFormat.Fl32;

namespace FileFormat.Fl32.Tests;

[TestFixture]
public sealed class Fl32WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Fl32Writer.ToBytes(null!));

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesMagic() {
    var data = _WriteGray(2, 2);
    var magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
    Assert.That(magic, Is.EqualTo(Fl32File.Magic));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesMagicAsFL32String() {
    var data = _WriteGray(1, 1);
    Assert.Multiple(() => {
      Assert.That(data[0], Is.EqualTo((byte)'F'));
      Assert.That(data[1], Is.EqualTo((byte)'L'));
      Assert.That(data[2], Is.EqualTo((byte)'3'));
      Assert.That(data[3], Is.EqualTo((byte)'2'));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesHeightBeforeWidth() {
    var data = _WriteGray(3, 5);
    var height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
    var width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
    Assert.Multiple(() => {
      Assert.That(height, Is.EqualTo(5));
      Assert.That(width, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesChannels() {
    var file = new Fl32File { Width = 2, Height = 2, Channels = 3, PixelData = new float[12] };
    var data = Fl32Writer.ToBytes(file);
    var channels = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
    Assert.That(channels, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gray_TotalSizeCorrect() {
    var data = _WriteGray(4, 3);
    Assert.That(data.Length, Is.EqualTo(Fl32File.HeaderSize + 4 * 3 * 1 * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb_TotalSizeCorrect() {
    var file = new Fl32File { Width = 4, Height = 3, Channels = 3, PixelData = new float[36] };
    var data = Fl32Writer.ToBytes(file);
    Assert.That(data.Length, Is.EqualTo(Fl32File.HeaderSize + 4 * 3 * 3 * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsPixelData() {
    var pixels = new float[] { 1.0f, 0.5f, 0.25f, 0.75f };
    var file = new Fl32File { Width = 2, Height = 2, Channels = 1, PixelData = pixels };
    var data = Fl32Writer.ToBytes(file);

    for (var i = 0; i < pixels.Length; ++i) {
      var actual = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(Fl32File.HeaderSize + i * 4));
      Assert.That(actual, Is.EqualTo(pixels[i]));
    }
  }

  private static byte[] _WriteGray(int w, int h)
    => Fl32Writer.ToBytes(new Fl32File { Width = w, Height = h, Channels = 1, PixelData = new float[w * h] });
}

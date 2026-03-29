using System;
using System.Buffers.Binary;
using FileFormat.SinbadSlideshow;

namespace FileFormat.SinbadSlideshow.Tests;

[TestFixture]
public sealed class SinbadSlideshowWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SinbadSlideshowWriter.ToBytes(null!));

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly32032Bytes() {
    var file = _CreateMinimalFile();
    var bytes = SinbadSlideshowWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(32032));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesPaletteAsBigEndian() {
    var palette = new short[16];
    palette[0] = 0x0777;
    palette[1] = 0x0700;
    palette[15] = 0x0007;

    var file = new SinbadSlideshowFile {
      Palette = palette,
      PixelData = new byte[32000],
    };

    var bytes = SinbadSlideshowWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(0)), Is.EqualTo((short)0x0777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2)), Is.EqualTo((short)0x0700));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(30)), Is.EqualTo((short)0x0007));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAtOffset32() {
    var pixelData = new byte[32000];
    pixelData[0] = 0xAA;
    pixelData[1] = 0xBB;
    pixelData[31999] = 0xCC;

    var file = new SinbadSlideshowFile {
      Palette = new short[16],
      PixelData = pixelData,
    };

    var bytes = SinbadSlideshowWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[32], Is.EqualTo(0xAA));
      Assert.That(bytes[33], Is.EqualTo(0xBB));
      Assert.That(bytes[32031], Is.EqualTo(0xCC));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyPixelData_ProducesZerosAtOffset32() {
    var file = new SinbadSlideshowFile {
      Palette = new short[16],
      PixelData = [],
    };
    var bytes = SinbadSlideshowWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(32032));
      Assert.That(bytes[32], Is.EqualTo(0));
      Assert.That(bytes[32031], Is.EqualTo(0));
    });
  }

  private static SinbadSlideshowFile _CreateMinimalFile() => new() {
    Palette = new short[16],
    PixelData = new byte[32000],
  };
}

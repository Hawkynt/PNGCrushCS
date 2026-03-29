using System;
using FileFormat.Flif;

namespace FileFormat.Flif.Tests;

[TestFixture]
public sealed class FlifWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FlifWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb_StartsWithFlifSignature() {
    var file = new FlifFile {
      Width = 2,
      Height = 2,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = FlifWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'F'));
    Assert.That(bytes[1], Is.EqualTo((byte)'L'));
    Assert.That(bytes[2], Is.EqualTo((byte)'I'));
    Assert.That(bytes[3], Is.EqualTo((byte)'F'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderByte_EncodesChannelCount() {
    var file = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Rgba,
      BitsPerChannel = 8,
      PixelData = new byte[4]
    };

    var bytes = FlifWriter.ToBytes(file);
    var channelBits = bytes[4] & 0x07;

    Assert.That(channelBits, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderByte_EncodesGrayChannelCount() {
    var file = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Gray,
      BitsPerChannel = 8,
      PixelData = new byte[1]
    };

    var bytes = FlifWriter.ToBytes(file);
    var channelBits = bytes[4] & 0x07;

    Assert.That(channelBits, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderByte_EncodesInterlacing() {
    var file = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      IsInterlaced = true,
      PixelData = new byte[3]
    };

    var bytes = FlifWriter.ToBytes(file);
    var interlaceBit = (bytes[4] & 0x10) != 0;

    Assert.That(interlaceBit, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderByte_NoInterlacingByDefault() {
    var file = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = new byte[3]
    };

    var bytes = FlifWriter.ToBytes(file);
    var interlaceBit = (bytes[4] & 0x10) != 0;

    Assert.That(interlaceBit, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BpcByte_8Bit_IsZero() {
    var file = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = new byte[3]
    };

    var bytes = FlifWriter.ToBytes(file);

    Assert.That(bytes[5], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BpcByte_16Bit_IsOne() {
    var file = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 16,
      PixelData = new byte[6]
    };

    var bytes = FlifWriter.ToBytes(file);

    Assert.That(bytes[5], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions_EncodedAsVarint() {
    var file = new FlifFile {
      Width = 200,
      Height = 150,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = new byte[200 * 150 * 3]
    };

    var bytes = FlifWriter.ToBytes(file);

    // Parse the dimensions back from the varint at offset 6
    var offset = 6;
    var widthMinus1 = FlifVarint.Decode(bytes.AsSpan(), ref offset);
    var heightMinus1 = FlifVarint.Decode(bytes.AsSpan(), ref offset);

    Assert.That(widthMinus1 + 1, Is.EqualTo(200));
    Assert.That(heightMinus1 + 1, Is.EqualTo(150));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_AtLeastMinimum() {
    var file = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = new byte[3]
    };

    var bytes = FlifWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(FlifReader.MinFileSize));
  }
}

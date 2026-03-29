using System;
using System.Buffers.Binary;
using FileFormat.Jbig;

namespace FileFormat.Jbig.Tests;

[TestFixture]
public sealed class JbigWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JbigWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSize_AtLeast20Bytes() {
    var file = new JbigFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1]
    };

    var bytes = JbigWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(JbigHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderContainsDimensions() {
    var file = new JbigFile {
      Width = 32,
      Height = 16,
      PixelData = new byte[4 * 16]
    };

    var bytes = JbigWriter.ToBytes(file);

    var xd = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    var yd = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8));

    Assert.That(xd, Is.EqualTo(32));
    Assert.That(yd, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderD_IsZero() {
    var file = new JbigFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1]
    };

    var bytes = JbigWriter.ToBytes(file);

    Assert.That(bytes[1], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderP_IsOne() {
    var file = new JbigFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1]
    };

    var bytes = JbigWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsStripeMarker() {
    var file = new JbigFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1]
    };

    var bytes = JbigWriter.ToBytes(file);

    // Last two bytes should be the SDNORM marker (FF 02)
    Assert.That(bytes[^2], Is.EqualTo(0xFF));
    Assert.That(bytes[^1], Is.EqualTo(0x02));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Options_HasTPBON() {
    var file = new JbigFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1]
    };

    var bytes = JbigWriter.ToBytes(file);

    Assert.That(bytes[18] & JbigHeader.OptionTPBON, Is.Not.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_L0_EqualsHeight() {
    var file = new JbigFile {
      Width = 8,
      Height = 10,
      PixelData = new byte[10]
    };

    var bytes = JbigWriter.ToBytes(file);

    var l0 = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(12));

    Assert.That(l0, Is.EqualTo(10));
  }
}

using System;
using System.Buffers.Binary;
using FileFormat.Farbfeld;

namespace FileFormat.Farbfeld.Tests;

[TestFixture]
public sealed class FarbfeldWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithFarbfeldMagic() {
    var file = new FarbfeldFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[8]
    };

    var bytes = FarbfeldWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo((byte)'f'));
      Assert.That(bytes[1], Is.EqualTo((byte)'a'));
      Assert.That(bytes[2], Is.EqualTo((byte)'r'));
      Assert.That(bytes[3], Is.EqualTo((byte)'b'));
      Assert.That(bytes[4], Is.EqualTo((byte)'f'));
      Assert.That(bytes[5], Is.EqualTo((byte)'e'));
      Assert.That(bytes[6], Is.EqualTo((byte)'l'));
      Assert.That(bytes[7], Is.EqualTo((byte)'d'));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesCorrectDimensions() {
    var file = new FarbfeldFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 8]
    };

    var bytes = FarbfeldWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8)), Is.EqualTo(320u));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12)), Is.EqualTo(240u));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeMatchesExpected() {
    var file = new FarbfeldFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[4 * 3 * 8]
    };

    var bytes = FarbfeldWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16 + 4 * 3 * 8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[2 * 2 * 8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var file = new FarbfeldFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = FarbfeldWriter.ToBytes(file);

    var outputPixels = bytes.AsSpan(16).ToArray();
    Assert.That(outputPixels, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_1x1_ProducesExactly24Bytes() {
    var file = new FarbfeldFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[8]
    };

    var bytes = FarbfeldWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(24));
  }
}

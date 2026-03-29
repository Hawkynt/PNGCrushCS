using System;
using System.Buffers.Binary;
using FileFormat.Avs;

namespace FileFormat.Avs.Tests;

[TestFixture]
public sealed class AvsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderBytes() {
    var file = new AvsFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 4]
    };

    var bytes = AvsWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));
    var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));

    Assert.That(width, Is.EqualTo(320u));
    Assert.That(height, Is.EqualTo(240u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Size() {
    var w = 4;
    var h = 3;
    var file = new AvsFile {
      Width = w,
      Height = h,
      PixelData = new byte[w * h * 4]
    };

    var bytes = AvsWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(8 + w * h * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => AvsWriter.ToBytes(null!));
  }
}

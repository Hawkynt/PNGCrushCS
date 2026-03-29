using System;
using FileFormat.Wsq;

namespace FileFormat.Wsq.Tests;

[TestFixture]
public sealed class WsqWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WsqWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithSOI() {
    var file = new WsqFile {
      Width = 16,
      Height = 16,
      PixelData = new byte[16 * 16]
    };

    var bytes = WsqWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[1], Is.EqualTo(0xA0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EndsWithEOI() {
    var file = new WsqFile {
      Width = 16,
      Height = 16,
      PixelData = new byte[16 * 16]
    };

    var bytes = WsqWriter.ToBytes(file);

    Assert.That(bytes[^2], Is.EqualTo(0xFF));
    Assert.That(bytes[^1], Is.EqualTo(0xA1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsSOFMarker() {
    var file = new WsqFile {
      Width = 32,
      Height = 32,
      PixelData = new byte[32 * 32]
    };

    var bytes = WsqWriter.ToBytes(file);
    var foundSOF = false;
    for (var i = 0; i < bytes.Length - 1; ++i) {
      if (bytes[i] == 0xFF && bytes[i + 1] == 0xA2) {
        foundSOF = true;
        break;
      }
    }

    Assert.That(foundSOF, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsDHTMarker() {
    var file = new WsqFile {
      Width = 16,
      Height = 16,
      PixelData = new byte[16 * 16]
    };

    var bytes = WsqWriter.ToBytes(file);
    var foundDHT = false;
    for (var i = 0; i < bytes.Length - 1; ++i) {
      if (bytes[i] == 0xFF && bytes[i + 1] == 0xA6) {
        foundDHT = true;
        break;
      }
    }

    Assert.That(foundDHT, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsDimensionsInSOF() {
    var file = new WsqFile {
      Width = 100,
      Height = 80,
      PixelData = new byte[100 * 80]
    };

    var bytes = WsqWriter.ToBytes(file);

    // Find SOF marker and verify dimensions
    for (var i = 0; i < bytes.Length - 9; ++i) {
      if (bytes[i] != 0xFF || bytes[i + 1] != 0xA2)
        continue;

      // Skip marker(2) + length(2) + black(1) + white(1) = offset to height
      var height = (bytes[i + 6] << 8) | bytes[i + 7];
      var width = (bytes[i + 8] << 8) | bytes[i + 9];

      Assert.That(height, Is.EqualTo(80));
      Assert.That(width, Is.EqualTo(100));
      return;
    }

    Assert.Fail("SOF marker not found.");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputLargerThanMinimum() {
    var file = new WsqFile {
      Width = 16,
      Height = 16,
      PixelData = new byte[16 * 16]
    };

    var bytes = WsqWriter.ToBytes(file);

    // Must be larger than just SOI + EOI (4 bytes)
    Assert.That(bytes.Length, Is.GreaterThan(10));
  }
}

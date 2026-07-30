using System;
using System.Text;
using FileFormat.Fits;

namespace FileFormat.Fits.Tests;

[TestFixture]
public sealed class FitsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_StartsWithSimpleKeyword() {
    var file = new FitsFile {
      Width = 2,
      Height = 2,
      Bitpix = FitsBitpix.UInt8,
      PixelData = new byte[4]
    };

    var bytes = FitsWriter.ToBytes(file);
    var firstCard = Encoding.ASCII.GetString(bytes, 0, 80);

    Assert.That(firstCard, Does.StartWith("SIMPLE"));
    Assert.That(firstCard, Does.Contain("T"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_AlignedTo2880Bytes() {
    var file = new FitsFile {
      Width = 2,
      Height = 2,
      Bitpix = FitsBitpix.UInt8,
      PixelData = new byte[4]
    };

    var bytes = FitsWriter.ToBytes(file);

    Assert.That(bytes.Length % 2880, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_ContainsEndKeyword() {
    var file = new FitsFile {
      Width = 1,
      Height = 1,
      Bitpix = FitsBitpix.UInt8,
      PixelData = new byte[1]
    };

    var bytes = FitsWriter.ToBytes(file);

    // Search for END keyword in the header (first 2880 bytes)
    var headerText = Encoding.ASCII.GetString(bytes, 0, Math.Min(2880, bytes.Length));
    var endFound = false;
    for (var i = 0; i <= headerText.Length - 80; i += 80) {
      var card = headerText.Substring(i, 80);
      if (card.StartsWith("END")) {
        endFound = true;
        break;
      }
    }

    Assert.That(endFound, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderPaddedWithSpaces() {
    var file = new FitsFile {
      Width = 1,
      Height = 1,
      Bitpix = FitsBitpix.UInt8,
      PixelData = new byte[1]
    };

    var bytes = FitsWriter.ToBytes(file);

    // Find END card position and verify remaining header bytes are spaces
    var headerText = Encoding.ASCII.GetString(bytes, 0, 2880);
    var endPos = -1;
    for (var i = 0; i <= headerText.Length - 80; i += 80) {
      var card = headerText.Substring(i, 80);
      if (card.StartsWith("END")) {
        endPos = i + 80; // byte after END card
        break;
      }
    }

    Assert.That(endPos, Is.GreaterThan(0));
    for (var i = endPos; i < 2880; ++i)
      Assert.That(bytes[i], Is.EqualTo((byte)' '), $"Byte at header offset {i} should be space padding");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPaddedWithZeros() {
    var file = new FitsFile {
      Width = 10,
      Height = 10,
      Bitpix = FitsBitpix.UInt8,
      PixelData = new byte[100] // 100 bytes data, needs padding to 2880
    };

    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = 0xFF;

    var bytes = FitsWriter.ToBytes(file);

    // Data starts at offset 2880, 100 bytes data + 2780 padding zeros
    var dataStart = 2880;
    for (var i = dataStart + 100; i < dataStart + 2880; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Data padding byte at offset {i} should be zero");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBitpixKeyword() {
    var file = new FitsFile {
      Width = 1,
      Height = 1,
      Bitpix = FitsBitpix.Int16,
      PixelData = new byte[2]
    };

    var bytes = FitsWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes, 0, 2880);

    Assert.That(headerText, Does.Contain("BITPIX"));
    Assert.That(headerText, Does.Contain("16"));
  }
}

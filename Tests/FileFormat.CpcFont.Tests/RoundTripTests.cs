using System;
using System.IO;
using FileFormat.CpcFont;

namespace FileFormat.CpcFont.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[2048];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new CpcFontFile { RawData = rawData };

    var bytes = CpcFontWriter.ToBytes(original);
    var restored = CpcFontReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new CpcFontFile {
      RawData = new byte[2048]
    };

    var bytes = CpcFontWriter.ToBytes(original);
    var restored = CpcFontReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(128));
    Assert.That(restored.Height, Is.EqualTo(128));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var rawData = new byte[2048];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new CpcFontFile { RawData = rawData };

    var bytes = CpcFontWriter.ToBytes(original);
    var restored = CpcFontReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[2048];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new CpcFontFile { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpf");
    try {
      var bytes = CpcFontWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = CpcFontReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SpecificCharacterPattern() {
    var rawData = new byte[2048];
    // Character 0: letter-like pattern (top rows filled, middle narrow)
    rawData[0] = 0xFF; // row 0 all set
    rawData[1] = 0x81; // row 1 corners
    rawData[2] = 0x81; // row 2 corners
    rawData[3] = 0xFF; // row 3 middle bar
    rawData[4] = 0x81; // row 4 corners
    rawData[5] = 0x81; // row 5 corners
    rawData[6] = 0xFF; // row 6 all set
    rawData[7] = 0x00; // row 7 empty

    // Character 65 ('A'): simple pattern
    var offset = 65 * 8;
    rawData[offset] = 0x18;
    rawData[offset + 1] = 0x24;
    rawData[offset + 2] = 0x42;
    rawData[offset + 3] = 0x7E;
    rawData[offset + 4] = 0x42;
    rawData[offset + 5] = 0x42;
    rawData[offset + 6] = 0x42;
    rawData[offset + 7] = 0x00;

    var original = new CpcFontFile { RawData = rawData };

    var bytes = CpcFontWriter.ToBytes(original);
    var restored = CpcFontReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_ProducesIndexed1() {
    var rawData = new byte[2048];
    rawData[0] = 0xFF;
    rawData[8] = 0xAA;

    var file = new CpcFontFile { RawData = rawData };

    var raw = CpcFontFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(128));
    Assert.That(raw.Height, Is.EqualTo(128));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Indexed1));
  }
}

using System;
using System.IO;
using FileFormat.WigmoreArtist;

namespace FileFormat.WigmoreArtist.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[WigmoreArtistFile.MinPayloadSize + 200];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new WigmoreArtistFile { LoadAddress = 0x2000, RawData = rawData };

    var bytes = WigmoreArtistWriter.ToBytes(original);
    var restored = WigmoreArtistReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[WigmoreArtistFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new WigmoreArtistFile { LoadAddress = 0x4000, RawData = rawData };
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wig");
    try {
      File.WriteAllBytes(path, WigmoreArtistWriter.ToBytes(original));
      var restored = WigmoreArtistReader.FromFile(new FileInfo(path));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var rawData = new byte[WigmoreArtistFile.MinPayloadSize];
    var original = new WigmoreArtistFile { LoadAddress = 0x6000, RawData = rawData };

    var bytes = WigmoreArtistWriter.ToBytes(original);
    var restored = WigmoreArtistReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var rawData = new byte[WigmoreArtistFile.MinPayloadSize];
    Array.Fill(rawData, (byte)0xFF);

    var original = new WigmoreArtistFile { LoadAddress = 0xFFFF, RawData = rawData };

    var bytes = WigmoreArtistWriter.ToBytes(original);
    var restored = WigmoreArtistReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ProducesCorrectDimensions() {
    var rawData = new byte[WigmoreArtistFile.MinPayloadSize];
    var file = new WigmoreArtistFile { LoadAddress = 0x2000, RawData = rawData };

    var raw = WigmoreArtistFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }
}

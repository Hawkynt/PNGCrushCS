using System;
using FileFormat.Mng;

namespace FileFormat.Mng.Tests;

[TestFixture]
public sealed class MngWriterTests {

  private static readonly byte[] _MNG_SIGNATURE = { 0x8A, 0x4D, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMngSignature() {
    var file = new MngFile {
      Width = 1,
      Height = 1,
      TicksPerSecond = 1000,
      Frames = []
    };

    var bytes = MngWriter.ToBytes(file);

    for (var i = 0; i < _MNG_SIGNATURE.Length; ++i)
      Assert.That(bytes[i], Is.EqualTo(_MNG_SIGNATURE[i]), $"Signature byte {i} mismatch");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasMhdrChunk() {
    var file = new MngFile {
      Width = 100,
      Height = 80,
      TicksPerSecond = 1000,
      Frames = []
    };

    var bytes = MngWriter.ToBytes(file);

    // MHDR chunk type starts at offset 12 (8 sig + 4 length)
    Assert.That(bytes[12], Is.EqualTo((byte)'M'));
    Assert.That(bytes[13], Is.EqualTo((byte)'H'));
    Assert.That(bytes[14], Is.EqualTo((byte)'D'));
    Assert.That(bytes[15], Is.EqualTo((byte)'R'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasMendChunk() {
    var file = new MngFile {
      Width = 1,
      Height = 1,
      TicksPerSecond = 1000,
      Frames = []
    };

    var bytes = MngWriter.ToBytes(file);

    // Find MEND chunk near end: 4-byte length (0) + "MEND" + 4-byte CRC = last 12 bytes
    var mendOffset = bytes.Length - 12;
    // Length should be 0
    Assert.That(bytes[mendOffset], Is.EqualTo(0));
    Assert.That(bytes[mendOffset + 1], Is.EqualTo(0));
    Assert.That(bytes[mendOffset + 2], Is.EqualTo(0));
    Assert.That(bytes[mendOffset + 3], Is.EqualTo(0));
    // Type should be MEND
    Assert.That(bytes[mendOffset + 4], Is.EqualTo((byte)'M'));
    Assert.That(bytes[mendOffset + 5], Is.EqualTo((byte)'E'));
    Assert.That(bytes[mendOffset + 6], Is.EqualTo((byte)'N'));
    Assert.That(bytes[mendOffset + 7], Is.EqualTo((byte)'D'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FramesEmbedded_ContainsIhdrChunks() {
    var png = MngTestHelper.BuildMinimalPng();
    var file = new MngFile {
      Width = 1,
      Height = 1,
      TicksPerSecond = 1000,
      Frames = [png]
    };

    var bytes = MngWriter.ToBytes(file);

    // Look for IHDR chunk type in output
    var found = false;
    for (var i = 0; i < bytes.Length - 4; ++i) {
      if (bytes[i] == (byte)'I' && bytes[i + 1] == (byte)'H' && bytes[i + 2] == (byte)'D' && bytes[i + 3] == (byte)'R') {
        found = true;
        break;
      }
    }

    Assert.That(found, Is.True, "Expected IHDR chunk from embedded PNG frame");
  }
}

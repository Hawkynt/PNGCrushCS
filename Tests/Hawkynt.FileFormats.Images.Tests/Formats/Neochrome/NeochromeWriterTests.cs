using System;
using System.Buffers.Binary;
using FileFormat.Neochrome;

namespace FileFormat.Neochrome.Tests;

[TestFixture]
public sealed class NeochromeWriterTests {

  [Test]
  public void ToBytes_StandardLow_IsExactly32128BytesAndWritesPublishedFields() {
    var file = _Create(0, 0, 32_000);
    file = file with {
      FileName = "PIC     .NEO"u8.ToArray(),
      AnimationLimits = unchecked((short)0x8123),
      AnimSpeed = 5,
      AnimDirection = 0xFE,
      AnimSteps = 10,
    };

    var bytes = NeochromeWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(32_128));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2)), Is.EqualTo(0));
      Assert.That(bytes.AsSpan(36, 12).ToArray(), Is.EqualTo("PIC     .NEO"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(48)), Is.EqualTo(unchecked((short)0x8123)));
      Assert.That(bytes[50], Is.EqualTo(5));
      Assert.That(bytes[51], Is.EqualTo(0xFE));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(52)), Is.EqualTo(10));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(58)), Is.EqualTo(320));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(60)), Is.EqualTo(200));
    });
  }

  [Test]
  public void ToBytes_PixelDataStartsAt128WithoutClipping() {
    var pixels = new byte[32_000];
    pixels[0] = 0xAA;
    pixels[1] = 0xBB;
    pixels[^1] = 0xCC;

    var bytes = NeochromeWriter.ToBytes(_Create(0, 0, 32_000) with { PixelData = pixels });

    Assert.Multiple(() => {
      Assert.That(bytes[128], Is.EqualTo(0xAA));
      Assert.That(bytes[129], Is.EqualTo(0xBB));
      Assert.That(bytes[^1], Is.EqualTo(0xCC));
    });
  }

  [Test]
  public void ToBytes_VirtualCanvas_IsExactly128128Bytes() {
    var bytes = NeochromeWriter.ToBytes(_Create(unchecked((short)0xBABE), 0, 128_000));
    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(128_128));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes), Is.EqualTo(0xBABE));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(58)), Is.EqualTo(640));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(60)), Is.EqualTo(400));
    });
  }

  [TestCase((short)1)]
  [TestCase((short)2)]
  public void ToBytes_StandardResolution_PreservesResolutionButStoredHeaderGeometryRemains320x200(short resolution) {
    var bytes = NeochromeWriter.ToBytes(_Create(0, resolution, 32_000));
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2)), Is.EqualTo(resolution));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(58)), Is.EqualTo(320));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(60)), Is.EqualTo(200));
    });
  }

  [Test]
  public void ToBytes_LegacyMinimalObject_FillsSpecifiedHeaderDefaults() {
    var bytes = NeochromeWriter.ToBytes(new NeochromeFile {
      Palette = new short[16],
      PixelData = new byte[32_000],
    });

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(36, 12).ToArray(), Is.All.EqualTo(0));
      Assert.That(bytes.AsSpan(62, 66).ToArray(), Is.All.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(58)), Is.EqualTo(320));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(60)), Is.EqualTo(200));
    });
  }

  [Test]
  public void ToBytes_InvalidRasterPaletteOffsetsAndDimensions_AreRejected() {
    var valid = _Create(0, 0, 32_000);
    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => NeochromeWriter.ToBytes(valid with { PixelData = [0] }));
      Assert.Throws<ArgumentException>(() => NeochromeWriter.ToBytes(valid with { Palette = [0] }));
      Assert.Throws<ArgumentException>(() => NeochromeWriter.ToBytes(valid with { AnimXOffset = 1 }));
      Assert.Throws<ArgumentException>(() => NeochromeWriter.ToBytes(valid with { AnimWidth = 640 }));
      Assert.Throws<ArgumentException>(() => NeochromeWriter.ToBytes(valid with { FileName = [0] }));
      Assert.Throws<ArgumentException>(() => NeochromeWriter.ToBytes(valid with { Reserved = [0] }));
    });
  }

  private static NeochromeFile _Create(short flag, short resolution, int rasterLength) => new() {
    Flag = flag,
    Resolution = resolution,
    Palette = new short[16],
    PixelData = new byte[rasterLength],
  };
}

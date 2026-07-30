using System;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using FileFormat.Apng;
using FileFormat.Png;

namespace FileFormat.Apng.Tests;

[TestFixture]
public sealed class ApngWriterTests {

  private static readonly byte[] _PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

  private static ApngFile _CreateSimpleApng(int frameCount = 1) {
    var frames = new ApngFrame[frameCount];
    for (var i = 0; i < frameCount; ++i)
      frames[i] = new ApngFrame {
        Width = 2,
        Height = 2,
        XOffset = 0,
        YOffset = 0,
        DelayNumerator = (ushort)(100 * (i + 1)),
        DelayDenominator = 1000,
        DisposeOp = ApngDisposeOp.None,
        BlendOp = ApngBlendOp.Source,
        PixelData = [new byte[6], new byte[6]]
      };

    return new ApngFile {
      Width = 2,
      Height = 2,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      NumPlays = 0,
      Frames = frames
    };
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithPngSignature() {
    var file = _CreateSimpleApng();
    var bytes = ApngWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(8));
    Assert.That(bytes[..8], Is.EqualTo(_PngSignature));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasIhdrChunk() {
    var file = _CreateSimpleApng();
    var bytes = ApngWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("IHDR"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasActlChunk() {
    var file = _CreateSimpleApng();
    var bytes = ApngWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("acTL"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasFctlChunk() {
    var file = _CreateSimpleApng();
    var bytes = ApngWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("fcTL"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasIdatChunk() {
    var file = _CreateSimpleApng();
    var bytes = ApngWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("IDAT"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultiFrame_HasFdatChunk() {
    var file = _CreateSimpleApng(2);
    var bytes = ApngWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("fdAT"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EndsWithIend() {
    var file = _CreateSimpleApng();
    var bytes = ApngWriter.ToBytes(file);
    var last12 = bytes[^12..];

    Assert.That(last12[0..4], Is.EqualTo(new byte[] { 0, 0, 0, 0 }), "IEND length should be 0");

    var iendType = Encoding.ASCII.GetString(last12, 4, 4);
    Assert.That(iendType, Is.EqualTo("IEND"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasValidCrc32() {
    var file = _CreateSimpleApng();
    var bytes = ApngWriter.ToBytes(file);

    // Verify CRC of the IHDR chunk
    var offset = 8; // skip signature
    var chunkLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
    var typeBytes = bytes.AsSpan(offset + 4, 4);
    var dataBytes = bytes.AsSpan(offset + 8, chunkLength);
    var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + chunkLength));

    var crc = new Crc32();
    crc.Append(typeBytes);
    crc.Append(dataBytes);
    var computedCrc = crc.GetCurrentHashAsUInt32();

    Assert.That(computedCrc, Is.EqualTo(storedCrc));
  }
}

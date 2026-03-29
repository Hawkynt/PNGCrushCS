using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.IffDeep;

namespace FileFormat.IffDeep.Tests;

[TestFixture]
public sealed class IffDeepWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => IffDeepWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithFormMagic() {
    var file = _MakeRgb(2, 2);
    var bytes = IffDeepWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("FORM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasDeepFormType() {
    var file = _MakeRgb(2, 2);
    var bytes = IffDeepWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("DEEP"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasDgblChunk() {
    var file = _MakeRgb(4, 3);
    var bytes = IffDeepWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 12, 4), Is.EqualTo("DGBL"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DgblChunkSize8() {
    var file = _MakeRgb(4, 3);
    var bytes = IffDeepWriter.ToBytes(file);

    var dgblSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16));
    Assert.That(dgblSize, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DgblContainsDimensions() {
    var file = _MakeRgb(320, 240);
    var bytes = IffDeepWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(20));
    var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(22));
    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DgblContainsCompression() {
    var file = new IffDeepFile {
      Width = 2,
      Height = 2,
      HasAlpha = false,
      Compression = IffDeepCompression.Rle,
      PixelData = new byte[2 * 2 * 3]
    };
    var bytes = IffDeepWriter.ToBytes(file);

    var comp = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(24));
    Assert.That(comp, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DgblNumElements_Rgb() {
    var file = _MakeRgb(2, 2);
    var bytes = IffDeepWriter.ToBytes(file);

    var numElements = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(26));
    Assert.That(numElements, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DgblNumElements_Rgba() {
    var file = _MakeRgba(2, 2);
    var bytes = IffDeepWriter.ToBytes(file);

    var numElements = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(26));
    Assert.That(numElements, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasDpelChunk() {
    var file = _MakeRgb(2, 2);
    var bytes = IffDeepWriter.ToBytes(file);

    // DGBL: offset 12 + 8 (header) + 8 (data) = 28
    Assert.That(Encoding.ASCII.GetString(bytes, 28, 4), Is.EqualTo("DPEL"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DpelChunkSize_Rgb() {
    var file = _MakeRgb(2, 2);
    var bytes = IffDeepWriter.ToBytes(file);

    var dpelSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(32));
    Assert.That(dpelSize, Is.EqualTo(12)); // 3 elements * 4 bytes
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DpelChunkSize_Rgba() {
    var file = _MakeRgba(2, 2);
    var bytes = IffDeepWriter.ToBytes(file);

    var dpelSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(32));
    Assert.That(dpelSize, Is.EqualTo(16)); // 4 elements * 4 bytes
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DpelAlphaElement_HasType1() {
    var file = _MakeRgba(2, 2);
    var bytes = IffDeepWriter.ToBytes(file);

    // DPEL data starts at 36; 4th element (alpha) at offset 36 + 12 = 48
    var alphaType = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(48));
    Assert.That(alphaType, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasBodyChunk() {
    var file = _MakeRgb(2, 2);
    var bytes = IffDeepWriter.ToBytes(file);

    // DGBL: 12..28; DPEL: 28..48 (header 8 + data 12); BODY at 48
    Assert.That(Encoding.ASCII.GetString(bytes, 48, 4), Is.EqualTo("BODY"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FormSizeIsCorrect() {
    var file = _MakeRgb(1, 1);
    var bytes = IffDeepWriter.ToBytes(file);

    var formSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    Assert.That(formSize, Is.EqualTo(bytes.Length - 8));
  }

  private static IffDeepFile _MakeRgb(int w, int h) => new() {
    Width = w,
    Height = h,
    HasAlpha = false,
    Compression = IffDeepCompression.None,
    PixelData = new byte[w * h * 3]
  };

  private static IffDeepFile _MakeRgba(int w, int h) => new() {
    Width = w,
    Height = h,
    HasAlpha = true,
    Compression = IffDeepCompression.None,
    PixelData = new byte[w * h * 4]
  };
}

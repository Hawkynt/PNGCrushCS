using System;
using System.IO;
using FileFormat.ZxBorderScreen;
using FileFormat.Core;

namespace FileFormat.ZxBorderScreen.Tests;

[TestFixture]
public sealed class ZxBorderScreenReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxBorderScreenReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxBorderScreenReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bsc"));
    Assert.Throws<FileNotFoundException>(() => ZxBorderScreenReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxBorderScreenReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxBorderScreenReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_TooLarge_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxBorderScreenReader.FromBytes(new byte[12000]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_OffByOne_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxBorderScreenReader.FromBytes(new byte[11135]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[11136];
    var result = ZxBorderScreenReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
    Assert.That(result.AttributeData.Length, Is.EqualTo(768));
    Assert.That(result.BorderData.Length, Is.EqualTo(4224));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AttributeDataPreserved() {
    var data = new byte[11136];
    for (var i = 0; i < 768; ++i)
      data[6144 + i] = (byte)(i % 256);

    var result = ZxBorderScreenReader.FromBytes(data);

    for (var i = 0; i < 768; ++i)
      Assert.That(result.AttributeData[i], Is.EqualTo((byte)(i % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BorderDataPreserved() {
    var data = new byte[11136];
    for (var i = 0; i < 4224; ++i)
      data[6144 + 768 + i] = (byte)(i % 256);

    var result = ZxBorderScreenReader.FromBytes(data);

    for (var i = 0; i < 4224; ++i)
      Assert.That(result.BorderData[i], Is.EqualTo((byte)(i % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_StreamParsing() {
    var data = new byte[11136];
    data[6144] = 0x47;
    using var ms = new MemoryStream(data);
    var result = ZxBorderScreenReader.FromStream(ms);
    Assert.That(result.AttributeData[0], Is.EqualTo(0x47));
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new ZxBorderScreenFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      BorderData = new byte[4224],
    };

    var bytes = ZxBorderScreenWriter.ToBytes(original);
    var restored = ZxBorderScreenReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
    Assert.That(restored.BorderData, Is.EqualTo(original.BorderData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KnownPattern() {
    var bitmap = new byte[6144];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i * 7 % 256);

    var attributes = new byte[768];
    for (var i = 0; i < attributes.Length; ++i)
      attributes[i] = (byte)(i * 13 % 256);

    var border = new byte[4224];
    for (var i = 0; i < border.Length; ++i)
      border[i] = (byte)(i * 3 % 256);

    var original = new ZxBorderScreenFile {
      BitmapData = bitmap,
      AttributeData = attributes,
      BorderData = border,
    };

    var bytes = ZxBorderScreenWriter.ToBytes(original);
    var restored = ZxBorderScreenReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
    Assert.That(restored.BorderData, Is.EqualTo(original.BorderData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var bitmap = new byte[6144];
    Array.Fill(bitmap, (byte)0xFF);

    var attributes = new byte[768];
    Array.Fill(attributes, (byte)0xFF);

    var border = new byte[4224];
    Array.Fill(border, (byte)0xFF);

    var original = new ZxBorderScreenFile {
      BitmapData = bitmap,
      AttributeData = attributes,
      BorderData = border,
    };

    var bytes = ZxBorderScreenWriter.ToBytes(original);
    var restored = ZxBorderScreenReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
    Assert.That(restored.BorderData, Is.EqualTo(original.BorderData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[11136];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);

    var file = ZxBorderScreenReader.FromBytes(original);
    var written = ZxBorderScreenWriter.ToBytes(file);

    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new byte[11136];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i & 0xFF);

    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ZxBorderScreenReader.FromFile(new FileInfo(tmp));
      var written = ZxBorderScreenWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_InterleaveVerification_Row1() {
    var bitmap = new byte[6144];
    var row1LinearOffset = 1 * 32;
    bitmap[row1LinearOffset] = 0xBE;

    var original = new ZxBorderScreenFile {
      BitmapData = bitmap,
      AttributeData = new byte[768],
      BorderData = new byte[4224],
    };

    var bytes = ZxBorderScreenWriter.ToBytes(original);
    Assert.That(bytes[256], Is.EqualTo(0xBE));

    var restored = ZxBorderScreenReader.FromBytes(bytes);
    Assert.That(restored.BitmapData[row1LinearOffset], Is.EqualTo(0xBE));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixelPerRow_AllRows() {
    var bitmap = new byte[6144];
    for (var row = 0; row < 192; ++row)
      bitmap[row * 32] = (byte)(row % 256);

    var original = new ZxBorderScreenFile {
      BitmapData = bitmap,
      AttributeData = new byte[768],
      BorderData = new byte[4224],
    };

    var bytes = ZxBorderScreenWriter.ToBytes(original);
    var restored = ZxBorderScreenReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new ZxBorderScreenFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      BorderData = new byte[4224],
    };

    var bytes = ZxBorderScreenWriter.ToBytes(original);
    var restored = ZxBorderScreenReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
  }
}


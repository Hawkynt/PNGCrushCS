using System;
using System.IO;
using FileFormat.ZxMultiArtist;
using FileFormat.Core;

namespace FileFormat.ZxMultiArtist.Tests;

[TestFixture]
public sealed class ZxMultiArtistReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxMultiArtistReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxMultiArtistReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mg1"));
    Assert.Throws<FileNotFoundException>(() => ZxMultiArtistReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxMultiArtistReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxMultiArtistReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxMultiArtistReader.FromBytes(new byte[8000]));

  [Test]
  [Category("Unit")]
  public void FromBytes_Mg8_Succeeds() {
    var data = new byte[6912];
    var result = ZxMultiArtistReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(ZxMultiArtistMode.Mg8));
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
    Assert.That(result.AttributeData.Length, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Mg4_Succeeds() {
    var data = new byte[7680];
    var result = ZxMultiArtistReader.FromBytes(data);

    Assert.That(result.Mode, Is.EqualTo(ZxMultiArtistMode.Mg4));
    Assert.That(result.AttributeData.Length, Is.EqualTo(1536));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Mg2_Succeeds() {
    var data = new byte[9216];
    var result = ZxMultiArtistReader.FromBytes(data);

    Assert.That(result.Mode, Is.EqualTo(ZxMultiArtistMode.Mg2));
    Assert.That(result.AttributeData.Length, Is.EqualTo(3072));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Mg1_Succeeds() {
    var data = new byte[12288];
    var result = ZxMultiArtistReader.FromBytes(data);

    Assert.That(result.Mode, Is.EqualTo(ZxMultiArtistMode.Mg1));
    Assert.That(result.AttributeData.Length, Is.EqualTo(6144));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AttributeDataPreserved_Mg4() {
    var data = new byte[7680];
    for (var i = 0; i < 1536; ++i)
      data[6144 + i] = (byte)(i % 256);

    var result = ZxMultiArtistReader.FromBytes(data);

    for (var i = 0; i < 1536; ++i)
      Assert.That(result.AttributeData[i], Is.EqualTo((byte)(i % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithHintMode_OverridesDetection() {
    var data = new byte[6912];
    var result = ZxMultiArtistReader.FromBytes(data, ZxMultiArtistMode.Mg8);

    Assert.That(result.Mode, Is.EqualTo(ZxMultiArtistMode.Mg8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithHintMode_SizeMismatch_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxMultiArtistReader.FromBytes(new byte[6912], ZxMultiArtistMode.Mg1));

  [Test]
  [Category("Unit")]
  public void FromBytes_StreamParsing_Mg2() {
    var data = new byte[9216];
    data[6144] = 0x47;
    using var ms = new MemoryStream(data);
    var result = ZxMultiArtistReader.FromStream(ms);
    Assert.That(result.AttributeData[0], Is.EqualTo(0x47));
    Assert.That(result.Mode, Is.EqualTo(ZxMultiArtistMode.Mg2));
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  [TestCase(ZxMultiArtistMode.Mg8, 6912)]
  [TestCase(ZxMultiArtistMode.Mg4, 7680)]
  [TestCase(ZxMultiArtistMode.Mg2, 9216)]
  [TestCase(ZxMultiArtistMode.Mg1, 12288)]
  public void RoundTrip_AllZeros(ZxMultiArtistMode mode, int fileSize) {
    var attrSize = ZxMultiArtistFile.GetAttributeSize(mode);
    var original = new ZxMultiArtistFile {
      Mode = mode,
      BitmapData = new byte[6144],
      AttributeData = new byte[attrSize],
    };

    var bytes = ZxMultiArtistWriter.ToBytes(original);
    Assert.That(bytes.Length, Is.EqualTo(fileSize));

    var restored = ZxMultiArtistReader.FromBytes(bytes);

    Assert.That(restored.Mode, Is.EqualTo(mode));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
  }

  [Test]
  [Category("Integration")]
  [TestCase(ZxMultiArtistMode.Mg8, 6912)]
  [TestCase(ZxMultiArtistMode.Mg4, 7680)]
  [TestCase(ZxMultiArtistMode.Mg2, 9216)]
  [TestCase(ZxMultiArtistMode.Mg1, 12288)]
  public void RoundTrip_AllBytes_Preserved(ZxMultiArtistMode mode, int fileSize) {
    var data = new byte[fileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 7 & 0xFF);

    var file = ZxMultiArtistReader.FromBytes(data);
    var written = ZxMultiArtistWriter.ToBytes(file);

    Assert.That(written, Is.EqualTo(data));
  }

  [Test]
  [Category("Integration")]
  [TestCase(ZxMultiArtistMode.Mg8)]
  [TestCase(ZxMultiArtistMode.Mg4)]
  [TestCase(ZxMultiArtistMode.Mg2)]
  [TestCase(ZxMultiArtistMode.Mg1)]
  public void RoundTrip_AllOnes(ZxMultiArtistMode mode) {
    var attrSize = ZxMultiArtistFile.GetAttributeSize(mode);
    var bitmap = new byte[6144];
    Array.Fill(bitmap, (byte)0xFF);

    var attributes = new byte[attrSize];
    Array.Fill(attributes, (byte)0xFF);

    var original = new ZxMultiArtistFile {
      Mode = mode,
      BitmapData = bitmap,
      AttributeData = attributes,
    };

    var bytes = ZxMultiArtistWriter.ToBytes(original);
    var restored = ZxMultiArtistReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_Mg1() {
    var data = new byte[12288];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);

    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mg1");
    try {
      File.WriteAllBytes(tmp, data);
      var file = ZxMultiArtistReader.FromFile(new FileInfo(tmp));
      Assert.That(file.Mode, Is.EqualTo(ZxMultiArtistMode.Mg1));
      var written = ZxMultiArtistWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(data));
    } finally {
      File.Delete(tmp);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_Mg4() {
    var data = new byte[7680];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);

    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mg4");
    try {
      File.WriteAllBytes(tmp, data);
      var file = ZxMultiArtistReader.FromFile(new FileInfo(tmp));
      Assert.That(file.Mode, Is.EqualTo(ZxMultiArtistMode.Mg4));
      var written = ZxMultiArtistWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(data));
    } finally {
      File.Delete(tmp);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_InterleaveVerification_Row1_Mg2() {
    var bitmap = new byte[6144];
    var row1LinearOffset = 1 * 32;
    bitmap[row1LinearOffset] = 0xBE;

    var original = new ZxMultiArtistFile {
      Mode = ZxMultiArtistMode.Mg2,
      BitmapData = bitmap,
      AttributeData = new byte[3072],
    };

    var bytes = ZxMultiArtistWriter.ToBytes(original);
    Assert.That(bytes[256], Is.EqualTo(0xBE));

    var restored = ZxMultiArtistReader.FromBytes(bytes);
    Assert.That(restored.BitmapData[row1LinearOffset], Is.EqualTo(0xBE));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new ZxMultiArtistFile {
      Mode = ZxMultiArtistMode.Mg4,
      BitmapData = new byte[6144],
      AttributeData = new byte[1536],
    };

    var bytes = ZxMultiArtistWriter.ToBytes(original);
    var restored = ZxMultiArtistReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
  }
}


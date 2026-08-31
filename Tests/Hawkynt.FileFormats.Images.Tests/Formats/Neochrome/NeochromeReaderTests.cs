using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Neochrome;

namespace FileFormat.Neochrome.Tests;

[TestFixture]
public sealed class NeochromeReaderTests {

  [Test]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NeochromeReader.FromBytes(null!));

  [Test]
  public void FromFile_NullAndMissing_AreRejected() {
    Assert.Throws<ArgumentNullException>(() => NeochromeReader.FromFile(null!));
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".neo"));
    Assert.Throws<FileNotFoundException>(() => NeochromeReader.FromFile(missing));
  }

  [Test]
  public void FromBytes_LowResolution_ParsesPublishedHeaderLayout() {
    var data = _Build(0, 0);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 0x0777);
    "PIC     .NEO"u8.CopyTo(data.AsSpan(36, 12));
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(48), unchecked((short)0x8123));
    data[50] = 5;
    data[51] = 0xFE;
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(52), 10);

    var result = NeochromeReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Resolution, Is.EqualTo(0));
      Assert.That(result.Palette[0], Is.EqualTo((short)0x0777));
      Assert.That(result.FileName, Is.EqualTo("PIC     .NEO"u8.ToArray()));
      Assert.That(result.AnimationLimits, Is.EqualTo(unchecked((short)0x8123)));
      Assert.That(result.AnimSpeed, Is.EqualTo(5));
      Assert.That(result.AnimDirection, Is.EqualTo(0xFE));
      Assert.That(result.AnimSteps, Is.EqualTo(10));
      Assert.That(result.PixelData, Has.Length.EqualTo(32_000));
    });
  }

  [TestCase((short)1, 640, 200)]
  [TestCase((short)2, 640, 400)]
  public void FromBytes_StandardResolution_DecodesGeometry(short resolution, int width, int height) {
    var result = NeochromeReader.FromBytes(_Build(0, resolution));
    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(width));
      Assert.That(result.Height, Is.EqualTo(height));
      Assert.That(result.PixelData, Has.Length.EqualTo(32_000));
    });
  }

  [Test]
  public void FromBytes_VirtualCanvas_UsesBabeAnd128000RasterBytes() {
    var result = NeochromeReader.FromBytes(_Build(unchecked((short)0xBABE), 0));
    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(640));
      Assert.That(result.Height, Is.EqualTo(400));
      Assert.That(result.PixelData, Has.Length.EqualTo(128_000));
    });
  }

  [Test]
  public void FromBytes_TruncatedSurplusAndUnsupportedHeaders_AreRejected() {
    var valid = _Build(0, 0);
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => NeochromeReader.FromBytes(valid[..^1]));
      Assert.Throws<InvalidDataException>(() => NeochromeReader.FromBytes([.. valid, 0]));

      var badFlag = _Build(0, 0); BinaryPrimitives.WriteInt16BigEndian(badFlag, 1);
      Assert.Throws<InvalidDataException>(() => NeochromeReader.FromBytes(badFlag));

      var badResolution = _Build(0, 0); BinaryPrimitives.WriteInt16BigEndian(badResolution.AsSpan(2), 3);
      Assert.Throws<InvalidDataException>(() => NeochromeReader.FromBytes(badResolution));

      var badVirtualResolution = _Build(unchecked((short)0xBABE), 0); BinaryPrimitives.WriteInt16BigEndian(badVirtualResolution.AsSpan(2), 1);
      Assert.Throws<InvalidDataException>(() => NeochromeReader.FromBytes(badVirtualResolution));
    });
  }

  [Test]
  public void FromBytes_InvalidStoredDimensionsAndOffsets_AreRejected() {
    var badWidth = _Build(0, 0); BinaryPrimitives.WriteInt16BigEndian(badWidth.AsSpan(58), 319);
    var badHeight = _Build(0, 0); BinaryPrimitives.WriteInt16BigEndian(badHeight.AsSpan(60), 199);
    var badOffset = _Build(0, 0); BinaryPrimitives.WriteInt16BigEndian(badOffset.AsSpan(54), 1);
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => NeochromeReader.FromBytes(badWidth));
      Assert.Throws<InvalidDataException>(() => NeochromeReader.FromBytes(badHeight));
      Assert.Throws<InvalidDataException>(() => NeochromeReader.FromBytes(badOffset));
    });
  }

  [Test]
  public void FromStream_ValidParsesCorrectly() {
    using var stream = new MemoryStream(_Build(0, 0));
    Assert.That(NeochromeReader.FromStream(stream).Width, Is.EqualTo(320));
  }

  private static byte[] _Build(short flag, short resolution) {
    var virtualCanvas = unchecked((ushort)flag) == 0xBABE;
    var data = new byte[128 + (virtualCanvas ? 128_000 : 32_000)];
    BinaryPrimitives.WriteInt16BigEndian(data, flag);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), resolution);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(58), virtualCanvas ? (short)640 : (short)320);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(60), virtualCanvas ? (short)400 : (short)200);
    for (var i = 128; i < data.Length; ++i)
      data[i] = (byte)(i * 7);
    return data;
  }
}

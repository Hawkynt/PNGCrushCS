using System;
using System.IO;
using FileFormat.SpritePad;
using FileFormat.Core;

namespace FileFormat.SpritePad.Tests;

[TestFixture]
public sealed class SpritePadReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpritePadReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spd"));
    Assert.Throws<FileNotFoundException>(() => SpritePadReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpritePadReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => SpritePadReader.FromBytes(new byte[10]));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpritePadReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidV1_ParsesVersion() {
    var data = TestHelpers._BuildValidSpritePadV1(2, false);
    var result = SpritePadReader.FromBytes(data);

    Assert.That(result.Version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidV1_ParsesSpriteCount() {
    var data = TestHelpers._BuildValidSpritePadV1(3, false);
    var result = SpritePadReader.FromBytes(data);

    Assert.That(result.SpriteCount, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidV1_ParsesMulticolorFlag() {
    var data = TestHelpers._BuildValidSpritePadV1(1, true);
    var result = SpritePadReader.FromBytes(data);

    Assert.That(result.IsMulticolor, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidV1_NoMulticolor() {
    var data = TestHelpers._BuildValidSpritePadV1(1, false);
    var result = SpritePadReader.FromBytes(data);

    Assert.That(result.IsMulticolor, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidV1_RawDataLength() {
    var data = TestHelpers._BuildValidSpritePadV1(2, false);
    var result = SpritePadReader.FromBytes(data);

    Assert.That(result.RawData.Length, Is.EqualTo(2 * SpritePadFile.BytesPerSprite));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidV2_ParsesVersion() {
    var data = TestHelpers._BuildValidSpritePadV2(2, false);
    var result = SpritePadReader.FromBytes(data);

    Assert.That(result.Version, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidV2_HasExtraHeader() {
    var data = TestHelpers._BuildValidSpritePadV2(1, false);
    var result = SpritePadReader.FromBytes(data);

    Assert.That(result.ExtraHeader.Length, Is.EqualTo(SpritePadFile.V2HeaderSize - SpritePadFile.V1HeaderSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_OversizedSpriteCount_ClampedToAvailable() {
    var data = TestHelpers._BuildValidSpritePadV1(2, false);
    data[1] = 200; // bogus count
    var result = SpritePadReader.FromBytes(data);

    Assert.That(result.SpriteCount, Is.EqualTo(2));
  }
}

[TestFixture]
public sealed class SpritePadRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_V1_AllFieldsPreserved() {
    var rawData = new byte[3 * SpritePadFile.BytesPerSprite];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new SpritePadFile { Version = 1, SpriteCount = 3, IsMulticolor = true, RawData = rawData };

    var bytes = SpritePadWriter.ToBytes(original);
    var restored = SpritePadReader.FromBytes(bytes);

    Assert.That(restored.Version, Is.EqualTo(original.Version));
    Assert.That(restored.SpriteCount, Is.EqualTo(original.SpriteCount));
    Assert.That(restored.IsMulticolor, Is.EqualTo(original.IsMulticolor));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_V2_AllFieldsPreserved() {
    var rawData = new byte[2 * SpritePadFile.BytesPerSprite];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var extraHeader = new byte[] { 0xAB, 0xCD, 0xEF };
    var original = new SpritePadFile { Version = 2, SpriteCount = 2, IsMulticolor = false, RawData = rawData, ExtraHeader = extraHeader };

    var bytes = SpritePadWriter.ToBytes(original);
    var restored = SpritePadReader.FromBytes(bytes);

    Assert.That(restored.Version, Is.EqualTo(original.Version));
    Assert.That(restored.SpriteCount, Is.EqualTo(original.SpriteCount));
    Assert.That(restored.IsMulticolor, Is.EqualTo(original.IsMulticolor));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    Assert.That(restored.ExtraHeader, Is.EqualTo(original.ExtraHeader));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var rawData = new byte[SpritePadFile.BytesPerSprite];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new SpritePadFile { Version = 1, SpriteCount = 1, IsMulticolor = false, RawData = rawData };

    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spd");
    try {
      File.WriteAllBytes(tmp, SpritePadWriter.ToBytes(original));
      var restored = SpritePadReader.FromFile(new FileInfo(tmp));

      Assert.That(restored.SpriteCount, Is.EqualTo(original.SpriteCount));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      try { File.Delete(tmp); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream_PreservesData() {
    var rawData = new byte[SpritePadFile.BytesPerSprite];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new SpritePadFile { Version = 1, SpriteCount = 1, IsMulticolor = false, RawData = rawData };
    var bytes = SpritePadWriter.ToBytes(original);

    using var ms = new MemoryStream(bytes);
    var restored = SpritePadReader.FromStream(ms);

    Assert.That(restored.SpriteCount, Is.EqualTo(original.SpriteCount));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidSpritePadV1(int spriteCount, bool multicolor) {
    var rawData = new byte[spriteCount * SpritePadFile.BytesPerSprite];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[SpritePadFile.V1HeaderSize + rawData.Length];
    data[0] = 1; // version
    data[1] = (byte)spriteCount;
    data[2] = (byte)(multicolor ? 1 : 0);
    Array.Copy(rawData, 0, data, SpritePadFile.V1HeaderSize, rawData.Length);
    return data;
  }

  internal static byte[] _BuildValidSpritePadV2(int spriteCount, bool multicolor) {
    var rawData = new byte[spriteCount * SpritePadFile.BytesPerSprite];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[SpritePadFile.V2HeaderSize + rawData.Length];
    data[0] = 2; // version
    data[1] = (byte)spriteCount;
    data[2] = (byte)(multicolor ? 1 : 0);
    data[3] = 0xAB;
    data[4] = 0xCD;
    data[5] = 0xEF;
    Array.Copy(rawData, 0, data, SpritePadFile.V2HeaderSize, rawData.Length);
    return data;
  }
}

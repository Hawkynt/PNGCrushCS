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
public sealed class SpritePadWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpritePadWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_V1_CorrectOutputSize() {
    var rawData = new byte[2 * SpritePadFile.BytesPerSprite];
    var file = new SpritePadFile { Version = 1, SpriteCount = 2, IsMulticolor = false, RawData = rawData };
    var bytes = SpritePadWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(SpritePadFile.V1HeaderSize + rawData.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_V2_CorrectOutputSize() {
    var rawData = new byte[SpritePadFile.BytesPerSprite];
    var extraHeader = new byte[SpritePadFile.V2HeaderSize - SpritePadFile.V1HeaderSize];
    var file = new SpritePadFile { Version = 2, SpriteCount = 1, IsMulticolor = false, RawData = rawData, ExtraHeader = extraHeader };
    var bytes = SpritePadWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(SpritePadFile.V2HeaderSize + rawData.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_V1_VersionByteCorrect() {
    var file = new SpritePadFile { Version = 1, SpriteCount = 1, RawData = new byte[64] };
    var bytes = SpritePadWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_V1_SpriteCountByteCorrect() {
    var file = new SpritePadFile { Version = 1, SpriteCount = 5, RawData = new byte[5 * 64] };
    var bytes = SpritePadWriter.ToBytes(file);

    Assert.That(bytes[1], Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MulticolorFlagSet() {
    var file = new SpritePadFile { Version = 1, SpriteCount = 1, IsMulticolor = true, RawData = new byte[64] };
    var bytes = SpritePadWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MulticolorFlagClear() {
    var file = new SpritePadFile { Version = 1, SpriteCount = 1, IsMulticolor = false, RawData = new byte[64] };
    var bytes = SpritePadWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0));
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

[TestFixture]
public sealed class SpritePadDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsSpd() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".spd"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsSpd() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".spd"));
  }

  [Test]
  [Category("Unit")]
  public void Capabilities_IsIndexedOnly() {
    Assert.That(_GetCapabilities(), Is.EqualTo(FormatCapability.IndexedOnly));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpritePadFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpritePadFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 24, Height = 21, Format = PixelFormat.Indexed8, PixelData = new byte[24 * 21], Palette = new byte[6], PaletteCount = 2 };
    Assert.Throws<NotSupportedException>(() => SpritePadFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_SingleSprite_CorrectDimensions() {
    var rawData = new byte[SpritePadFile.BytesPerSprite];
    var file = new SpritePadFile { Version = 1, SpriteCount = 1, RawData = rawData };
    var image = SpritePadFile.ToRawImage(file);

    Assert.That(image.Width, Is.EqualTo(24));
    Assert.That(image.Height, Is.EqualTo(21));
    Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_MultipleSprites_GridDimensions() {
    var rawData = new byte[4 * SpritePadFile.BytesPerSprite];
    var file = new SpritePadFile { Version = 1, SpriteCount = 4, RawData = rawData };
    var image = SpritePadFile.ToRawImage(file);

    Assert.That(image.Width, Is.EqualTo(4 * 24));
    Assert.That(image.Height, Is.EqualTo(21));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NineSprites_WrapsToTwoRows() {
    var rawData = new byte[9 * SpritePadFile.BytesPerSprite];
    var file = new SpritePadFile { Version = 1, SpriteCount = 9, RawData = rawData };
    var image = SpritePadFile.ToRawImage(file);

    Assert.That(image.Width, Is.EqualTo(8 * 24));
    Assert.That(image.Height, Is.EqualTo(2 * 21));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_MonoSprite_HasCorrectPixels() {
    var rawData = new byte[SpritePadFile.BytesPerSprite];
    rawData[0] = 0xFF; // first byte all 1s = first 8 pixels set
    var file = new SpritePadFile { Version = 1, SpriteCount = 1, IsMulticolor = false, RawData = rawData };
    var image = SpritePadFile.ToRawImage(file);

    for (var x = 0; x < 8; ++x)
      Assert.That(image.PixelData[x], Is.EqualTo(1), $"Pixel at ({x},0) should be 1");

    for (var x = 8; x < 24; ++x)
      Assert.That(image.PixelData[x], Is.EqualTo(0), $"Pixel at ({x},0) should be 0");
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_HasPalette() {
    var rawData = new byte[SpritePadFile.BytesPerSprite];
    var file = new SpritePadFile { Version = 1, SpriteCount = 1, RawData = rawData };
    var image = SpritePadFile.ToRawImage(file);

    Assert.That(image.Palette, Is.Not.Null);
    Assert.That(image.PaletteCount, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void Width_SingleSprite() {
    var file = new SpritePadFile { SpriteCount = 1, RawData = new byte[64] };
    Assert.That(file.Width, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void Height_TwoRowsOfSprites() {
    var file = new SpritePadFile { SpriteCount = 9, RawData = new byte[9 * 64] };
    Assert.That(file.Height, Is.EqualTo(2 * 21));
  }

  [Test]
  [Category("Unit")]
  public void Defaults_VersionIsOne() {
    var file = new SpritePadFile();
    Assert.That(file.Version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Defaults_SpriteCountIsZero() {
    var file = new SpritePadFile();
    Assert.That(file.SpriteCount, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Defaults_RawDataEmpty() {
    var file = new SpritePadFile();
    Assert.That(file.RawData, Is.Empty);
  }

  private static string _GetPrimaryExtension() => _Helper<SpritePadFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<SpritePadFile>.FileExtensions;
  private static FormatCapability _GetCapabilities() => _Helper<SpritePadFile>.Capabilities;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
    public static FormatCapability Capabilities => T.Capabilities;
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

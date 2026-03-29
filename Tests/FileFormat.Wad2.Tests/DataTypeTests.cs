using System;
using System.Linq;
using FileFormat.Core;
using FileFormat.Wad2;

namespace FileFormat.Wad2.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Wad2LumpType_HasExpectedValues() {
    Assert.That((byte)Wad2LumpType.Palette, Is.EqualTo(0x40));
    Assert.That((byte)Wad2LumpType.StatusBar, Is.EqualTo(0x42));
    Assert.That((byte)Wad2LumpType.MipTex, Is.EqualTo(0x44));
    Assert.That((byte)Wad2LumpType.ConsolePic, Is.EqualTo(0x45));

    var values = Enum.GetValues<Wad2LumpType>();
    Assert.That(values, Has.Length.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void Wad2Entry_StructSize_Is32() {
    Assert.That(Wad2Entry.StructSize, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void Wad2Entry_RoundTrip_PreservesAllFields() {
    var original = new Wad2Entry(512, 2048, 2048, 0x44, 0, 0, "MYENTRY");
    var buffer = new byte[Wad2Entry.StructSize];
    original.WriteTo(buffer);

    var restored = Wad2Entry.ReadFrom(buffer);

    Assert.That(restored.FilePos, Is.EqualTo(512));
    Assert.That(restored.DiskSize, Is.EqualTo(2048));
    Assert.That(restored.Size, Is.EqualTo(2048));
    Assert.That(restored.Type, Is.EqualTo(0x44));
    Assert.That(restored.Compression, Is.EqualTo(0));
    Assert.That(restored.Name, Is.EqualTo("MYENTRY"));
  }

  [Test]
  [Category("Unit")]
  public void Wad2Entry_NameTruncatedTo16Chars() {
    var original = new Wad2Entry(0, 0, 0, 0x44, 0, 0, "LONGERTEXNAME01");
    var buffer = new byte[Wad2Entry.StructSize];
    original.WriteTo(buffer);

    var restored = Wad2Entry.ReadFrom(buffer);

    Assert.That(restored.Name, Has.Length.LessThanOrEqualTo(16));
    Assert.That(restored.Name, Is.EqualTo("LONGERTEXNAME01"));
  }

  [Test]
  [Category("Unit")]
  public void Wad2Texture_DefaultValues() {
    var texture = new Wad2Texture();

    Assert.That(texture.Name, Is.EqualTo(""));
    Assert.That(texture.Width, Is.EqualTo(0));
    Assert.That(texture.Height, Is.EqualTo(0));
    Assert.That(texture.PixelData, Is.Empty);
    Assert.That(texture.MipMaps, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void Wad2Header_StructSize_Is12() {
    Assert.That(Wad2Header.StructSize, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void Wad2Header_RoundTrip_PreservesAllFields() {
    var original = new Wad2Header((byte)'W', (byte)'A', (byte)'D', (byte)'2', 5, 2048);
    var buffer = new byte[Wad2Header.StructSize];
    original.WriteTo(buffer);

    var restored = Wad2Header.ReadFrom(buffer);

    Assert.That(restored.Magic1, Is.EqualTo((byte)'W'));
    Assert.That(restored.Magic2, Is.EqualTo((byte)'A'));
    Assert.That(restored.Magic3, Is.EqualTo((byte)'D'));
    Assert.That(restored.Magic4, Is.EqualTo((byte)'2'));
    Assert.That(restored.NumLumps, Is.EqualTo(5));
    Assert.That(restored.DirectoryOffset, Is.EqualTo(2048));
  }

  [Test]
  [Category("Unit")]
  public void Wad2Header_GetFieldMap_HasExpectedEntries() {
    var fields = Wad2Header.GetFieldMap();

    Assert.That(fields, Has.Length.EqualTo(7));
    Assert.That(fields.Any(f => f.Name == "Magic" && f.Offset == 0 && f.Size == 4), Is.True);
    Assert.That(fields.Any(f => f.Name == "Magic1" && f.Offset == 0 && f.Size == 1), Is.True);
    Assert.That(fields.Any(f => f.Name == "NumLumps" && f.Offset == 4 && f.Size == 4), Is.True);
    Assert.That(fields.Any(f => f.Name == "DirectoryOffset" && f.Offset == 8 && f.Size == 4), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_Is768Bytes() {
    Assert.That(Wad2File.DefaultPalette, Has.Length.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_IsGrayscaleRamp() {
    var palette = Wad2File.DefaultPalette;
    for (var i = 0; i < 256; ++i) {
      Assert.That(palette[i * 3], Is.EqualTo((byte)i));
      Assert.That(palette[i * 3 + 1], Is.EqualTo((byte)i));
      Assert.That(palette[i * 3 + 2], Is.EqualTo((byte)i));
    }
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsWad() {
    var extensions = _GetExtensions<Wad2File>();
    Assert.That(extensions, Does.Contain(".wad"));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsWad() {
    var primary = _GetPrimaryExtension<Wad2File>();
    Assert.That(primary, Is.EqualTo(".wad"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Wad2File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_EmptyTextures_ThrowsArgumentException() {
    var file = new Wad2File { Textures = [] };
    Assert.Throws<ArgumentException>(() => Wad2File.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NullImage_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Wad2File.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 16,
      Height = 16,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[16 * 16 * 3]
    };
    Assert.Throws<ArgumentException>(() => Wad2File.FromRawImage(raw));
  }

  private static string[] _GetExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
}

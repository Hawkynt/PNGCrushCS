using System;
using System.IO;
using FileFormat.Wad2;

namespace FileFormat.Wad2.Tests;

[TestFixture]
public sealed class Wad2ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Wad2Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Wad2Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wad"));
    Assert.Throws<FileNotFoundException>(() => Wad2Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Wad2Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => Wad2Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[Wad2Header.StructSize];
    bad[0] = (byte)'W';
    bad[1] = (byte)'A';
    bad[2] = (byte)'D';
    bad[3] = (byte)'3';
    Assert.Throws<InvalidDataException>(() => Wad2Reader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleTexture_ParsesCorrectly() {
    var texture = _CreateTestTexture("BRICK01", 16, 16);
    var bytes = Wad2Writer.ToBytes(new Wad2File { Textures = [texture] });

    var result = Wad2Reader.FromBytes(bytes);

    Assert.That(result.Textures, Has.Count.EqualTo(1));
    Assert.That(result.Textures[0].Name, Is.EqualTo("BRICK01"));
    Assert.That(result.Textures[0].Width, Is.EqualTo(16));
    Assert.That(result.Textures[0].Height, Is.EqualTo(16));
    Assert.That(result.Textures[0].PixelData, Has.Length.EqualTo(16 * 16));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMultiTexture_ParsesAll() {
    var tex1 = _CreateTestTexture("WALL01", 16, 16);
    var tex2 = _CreateTestTexture("FLOOR02", 32, 32);
    var bytes = Wad2Writer.ToBytes(new Wad2File { Textures = [tex1, tex2] });

    var result = Wad2Reader.FromBytes(bytes);

    Assert.That(result.Textures, Has.Count.EqualTo(2));
    Assert.That(result.Textures[0].Name, Is.EqualTo("WALL01"));
    Assert.That(result.Textures[1].Name, Is.EqualTo("FLOOR02"));
    Assert.That(result.Textures[1].Width, Is.EqualTo(32));
    Assert.That(result.Textures[1].Height, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidTexture_ParsesCorrectly() {
    var texture = _CreateTestTexture("STREAM", 16, 16);
    var bytes = Wad2Writer.ToBytes(new Wad2File { Textures = [texture] });

    using var ms = new MemoryStream(bytes);
    var result = Wad2Reader.FromStream(ms);

    Assert.That(result.Textures, Has.Count.EqualTo(1));
    Assert.That(result.Textures[0].Name, Is.EqualTo("STREAM"));
  }

  private static Wad2Texture _CreateTestTexture(string name, int width, int height) {
    var mip0 = new byte[width * height];
    var mip1 = new byte[(width / 2) * (height / 2)];
    var mip2 = new byte[(width / 4) * (height / 4)];
    var mip3 = new byte[(width / 8) * (height / 8)];

    for (var i = 0; i < mip0.Length; ++i)
      mip0[i] = (byte)(i % 256);
    for (var i = 0; i < mip1.Length; ++i)
      mip1[i] = (byte)(i * 2 % 256);
    for (var i = 0; i < mip2.Length; ++i)
      mip2[i] = (byte)(i * 3 % 256);
    for (var i = 0; i < mip3.Length; ++i)
      mip3[i] = (byte)(i * 5 % 256);

    return new Wad2Texture {
      Name = name,
      Width = width,
      Height = height,
      PixelData = mip0,
      MipMaps = [mip1, mip2, mip3]
    };
  }
}

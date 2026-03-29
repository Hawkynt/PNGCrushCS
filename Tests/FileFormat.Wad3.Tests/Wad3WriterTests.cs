using System;
using System.Buffers.Binary;
using FileFormat.Wad3;

namespace FileFormat.Wad3.Tests;

[TestFixture]
public sealed class Wad3WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Wad3Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithWAD3() {
    var bytes = Wad3Writer.ToBytes(new Wad3File { Textures = [] });

    Assert.That(bytes[0], Is.EqualTo((byte)'W'));
    Assert.That(bytes[1], Is.EqualTo((byte)'A'));
    Assert.That(bytes[2], Is.EqualTo((byte)'D'));
    Assert.That(bytes[3], Is.EqualTo((byte)'3'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DirectoryOffsetCorrect() {
    var texture = _CreateTestTexture("TEST", 16, 16);
    var bytes = Wad3Writer.ToBytes(new Wad3File { Textures = [texture] });

    var directoryOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));

    // Header(12) + lump size
    var expectedLumpSize = 40 + (16 * 16) + (8 * 8) + (4 * 4) + (2 * 2) + 2 + 768;
    Assert.That(directoryOffset, Is.EqualTo(Wad3Header.StructSize + expectedLumpSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TextureDataPreserved() {
    var texture = _CreateTestTexture("DATA", 16, 16);
    var bytes = Wad3Writer.ToBytes(new Wad3File { Textures = [texture] });
    var restored = Wad3Reader.FromBytes(bytes);

    Assert.That(restored.Textures[0].PixelData, Is.EqualTo(texture.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PalettePreserved() {
    var texture = _CreateTestTexture("PAL", 16, 16);
    var bytes = Wad3Writer.ToBytes(new Wad3File { Textures = [texture] });
    var restored = Wad3Reader.FromBytes(bytes);

    Assert.That(restored.Textures[0].Palette, Is.EqualTo(texture.Palette));
  }

  private static Wad3Texture _CreateTestTexture(string name, int width, int height) {
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

    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 2 % 256);
    }

    return new Wad3Texture {
      Name = name,
      Width = width,
      Height = height,
      PixelData = mip0,
      MipMaps = [mip1, mip2, mip3],
      Palette = palette
    };
  }
}

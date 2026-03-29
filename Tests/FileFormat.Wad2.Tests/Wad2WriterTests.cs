using System;
using System.Buffers.Binary;
using FileFormat.Wad2;

namespace FileFormat.Wad2.Tests;

[TestFixture]
public sealed class Wad2WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Wad2Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithWAD2() {
    var bytes = Wad2Writer.ToBytes(new Wad2File { Textures = [] });

    Assert.That(bytes[0], Is.EqualTo((byte)'W'));
    Assert.That(bytes[1], Is.EqualTo((byte)'A'));
    Assert.That(bytes[2], Is.EqualTo((byte)'D'));
    Assert.That(bytes[3], Is.EqualTo((byte)'2'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DirectoryOffsetCorrect() {
    var texture = _CreateTestTexture("TEST", 16, 16);
    var bytes = Wad2Writer.ToBytes(new Wad2File { Textures = [texture] });

    var directoryOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));

    // Header(12) + MipTex header(40) + mip0(256) + mip1(64) + mip2(16) + mip3(4) = 12 + 380 = 392
    var expectedLumpSize = 40 + (16 * 16) + (8 * 8) + (4 * 4) + (2 * 2);
    Assert.That(directoryOffset, Is.EqualTo(Wad2Header.StructSize + expectedLumpSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TextureDataPreserved() {
    var texture = _CreateTestTexture("DATA", 16, 16);
    var bytes = Wad2Writer.ToBytes(new Wad2File { Textures = [texture] });
    var restored = Wad2Reader.FromBytes(bytes);

    Assert.That(restored.Textures[0].PixelData, Is.EqualTo(texture.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NoPaletteEmbedded() {
    var texture = _CreateTestTexture("NOPAL", 16, 16);
    var bytes = Wad2Writer.ToBytes(new Wad2File { Textures = [texture] });

    // WAD2 has no palette; total size = header(12) + lumpSize + directory(32)
    var expectedLumpSize = 40 + (16 * 16) + (8 * 8) + (4 * 4) + (2 * 2);
    var expectedTotal = Wad2Header.StructSize + expectedLumpSize + Wad2Entry.StructSize;
    Assert.That(bytes, Has.Length.EqualTo(expectedTotal));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NumLumpsFieldCorrect() {
    var tex1 = _CreateTestTexture("A", 16, 16);
    var tex2 = _CreateTestTexture("B", 16, 16);
    var bytes = Wad2Writer.ToBytes(new Wad2File { Textures = [tex1, tex2] });

    var numLumps = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
    Assert.That(numLumps, Is.EqualTo(2));
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

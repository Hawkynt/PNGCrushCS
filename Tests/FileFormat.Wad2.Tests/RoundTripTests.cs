using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Wad2;

namespace FileFormat.Wad2.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleTexture() {
    var texture = _CreateTestTexture("SINGLE", 16, 16);
    var original = new Wad2File { Textures = [texture] };

    var bytes = Wad2Writer.ToBytes(original);
    var restored = Wad2Reader.FromBytes(bytes);

    Assert.That(restored.Textures, Has.Count.EqualTo(1));
    Assert.That(restored.Textures[0].Name, Is.EqualTo("SINGLE"));
    Assert.That(restored.Textures[0].Width, Is.EqualTo(16));
    Assert.That(restored.Textures[0].Height, Is.EqualTo(16));
    Assert.That(restored.Textures[0].PixelData, Is.EqualTo(texture.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiTexture() {
    var tex1 = _CreateTestTexture("TEX_A", 16, 16);
    var tex2 = _CreateTestTexture("TEX_B", 32, 32);
    var original = new Wad2File { Textures = [tex1, tex2] };

    var bytes = Wad2Writer.ToBytes(original);
    var restored = Wad2Reader.FromBytes(bytes);

    Assert.That(restored.Textures, Has.Count.EqualTo(2));
    Assert.That(restored.Textures[0].Name, Is.EqualTo("TEX_A"));
    Assert.That(restored.Textures[0].PixelData, Is.EqualTo(tex1.PixelData));
    Assert.That(restored.Textures[1].Name, Is.EqualTo("TEX_B"));
    Assert.That(restored.Textures[1].Width, Is.EqualTo(32));
    Assert.That(restored.Textures[1].Height, Is.EqualTo(32));
    Assert.That(restored.Textures[1].PixelData, Is.EqualTo(tex2.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithMipmaps() {
    var texture = _CreateTestTexture("MIPMAPS", 32, 32);
    var original = new Wad2File { Textures = [texture] };

    var bytes = Wad2Writer.ToBytes(original);
    var restored = Wad2Reader.FromBytes(bytes);

    Assert.That(restored.Textures[0].MipMaps, Is.Not.Null);
    Assert.That(restored.Textures[0].MipMaps!, Has.Length.EqualTo(3));
    Assert.That(restored.Textures[0].MipMaps![0], Is.EqualTo(texture.MipMaps![0]));
    Assert.That(restored.Textures[0].MipMaps![1], Is.EqualTo(texture.MipMaps![1]));
    Assert.That(restored.Textures[0].MipMaps![2], Is.EqualTo(texture.MipMaps![2]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wad");
    try {
      var texture = _CreateTestTexture("FILERT", 16, 16);
      var original = new Wad2File { Textures = [texture] };

      var bytes = Wad2Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = Wad2Reader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Textures, Has.Count.EqualTo(1));
      Assert.That(restored.Textures[0].Name, Is.EqualTo("FILERT"));
      Assert.That(restored.Textures[0].PixelData, Is.EqualTo(texture.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var texture = _CreateTestTexture("RAWIMG", 16, 16);
    var original = new Wad2File { Textures = [texture] };

    var raw = Wad2File.ToRawImage(original);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(16));
    Assert.That(raw.Height, Is.EqualTo(16));
    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(256));

    var restored = Wad2File.FromRawImage(raw);
    Assert.That(restored.Textures, Has.Count.EqualTo(1));
    Assert.That(restored.Textures[0].PixelData, Is.EqualTo(texture.PixelData));
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

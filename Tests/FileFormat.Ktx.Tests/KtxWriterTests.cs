using System;
using FileFormat.Ktx;

namespace FileFormat.Ktx.Tests;

[TestFixture]
public sealed class KtxWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Ktx1_StartsWithKtx1Identifier() {
    var file = new KtxFile {
      Width = 2,
      Height = 2,
      Version = KtxVersion.Ktx1,
      MipmapCount = 1,
      Faces = 1,
      GlType = 0x1401,
      GlTypeSize = 1,
      GlFormat = 0x1908,
      GlInternalFormat = 0x8058,
      GlBaseInternalFormat = 0x1908,
      MipLevels = [new KtxMipLevel { Width = 2, Height = 2, Data = new byte[16] }]
    };

    var bytes = KtxWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(KtxHeader.IdentifierSize));
    for (var i = 0; i < KtxHeader.Identifier.Length; ++i)
      Assert.That(bytes[i], Is.EqualTo(KtxHeader.Identifier[i]), $"Identifier mismatch at byte {i}");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Ktx2_StartsWithKtx2Identifier() {
    var file = new KtxFile {
      Width = 2,
      Height = 2,
      Version = KtxVersion.Ktx2,
      MipmapCount = 1,
      Faces = 1,
      VkFormat = 37, // VK_FORMAT_R8G8B8A8_UNORM
      TypeSize = 1,
      MipLevels = [new KtxMipLevel { Width = 2, Height = 2, Data = new byte[16] }]
    };

    var bytes = KtxWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(Ktx2Header.IdentifierSize));
    for (var i = 0; i < Ktx2Header.Identifier.Length; ++i)
      Assert.That(bytes[i], Is.EqualTo(Ktx2Header.Identifier[i]), $"Identifier mismatch at byte {i}");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Ktx1_ContainsDimensions() {
    var file = new KtxFile {
      Width = 128,
      Height = 64,
      Version = KtxVersion.Ktx1,
      MipmapCount = 1,
      Faces = 1,
      GlType = 0x1401,
      GlTypeSize = 1,
      GlFormat = 0x1908,
      GlInternalFormat = 0x8058,
      GlBaseInternalFormat = 0x1908,
      MipLevels = [new KtxMipLevel { Width = 128, Height = 64, Data = new byte[128 * 64 * 4] }]
    };

    var bytes = KtxWriter.ToBytes(file);

    // Width at offset 36, Height at offset 40 (LE)
    var width = BitConverter.ToInt32(bytes, 36);
    var height = BitConverter.ToInt32(bytes, 40);
    Assert.That(width, Is.EqualTo(128));
    Assert.That(height, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Ktx2_ContainsDimensions() {
    var file = new KtxFile {
      Width = 256,
      Height = 128,
      Version = KtxVersion.Ktx2,
      MipmapCount = 1,
      Faces = 1,
      VkFormat = 37,
      TypeSize = 1,
      MipLevels = [new KtxMipLevel { Width = 256, Height = 128, Data = new byte[256 * 128 * 4] }]
    };

    var bytes = KtxWriter.ToBytes(file);

    // Width at offset 20, Height at offset 24 (LE)
    var width = BitConverter.ToInt32(bytes, 20);
    var height = BitConverter.ToInt32(bytes, 24);
    Assert.That(width, Is.EqualTo(256));
    Assert.That(height, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KtxWriter.ToBytes(null!));
  }
}

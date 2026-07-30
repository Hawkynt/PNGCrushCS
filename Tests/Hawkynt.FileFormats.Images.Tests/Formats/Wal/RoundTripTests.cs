using System;
using System.IO;
using FileFormat.Wal;

namespace FileFormat.Wal.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_NoMips_PreservesData() {
    var pixelData = new byte[4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new WalFile {
      Name = "test/wall01",
      Width = 4,
      Height = 4,
      NextFrameName = "",
      Flags = 0x10,
      Contents = 0x20,
      Value = 0x30,
      PixelData = pixelData
    };

    var bytes = WalWriter.ToBytes(original);
    var restored = WalReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Name, Is.EqualTo(original.Name));
    Assert.That(restored.Flags, Is.EqualTo(original.Flags));
    Assert.That(restored.Contents, Is.EqualTo(original.Contents));
    Assert.That(restored.Value, Is.EqualTo(original.Value));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.MipMaps, Is.Null);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithMips_PreservesAllLevels() {
    var mip0 = new byte[8 * 8];
    var mip1 = new byte[4 * 4];
    var mip2 = new byte[2 * 2];
    var mip3 = new byte[1 * 1];

    for (var i = 0; i < mip0.Length; ++i)
      mip0[i] = (byte)(i % 256);
    for (var i = 0; i < mip1.Length; ++i)
      mip1[i] = (byte)(i * 3 % 256);
    for (var i = 0; i < mip2.Length; ++i)
      mip2[i] = (byte)(i * 7 % 256);
    mip3[0] = 42;

    var original = new WalFile {
      Name = "textures/brick",
      Width = 8,
      Height = 8,
      NextFrameName = "textures/brick2",
      PixelData = mip0,
      MipMaps = [mip1, mip2, mip3]
    };

    var bytes = WalWriter.ToBytes(original);
    var restored = WalReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(mip0));
    Assert.That(restored.MipMaps, Is.Not.Null);
    Assert.That(restored.MipMaps!, Has.Length.EqualTo(3));
    Assert.That(restored.MipMaps![0], Is.EqualTo(mip1));
    Assert.That(restored.MipMaps[1], Is.EqualTo(mip2));
    Assert.That(restored.MipMaps[2], Is.EqualTo(mip3));
    Assert.That(restored.NextFrameName, Is.EqualTo("textures/brick2"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LongName_Truncated() {
    var longName = new string('A', 50);

    var original = new WalFile {
      Name = longName,
      Width = 4,
      Height = 4,
      PixelData = new byte[16]
    };

    var bytes = WalWriter.ToBytes(original);
    var restored = WalReader.FromBytes(bytes);

    Assert.That(restored.Name.Length, Is.LessThanOrEqualTo(32));
    Assert.That(restored.Name, Is.EqualTo(longName[..32]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var pixelData = new byte[4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new WalFile {
      Name = "file_test",
      Width = 4,
      Height = 4,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wal");
    try {
      var bytes = WalWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = WalReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Name, Is.EqualTo(original.Name));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}

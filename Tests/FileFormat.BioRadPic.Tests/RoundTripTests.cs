using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.BioRadPic;

namespace FileFormat.BioRadPic.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_8BitGrayscale() {
    var pixelData = new byte[16 * 8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new BioRadPicFile {
      Width = 16,
      Height = 8,
      ByteFormat = true,
      PixelData = pixelData
    };

    var bytes = BioRadPicWriter.ToBytes(original);
    var restored = BioRadPicReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ByteFormat, Is.EqualTo(original.ByteFormat));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_16BitGrayscale() {
    var width = 8;
    var height = 4;
    var pixelData = new byte[width * height * 2];
    for (var i = 0; i < width * height; ++i)
      BinaryPrimitives.WriteUInt16LittleEndian(pixelData.AsSpan(i * 2), (ushort)(i * 100));

    var original = new BioRadPicFile {
      Width = width,
      Height = height,
      ByteFormat = false,
      PixelData = pixelData
    };

    var bytes = BioRadPicWriter.ToBytes(original);
    var restored = BioRadPicReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ByteFormat, Is.EqualTo(original.ByteFormat));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[10 * 10];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new BioRadPicFile {
      Width = 10,
      Height = 10,
      ByteFormat = true,
      Name = "microscope",
      Lens = 60,
      MagFactor = 2.0f,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pic");
    try {
      var bytes = BioRadPicWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = BioRadPicReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.ByteFormat, Is.EqualTo(original.ByteFormat));
      Assert.That(restored.Name, Is.EqualTo(original.Name));
      Assert.That(restored.Lens, Is.EqualTo(original.Lens));
      Assert.That(restored.MagFactor, Is.EqualTo(original.MagFactor));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NamePreservation() {
    var original = new BioRadPicFile {
      Width = 2,
      Height = 2,
      ByteFormat = true,
      Name = "confocal_z_stack",
      PixelData = new byte[4]
    };

    var bytes = BioRadPicWriter.ToBytes(original);
    var restored = BioRadPicReader.FromBytes(bytes);

    Assert.That(restored.Name, Is.EqualTo(original.Name));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllHeaderFields() {
    var original = new BioRadPicFile {
      Width = 64,
      Height = 32,
      NumImages = 5,
      ByteFormat = true,
      Name = "sample",
      Lens = 100,
      MagFactor = 1.5f,
      Ramp1Min = -200,
      Ramp1Max = 4095,
      Ramp2Min = -50,
      Ramp2Max = 2000,
      Color1 = 3,
      Color2 = 7,
      Merged = 1,
      Edited = 1,
      Notes = 42,
      PixelData = new byte[64 * 32]
    };

    var bytes = BioRadPicWriter.ToBytes(original);
    var restored = BioRadPicReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.NumImages, Is.EqualTo(original.NumImages));
    Assert.That(restored.ByteFormat, Is.EqualTo(original.ByteFormat));
    Assert.That(restored.Name, Is.EqualTo(original.Name));
    Assert.That(restored.Lens, Is.EqualTo(original.Lens));
    Assert.That(restored.MagFactor, Is.EqualTo(original.MagFactor));
    Assert.That(restored.Ramp1Min, Is.EqualTo(original.Ramp1Min));
    Assert.That(restored.Ramp1Max, Is.EqualTo(original.Ramp1Max));
    Assert.That(restored.Ramp2Min, Is.EqualTo(original.Ramp2Min));
    Assert.That(restored.Ramp2Max, Is.EqualTo(original.Ramp2Max));
    Assert.That(restored.Color1, Is.EqualTo(original.Color1));
    Assert.That(restored.Color2, Is.EqualTo(original.Color2));
    Assert.That(restored.Merged, Is.EqualTo(original.Merged));
    Assert.That(restored.Edited, Is.EqualTo(original.Edited));
    Assert.That(restored.Notes, Is.EqualTo(original.Notes));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_8Bit() {
    var original = new BioRadPicFile {
      Width = 4,
      Height = 4,
      ByteFormat = true,
      PixelData = new byte[16]
    };

    var bytes = BioRadPicWriter.ToBytes(original);
    var restored = BioRadPicReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}

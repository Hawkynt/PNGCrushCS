using System;
using System.IO;
using FileFormat.Nifti;

namespace FileFormat.Nifti.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_UInt8_2D() {
    var pixelData = new byte[] { 10, 20, 30, 40, 50, 60 };
    var original = new NiftiFile {
      Width = 3,
      Height = 2,
      Depth = 1,
      Datatype = NiftiDataType.UInt8,
      Bitpix = 8,
      VoxOffset = 352f,
      PixelData = pixelData
    };

    var bytes = NiftiWriter.ToBytes(original);
    var restored = NiftiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(1));
    Assert.That(restored.Datatype, Is.EqualTo(NiftiDataType.UInt8));
    Assert.That(restored.Bitpix, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Int16() {
    var pixelData = new byte[4 * 4 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new NiftiFile {
      Width = 4,
      Height = 4,
      Depth = 1,
      Datatype = NiftiDataType.Int16,
      Bitpix = 16,
      VoxOffset = 352f,
      PixelData = pixelData
    };

    var bytes = NiftiWriter.ToBytes(original);
    var restored = NiftiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Datatype, Is.EqualTo(NiftiDataType.Int16));
    Assert.That(restored.Bitpix, Is.EqualTo(16));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Float32() {
    var pixelData = new byte[2 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new NiftiFile {
      Width = 2,
      Height = 2,
      Depth = 1,
      Datatype = NiftiDataType.Float32,
      Bitpix = 32,
      VoxOffset = 352f,
      PixelData = pixelData
    };

    var bytes = NiftiWriter.ToBytes(original);
    var restored = NiftiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Datatype, Is.EqualTo(NiftiDataType.Float32));
    Assert.That(restored.Bitpix, Is.EqualTo(32));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24() {
    var pixelData = new byte[3 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new NiftiFile {
      Width = 3,
      Height = 2,
      Depth = 1,
      Datatype = NiftiDataType.Rgb24,
      Bitpix = 24,
      VoxOffset = 352f,
      PixelData = pixelData
    };

    var bytes = NiftiWriter.ToBytes(original);
    var restored = NiftiReader.FromBytes(bytes);

    Assert.That(restored.Datatype, Is.EqualTo(NiftiDataType.Rgb24));
    Assert.That(restored.Bitpix, Is.EqualTo(24));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_3D() {
    var pixelData = new byte[4 * 3 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 5 % 256);

    var original = new NiftiFile {
      Width = 4,
      Height = 3,
      Depth = 2,
      Datatype = NiftiDataType.UInt8,
      Bitpix = 8,
      VoxOffset = 352f,
      PixelData = pixelData
    };

    var bytes = NiftiWriter.ToBytes(original);
    var restored = NiftiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.Depth, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nii");
    try {
      var pixelData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
      var original = new NiftiFile {
        Width = 4,
        Height = 2,
        Depth = 1,
        Datatype = NiftiDataType.UInt8,
        Bitpix = 8,
        VoxOffset = 352f,
        PixelData = pixelData
      };

      var bytes = NiftiWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = NiftiReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DescriptionPreserved() {
    var original = new NiftiFile {
      Width = 1,
      Height = 1,
      Depth = 1,
      Datatype = NiftiDataType.UInt8,
      Bitpix = 8,
      VoxOffset = 352f,
      Description = "Test brain scan",
      PixelData = [42]
    };

    var bytes = NiftiWriter.ToBytes(original);
    var restored = NiftiReader.FromBytes(bytes);

    Assert.That(restored.Description, Is.EqualTo("Test brain scan"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SclSlopeInterPreserved() {
    var original = new NiftiFile {
      Width = 1,
      Height = 1,
      Depth = 1,
      Datatype = NiftiDataType.Float32,
      Bitpix = 32,
      SclSlope = 2.5f,
      SclInter = -10.0f,
      VoxOffset = 352f,
      PixelData = new byte[4]
    };

    var bytes = NiftiWriter.ToBytes(original);
    var restored = NiftiReader.FromBytes(bytes);

    Assert.That(restored.SclSlope, Is.EqualTo(2.5f));
    Assert.That(restored.SclInter, Is.EqualTo(-10.0f));
  }
}

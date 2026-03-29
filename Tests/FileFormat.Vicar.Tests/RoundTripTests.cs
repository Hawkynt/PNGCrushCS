using System;
using System.IO;
using FileFormat.Vicar;

namespace FileFormat.Vicar.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Byte() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new VicarFile {
      Width = width,
      Height = height,
      Bands = 1,
      PixelType = VicarPixelType.Byte,
      Organization = VicarOrganization.Bsq,
      PixelData = pixelData
    };

    var bytes = VicarWriter.ToBytes(original);
    var restored = VicarReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(original.Bands));
    Assert.That(restored.PixelType, Is.EqualTo(original.PixelType));
    Assert.That(restored.Organization, Is.EqualTo(original.Organization));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Half() {
    var width = 4;
    var height = 2;
    var bytesPerPixel = 2;
    var pixelData = new byte[width * height * bytesPerPixel];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new VicarFile {
      Width = width,
      Height = height,
      Bands = 1,
      PixelType = VicarPixelType.Half,
      Organization = VicarOrganization.Bsq,
      PixelData = pixelData
    };

    var bytes = VicarWriter.ToBytes(original);
    var restored = VicarReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelType, Is.EqualTo(VicarPixelType.Half));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Real() {
    var width = 2;
    var height = 2;
    var bytesPerPixel = 4;
    var pixelData = new byte[width * height * bytesPerPixel];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new VicarFile {
      Width = width,
      Height = height,
      Bands = 1,
      PixelType = VicarPixelType.Real,
      Organization = VicarOrganization.Bsq,
      PixelData = pixelData
    };

    var bytes = VicarWriter.ToBytes(original);
    var restored = VicarReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelType, Is.EqualTo(VicarPixelType.Real));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiBand() {
    var width = 4;
    var height = 3;
    var bands = 3;
    var pixelData = new byte[width * height * bands];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new VicarFile {
      Width = width,
      Height = height,
      Bands = bands,
      PixelType = VicarPixelType.Byte,
      Organization = VicarOrganization.Bip,
      PixelData = pixelData
    };

    var bytes = VicarWriter.ToBytes(original);
    var restored = VicarReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(bands));
    Assert.That(restored.Organization, Is.EqualTo(VicarOrganization.Bip));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Doub() {
    var width = 2;
    var height = 1;
    var bytesPerPixel = 8;
    var pixelData = new byte[width * height * bytesPerPixel];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 23 % 256);

    var original = new VicarFile {
      Width = width,
      Height = height,
      Bands = 1,
      PixelType = VicarPixelType.Doub,
      Organization = VicarOrganization.Bsq,
      PixelData = pixelData
    };

    var bytes = VicarWriter.ToBytes(original);
    var restored = VicarReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelType, Is.EqualTo(VicarPixelType.Doub));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vic");
    try {
      var original = new VicarFile {
        Width = 3,
        Height = 2,
        Bands = 1,
        PixelType = VicarPixelType.Byte,
        Organization = VicarOrganization.Bsq,
        PixelData = [0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE]
      };

      var bytes = VicarWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = VicarReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}

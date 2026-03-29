using System;
using System.IO;
using FileFormat.FaceSaver;
using FileFormat.Core;

namespace FileFormat.FaceSaver.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_2x2_PreservesPixelData() {
    var original = new FaceSaverFile {
      Width = 2, Height = 2,
      PixelData = [0x10, 0x20, 0x30, 0x40],
    };

    var bytes = FaceSaverWriter.ToBytes(original);
    var restored = FaceSaverReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelData, Has.Length.EqualTo(4));
    });
    for (var i = 0; i < original.PixelData.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(original.PixelData[i]), $"pixel[{i}]");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new FaceSaverFile {
      Width = 4, Height = 4,
      PixelData = new byte[16],
    };

    var bytes = FaceSaverWriter.ToBytes(original);
    var restored = FaceSaverReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMax() {
    var pixels = new byte[9];
    Array.Fill(pixels, (byte)0xFF);
    var original = new FaceSaverFile {
      Width = 3, Height = 3,
      PixelData = pixels,
    };

    var bytes = FaceSaverWriter.ToBytes(original);
    var restored = FaceSaverReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.All.EqualTo(0xFF));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_HeaderFieldsPreserved() {
    var original = new FaceSaverFile {
      Width = 2, Height = 2,
      FirstName = "Alice",
      LastName = "Bob",
      Email = "alice@example.org",
      Company = "TestCorp",
      PixelData = new byte[4],
    };

    var bytes = FaceSaverWriter.ToBytes(original);
    var restored = FaceSaverReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.FirstName, Is.EqualTo("Alice"));
      Assert.That(restored.LastName, Is.EqualTo("Bob"));
      Assert.That(restored.Email, Is.EqualTo("alice@example.org"));
      Assert.That(restored.Company, Is.EqualTo("TestCorp"));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonSquarePixels() {
    var original = new FaceSaverFile {
      Width = 108, Height = 96,
      ImageWidth = 96, ImageHeight = 96,
      PixelData = new byte[108 * 96],
    };

    var bytes = FaceSaverWriter.ToBytes(original);
    var restored = FaceSaverReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(108));
      Assert.That(restored.Height, Is.EqualTo(96));
      Assert.That(restored.ImageWidth, Is.EqualTo(96));
      Assert.That(restored.ImageHeight, Is.EqualTo(96));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new FaceSaverFile {
      Width = 3, Height = 2,
      PixelData = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60],
    };

    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".face");
    try {
      File.WriteAllBytes(tmp, FaceSaverWriter.ToBytes(original));
      var restored = FaceSaverReader.FromFile(new FileInfo(tmp));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(3));
        Assert.That(restored.Height, Is.EqualTo(2));
        Assert.That(restored.PixelData[0], Is.EqualTo(0x10));
        Assert.That(restored.PixelData[5], Is.EqualTo(0x60));
      });
    } finally {
      try { File.Delete(tmp); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixels = new byte[4 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17);

    var original = new FaceSaverFile {
      Width = 4, Height = 3,
      PixelData = pixels,
    };

    var raw = FaceSaverFile.ToRawImage(original);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));

    var restored = FaceSaverFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.Height, Is.EqualTo(3));
      Assert.That(restored.PixelData, Has.Length.EqualTo(12));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient() {
    var pixels = new byte[256];
    for (var i = 0; i < 256; ++i)
      pixels[i] = (byte)i;

    var original = new FaceSaverFile {
      Width = 16, Height = 16,
      PixelData = pixels,
    };

    var bytes = FaceSaverWriter.ToBytes(original);
    var restored = FaceSaverReader.FromBytes(bytes);

    for (var i = 0; i < pixels.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixels[i]), $"pixel[{i}]");
  }
}

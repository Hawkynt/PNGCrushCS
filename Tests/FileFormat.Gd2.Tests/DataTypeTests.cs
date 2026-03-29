using System;
using FileFormat.Gd2;
using FileFormat.Core;

namespace FileFormat.Gd2.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Gd2File_DefaultPixelData_IsEmptyArray() {
    var file = new Gd2File();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Gd2File_DefaultVersion_Is2() {
    var file = new Gd2File();
    Assert.That(file.Version, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void Gd2File_DefaultFormat_Is1() {
    var file = new Gd2File();
    Assert.That(file.Format, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Gd2File_DefaultWidth_IsZero() {
    var file = new Gd2File();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Gd2File_DefaultHeight_IsZero() {
    var file = new Gd2File();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Gd2File_DefaultChunkSize_IsZero() {
    var file = new Gd2File();
    Assert.That(file.ChunkSize, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Gd2File_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0, 255, 128, 64 };
    var file = new Gd2File {
      Width = 1,
      Height = 1,
      Version = 3,
      ChunkSize = 1,
      Format = 1,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.Version, Is.EqualTo(3));
    Assert.That(file.ChunkSize, Is.EqualTo(1));
    Assert.That(file.Format, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void Gd2File_HeaderSize_Is18() {
    Assert.That(Gd2File.HeaderSize, Is.EqualTo(18));
  }

  [Test]
  [Category("Unit")]
  public void Gd2File_Signature_IsGd2Null() {
    var sig = Gd2File.Signature;
    Assert.That(sig.Length, Is.EqualTo(4));
    Assert.That(sig[0], Is.EqualTo(0x67)); // 'g'
    Assert.That(sig[1], Is.EqualTo(0x64)); // 'd'
    Assert.That(sig[2], Is.EqualTo(0x32)); // '2'
    Assert.That(sig[3], Is.EqualTo(0x00)); // '\0'
  }

  [Test]
  [Category("Unit")]
  public void Gd2File_PrimaryExtension_IsGd2() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".gd2"));
  }

  [Test]
  [Category("Unit")]
  public void Gd2File_FileExtensions_ContainsGd2() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts, Does.Contain(".gd2"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => Gd2File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgba32() {
    var file = new Gd2File {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 4],
    };

    var raw = Gd2File.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.PixelData.Length, Is.EqualTo(2 * 2 * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_OpaquePixel_Alpha255() {
    var pixels = new byte[4];
    pixels[0] = 0;    // GD2 alpha 0 = opaque
    pixels[1] = 0xFF;
    pixels[2] = 0x80;
    pixels[3] = 0x40;

    var file = new Gd2File {
      Width = 1,
      Height = 1,
      PixelData = pixels,
    };

    var raw = Gd2File.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(0xFF)); // R
    Assert.That(raw.PixelData[1], Is.EqualTo(0x80)); // G
    Assert.That(raw.PixelData[2], Is.EqualTo(0x40)); // B
    Assert.That(raw.PixelData[3], Is.EqualTo(255));   // A
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_TransparentPixel_Alpha0() {
    var pixels = new byte[4];
    pixels[0] = 127;  // GD2 alpha 127 = fully transparent
    pixels[1] = 0x10;
    pixels[2] = 0x20;
    pixels[3] = 0x30;

    var file = new Gd2File {
      Width = 1,
      Height = 1,
      PixelData = pixels,
    };

    var raw = Gd2File.ToRawImage(file);

    Assert.That(raw.PixelData[3], Is.EqualTo(0)); // fully transparent
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => Gd2File.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[3],
    };
    Assert.Throws<ArgumentException>(() => Gd2File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SetsChunkSizeToMax() {
    var raw = new RawImage {
      Width = 3,
      Height = 5,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[3 * 5 * 4],
    };

    var file = Gd2File.FromRawImage(raw);

    Assert.That(file.ChunkSize, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ConvertsAlpha_OpaqueToZero() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [0xAA, 0xBB, 0xCC, 255],
    };

    var file = Gd2File.FromRawImage(raw);

    Assert.That(file.PixelData[0], Is.EqualTo(0)); // GD2 alpha 0 = opaque
    Assert.That(file.PixelData[1], Is.EqualTo(0xAA));
    Assert.That(file.PixelData[2], Is.EqualTo(0xBB));
    Assert.That(file.PixelData[3], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ConvertsAlpha_TransparentTo127() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [0x00, 0x00, 0x00, 0],
    };

    var file = Gd2File.FromRawImage(raw);

    Assert.That(file.PixelData[0], Is.EqualTo(127)); // GD2 alpha 127 = transparent
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var rgba = new byte[] { 255, 0, 0, 255 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = rgba,
    };

    var file = Gd2File.FromRawImage(raw);
    rgba[0] = 0;

    // Conversion creates new array; mutating input should not affect file
    Assert.That(file.PixelData.Length, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0, 0xFF, 0x80, 0x40 };
    var file = new Gd2File {
      Width = 1,
      Height = 1,
      PixelData = pixels,
    };

    var raw = Gd2File.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
  }

  private static T _GetStaticProperty<T>(string name) {
    var map = typeof(Gd2File).GetInterfaceMap(typeof(IImageFileFormat<Gd2File>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}

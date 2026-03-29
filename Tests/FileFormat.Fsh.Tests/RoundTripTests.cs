using System;
using System.IO;
using FileFormat.Fsh;

namespace FileFormat.Fsh.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleArgb8888Entry() {
    var pixels = new byte[4 * 4 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);

    var original = new FshFile {
      DirectoryId = "GIMX",
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Argb8888,
          Width = 4,
          Height = 4,
          PixelData = pixels,
          CenterX = 2,
          CenterY = 2,
        }
      ]
    };

    var bytes = FshWriter.ToBytes(original);
    var restored = FshReader.FromBytes(bytes);

    Assert.That(restored.DirectoryId, Is.EqualTo("GIMX"));
    Assert.That(restored.Entries.Count, Is.EqualTo(1));
    Assert.That(restored.Entries[0].Tag, Is.EqualTo("img0"));
    Assert.That(restored.Entries[0].RecordCode, Is.EqualTo(FshRecordCode.Argb8888));
    Assert.That(restored.Entries[0].Width, Is.EqualTo(4));
    Assert.That(restored.Entries[0].Height, Is.EqualTo(4));
    Assert.That(restored.Entries[0].PixelData, Is.EqualTo(pixels));
    Assert.That(restored.Entries[0].CenterX, Is.EqualTo(2));
    Assert.That(restored.Entries[0].CenterY, Is.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb888Entry() {
    var pixels = new byte[8 * 4 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    var original = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "rgb0",
          RecordCode = FshRecordCode.Rgb888,
          Width = 8,
          Height = 4,
          PixelData = pixels,
        }
      ]
    };

    var bytes = FshWriter.ToBytes(original);
    var restored = FshReader.FromBytes(bytes);

    Assert.That(restored.Entries[0].RecordCode, Is.EqualTo(FshRecordCode.Rgb888));
    Assert.That(restored.Entries[0].Width, Is.EqualTo(8));
    Assert.That(restored.Entries[0].Height, Is.EqualTo(4));
    Assert.That(restored.Entries[0].PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb565Entry() {
    var pixels = new byte[4 * 4 * 2];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 11 % 256);

    var original = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "r565",
          RecordCode = FshRecordCode.Rgb565,
          Width = 4,
          Height = 4,
          PixelData = pixels,
        }
      ]
    };

    var bytes = FshWriter.ToBytes(original);
    var restored = FshReader.FromBytes(bytes);

    Assert.That(restored.Entries[0].RecordCode, Is.EqualTo(FshRecordCode.Rgb565));
    Assert.That(restored.Entries[0].PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Indexed8Entry() {
    var palette = new byte[1024];
    for (var i = 0; i < 256; ++i) {
      palette[i * 4] = (byte)i;
      palette[i * 4 + 1] = (byte)(255 - i);
      palette[i * 4 + 2] = (byte)(i / 2);
      palette[i * 4 + 3] = 0xFF;
    }

    var indices = new byte[8 * 8];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)(i % 256);

    var original = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "pal0",
          RecordCode = FshRecordCode.Indexed8,
          Width = 8,
          Height = 8,
          PixelData = indices,
          Palette = palette
        }
      ]
    };

    var bytes = FshWriter.ToBytes(original);
    var restored = FshReader.FromBytes(bytes);

    Assert.That(restored.Entries[0].RecordCode, Is.EqualTo(FshRecordCode.Indexed8));
    Assert.That(restored.Entries[0].Palette, Is.EqualTo(palette));
    Assert.That(restored.Entries[0].PixelData, Is.EqualTo(indices));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleEntries() {
    var pixels1 = new byte[2 * 2 * 4];
    var pixels2 = new byte[4 * 4 * 3];
    for (var i = 0; i < pixels1.Length; ++i)
      pixels1[i] = (byte)(i * 17 % 256);
    for (var i = 0; i < pixels2.Length; ++i)
      pixels2[i] = (byte)(i * 23 % 256);

    var original = new FshFile {
      DirectoryId = "TEST",
      Entries = [
        new FshEntry {
          Tag = "ent0",
          RecordCode = FshRecordCode.Argb8888,
          Width = 2,
          Height = 2,
          PixelData = pixels1,
        },
        new FshEntry {
          Tag = "ent1",
          RecordCode = FshRecordCode.Rgb888,
          Width = 4,
          Height = 4,
          PixelData = pixels2,
        }
      ]
    };

    var bytes = FshWriter.ToBytes(original);
    var restored = FshReader.FromBytes(bytes);

    Assert.That(restored.DirectoryId, Is.EqualTo("TEST"));
    Assert.That(restored.Entries.Count, Is.EqualTo(2));
    Assert.That(restored.Entries[0].Tag, Is.EqualTo("ent0"));
    Assert.That(restored.Entries[0].PixelData, Is.EqualTo(pixels1));
    Assert.That(restored.Entries[1].Tag, Is.EqualTo("ent1"));
    Assert.That(restored.Entries[1].PixelData, Is.EqualTo(pixels2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[4 * 4 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 31 % 256);

    var original = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Argb8888,
          Width = 4,
          Height = 4,
          PixelData = pixels,
        }
      ]
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fsh");
    try {
      var bytes = FshWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = FshReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Entries.Count, Is.EqualTo(1));
      Assert.That(restored.Entries[0].PixelData, Is.EqualTo(pixels));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Argb8888_78Entry() {
    var pixels = new byte[2 * 2 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 19 % 256);

    var original = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Argb8888_78,
          Width = 2,
          Height = 2,
          PixelData = pixels,
        }
      ]
    };

    var bytes = FshWriter.ToBytes(original);
    var restored = FshReader.FromBytes(bytes);

    Assert.That(restored.Entries[0].RecordCode, Is.EqualTo(FshRecordCode.Argb8888_78));
    Assert.That(restored.Entries[0].PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImageAndBack_Argb8888() {
    // Create ARGB8888 data: A=0xFF, R=0xAA, G=0xBB, B=0xCC
    var pixels = new byte[] { 0xFF, 0xAA, 0xBB, 0xCC };
    var original = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Argb8888,
          Width = 1,
          Height = 1,
          PixelData = pixels,
        }
      ]
    };

    var raw = FshFile.ToRawImage(original);

    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Bgra32));
    // BGRA: B=0xCC, G=0xBB, R=0xAA, A=0xFF
    Assert.That(raw.PixelData[0], Is.EqualTo(0xCC));
    Assert.That(raw.PixelData[1], Is.EqualTo(0xBB));
    Assert.That(raw.PixelData[2], Is.EqualTo(0xAA));
    Assert.That(raw.PixelData[3], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_CreatesArgb8888() {
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = [0xCC, 0xBB, 0xAA, 0xFF, 0x11, 0x22, 0x33, 0x80]
    };

    var fsh = FshFile.FromRawImage(raw);

    Assert.That(fsh.Entries.Count, Is.EqualTo(1));
    Assert.That(fsh.Entries[0].RecordCode, Is.EqualTo(FshRecordCode.Argb8888));
    Assert.That(fsh.Entries[0].Width, Is.EqualTo(2));
    Assert.That(fsh.Entries[0].Height, Is.EqualTo(1));
    // BGRA -> ARGB: pixel 0: A=0xFF, R=0xAA, G=0xBB, B=0xCC
    Assert.That(fsh.Entries[0].PixelData[0], Is.EqualTo(0xFF));
    Assert.That(fsh.Entries[0].PixelData[1], Is.EqualTo(0xAA));
    Assert.That(fsh.Entries[0].PixelData[2], Is.EqualTo(0xBB));
    Assert.That(fsh.Entries[0].PixelData[3], Is.EqualTo(0xCC));
  }
}

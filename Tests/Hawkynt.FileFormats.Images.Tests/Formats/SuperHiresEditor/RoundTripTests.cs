using System;
using System.IO;
using FileFormat.SuperHiresEditor;

namespace FileFormat.SuperHiresEditor.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmap1 = new byte[8000];
    var screen1 = new byte[1000];
    var bitmap2 = new byte[8000];
    var screen2 = new byte[1000];
    for (var i = 0; i < 8000; ++i) {
      bitmap1[i] = (byte)(i * 7 % 256);
      bitmap2[i] = (byte)(i * 3 % 256);
    }

    for (var i = 0; i < 1000; ++i) {
      screen1[i] = (byte)(i % 256);
      screen2[i] = (byte)((i + 5) % 256);
    }

    var original = new SuperHiresEditorFile {
      LoadAddress = 0x2000,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
    };

    var bytes = SuperHiresEditorWriter.ToBytes(original);
    var restored = SuperHiresEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
    Assert.That(restored.Screen1, Is.EqualTo(original.Screen1));
    Assert.That(restored.Bitmap2, Is.EqualTo(original.Bitmap2));
    Assert.That(restored.Screen2, Is.EqualTo(original.Screen2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var original = new SuperHiresEditorFile {
      LoadAddress = 0x6000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
    };

    var bytes = SuperHiresEditorWriter.ToBytes(original);
    var restored = SuperHiresEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var bitmap1 = new byte[8000];
    var bitmap2 = new byte[8000];
    var screen1 = new byte[1000];
    var screen2 = new byte[1000];
    Array.Fill(bitmap1, (byte)0xFF);
    Array.Fill(bitmap2, (byte)0xFF);
    Array.Fill(screen1, (byte)0xFF);
    Array.Fill(screen2, (byte)0xFF);

    var original = new SuperHiresEditorFile {
      LoadAddress = 0xFFFF,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
    };

    var bytes = SuperHiresEditorWriter.ToBytes(original);
    var restored = SuperHiresEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
    Assert.That(restored.Screen1, Is.EqualTo(original.Screen1));
    Assert.That(restored.Bitmap2, Is.EqualTo(original.Bitmap2));
    Assert.That(restored.Screen2, Is.EqualTo(original.Screen2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var bitmap1 = new byte[8000];
    for (var i = 0; i < 8000; ++i)
      bitmap1[i] = (byte)(i % 256);

    var original = new SuperHiresEditorFile {
      LoadAddress = 0x2000,
      Bitmap1 = bitmap1,
      Screen1 = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".she");
    try {
      var bytes = SuperHiresEditorWriter.ToBytes(original);
      File.WriteAllBytes(tmpPath, bytes);
      var restored = SuperHiresEditorReader.FromFile(new FileInfo(tmpPath));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TrailingDataPreserved() {
    var original = new SuperHiresEditorFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      TrailingData = [0x01, 0x02, 0x03],
    };

    var bytes = SuperHiresEditorWriter.ToBytes(original);
    var restored = SuperHiresEditorReader.FromBytes(bytes);

    Assert.That(restored.TrailingData, Is.EqualTo(original.TrailingData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ProducesCorrectDimensions() {
    var original = new SuperHiresEditorFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
    };

    var raw = SuperHiresEditorFile.ToRawImage(original);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }
}

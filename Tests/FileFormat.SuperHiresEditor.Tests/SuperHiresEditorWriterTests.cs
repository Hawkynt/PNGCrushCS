using System;
using FileFormat.SuperHiresEditor;

namespace FileFormat.SuperHiresEditor.Tests;

[TestFixture]
public sealed class SuperHiresEditorWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SuperHiresEditorWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MinimalFile_OutputIs18002Bytes() {
    var file = _BuildValidFile();
    var bytes = SuperHiresEditorWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(SuperHiresEditorFile.LoadAddressSize + SuperHiresEditorFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_WrittenAsLittleEndian() {
    var file = new SuperHiresEditorFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
    };

    var bytes = SuperHiresEditorWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x20));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Bitmap1Offset_StartsAtByte2() {
    var file = new SuperHiresEditorFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
    };
    file.Bitmap1[0] = 0xAA;
    file.Bitmap1[7999] = 0xBB;

    var bytes = SuperHiresEditorWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[8001], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Screen1Offset_StartsAtByte8002() {
    var file = new SuperHiresEditorFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
    };
    file.Screen1[0] = 0xCC;
    file.Screen1[999] = 0xDD;

    var bytes = SuperHiresEditorWriter.ToBytes(file);

    Assert.That(bytes[8002], Is.EqualTo(0xCC));
    Assert.That(bytes[9001], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Bitmap2Offset_StartsAtByte9002() {
    var file = new SuperHiresEditorFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
    };
    file.Bitmap2[0] = 0xEE;
    file.Bitmap2[7999] = 0xFF;

    var bytes = SuperHiresEditorWriter.ToBytes(file);

    Assert.That(bytes[9002], Is.EqualTo(0xEE));
    Assert.That(bytes[17001], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Screen2Offset_StartsAtByte17002() {
    var file = new SuperHiresEditorFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
    };
    file.Screen2[0] = 0x11;
    file.Screen2[999] = 0x22;

    var bytes = SuperHiresEditorWriter.ToBytes(file);

    Assert.That(bytes[17002], Is.EqualTo(0x11));
    Assert.That(bytes[18001], Is.EqualTo(0x22));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithTrailingData_IncludesTrailingBytes() {
    var file = new SuperHiresEditorFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      TrailingData = [0xAA, 0xBB, 0xCC],
    };

    var bytes = SuperHiresEditorWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(18002 + 3));
    Assert.That(bytes[18002], Is.EqualTo(0xAA));
    Assert.That(bytes[18003], Is.EqualTo(0xBB));
    Assert.That(bytes[18004], Is.EqualTo(0xCC));
  }

  private static SuperHiresEditorFile _BuildValidFile() {
    var bitmap1 = new byte[8000];
    var screen1 = new byte[1000];
    var bitmap2 = new byte[8000];
    var screen2 = new byte[1000];
    for (var i = 0; i < 8000; ++i) {
      bitmap1[i] = (byte)(i % 256);
      bitmap2[i] = (byte)((i + 1) % 256);
    }

    for (var i = 0; i < 1000; ++i) {
      screen1[i] = (byte)(i % 256);
      screen2[i] = (byte)((i + 1) % 256);
    }

    return new() {
      LoadAddress = 0x2000,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
    };
  }
}

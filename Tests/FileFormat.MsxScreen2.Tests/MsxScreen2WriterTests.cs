using System;
using FileFormat.MsxScreen2;

namespace FileFormat.MsxScreen2.Tests;

[TestFixture]
public sealed class MsxScreen2WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen2Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithoutHeader_SizeMatchesVramData() {
    var file = new MsxScreen2File {
      PatternGenerator = new byte[MsxScreen2File.PatternGeneratorSize],
      ColorTable = new byte[MsxScreen2File.ColorTableSize],
      PatternNameTable = new byte[MsxScreen2File.PatternNameTableSize],
      HasBsaveHeader = false
    };

    var bytes = MsxScreen2Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(MsxScreen2File.VramDataSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithBsaveHeader_PrependsMagicByte() {
    var file = new MsxScreen2File {
      PatternGenerator = new byte[MsxScreen2File.PatternGeneratorSize],
      ColorTable = new byte[MsxScreen2File.ColorTableSize],
      PatternNameTable = new byte[MsxScreen2File.PatternNameTableSize],
      HasBsaveHeader = true
    };

    var bytes = MsxScreen2Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(MsxScreen2File.BsaveMagic));
    Assert.That(bytes.Length, Is.EqualTo(MsxScreen2File.FileWithHeaderSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PatternGeneratorDataPreserved() {
    var patternGen = new byte[MsxScreen2File.PatternGeneratorSize];
    for (var i = 0; i < patternGen.Length; ++i)
      patternGen[i] = (byte)(i * 3 % 256);

    var file = new MsxScreen2File {
      PatternGenerator = patternGen,
      ColorTable = new byte[MsxScreen2File.ColorTableSize],
      PatternNameTable = new byte[MsxScreen2File.PatternNameTableSize],
      HasBsaveHeader = false
    };

    var bytes = MsxScreen2Writer.ToBytes(file);

    for (var i = 0; i < MsxScreen2File.PatternGeneratorSize; ++i)
      Assert.That(bytes[i], Is.EqualTo(patternGen[i]));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ColorTableDataPreserved() {
    var colorTable = new byte[MsxScreen2File.ColorTableSize];
    for (var i = 0; i < colorTable.Length; ++i)
      colorTable[i] = (byte)(i * 5 % 256);

    var file = new MsxScreen2File {
      PatternGenerator = new byte[MsxScreen2File.PatternGeneratorSize],
      ColorTable = colorTable,
      PatternNameTable = new byte[MsxScreen2File.PatternNameTableSize],
      HasBsaveHeader = false
    };

    var bytes = MsxScreen2Writer.ToBytes(file);

    for (var i = 0; i < MsxScreen2File.ColorTableSize; ++i)
      Assert.That(bytes[MsxScreen2File.PatternGeneratorSize + i], Is.EqualTo(colorTable[i]));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PatternNameTableDataPreserved() {
    var nameTable = new byte[MsxScreen2File.PatternNameTableSize];
    for (var i = 0; i < nameTable.Length; ++i)
      nameTable[i] = (byte)(i * 7 % 256);

    var file = new MsxScreen2File {
      PatternGenerator = new byte[MsxScreen2File.PatternGeneratorSize],
      ColorTable = new byte[MsxScreen2File.ColorTableSize],
      PatternNameTable = nameTable,
      HasBsaveHeader = false
    };

    var bytes = MsxScreen2Writer.ToBytes(file);

    var offset = MsxScreen2File.PatternGeneratorSize + MsxScreen2File.ColorTableSize;
    for (var i = 0; i < MsxScreen2File.PatternNameTableSize; ++i)
      Assert.That(bytes[offset + i], Is.EqualTo(nameTable[i]));
  }
}

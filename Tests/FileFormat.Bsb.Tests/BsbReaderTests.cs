using System;
using System.IO;
using System.Text;
using FileFormat.Bsb;

namespace FileFormat.Bsb.Tests;

[TestFixture]
public sealed class BsbReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BsbReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BsbReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".kap"));
    Assert.Throws<FileNotFoundException>(() => BsbReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BsbReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => BsbReader.FromBytes([1, 2]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoNulTerminator_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("VER/3.0\nBSB/NA=Test,RA=4,2\n");
    Assert.Throws<InvalidDataException>(() => BsbReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingDimensions_ThrowsInvalidDataException() {
    var header = "VER/3.0\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 1];
    Array.Copy(headerBytes, data, headerBytes.Length);
    data[headerBytes.Length] = 0x00;

    Assert.Throws<InvalidDataException>(() => BsbReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHeader_ParsesDimensions() {
    var data = _BuildMinimalBsb(4, 2, 2);
    var result = BsbReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHeader_ParsesPalette() {
    var data = _BuildMinimalBsb(2, 1, 2);
    var result = BsbReader.FromBytes(data);

    Assert.That(result.PaletteCount, Is.EqualTo(2));
    Assert.That(result.Palette[0], Is.EqualTo(255));
    Assert.That(result.Palette[1], Is.EqualTo(0));
    Assert.That(result.Palette[2], Is.EqualTo(0));
    Assert.That(result.Palette[3], Is.EqualTo(0));
    Assert.That(result.Palette[4], Is.EqualTo(255));
    Assert.That(result.Palette[5], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHeader_ParsesName() {
    var data = _BuildMinimalBsb(2, 1, 2, "TestChart");
    var result = BsbReader.FromBytes(data);

    Assert.That(result.Name, Is.EqualTo("TestChart"));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesDimensions() {
    var data = _BuildMinimalBsb(3, 2, 2);
    using var ms = new MemoryStream(data);
    var result = BsbReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void DecodePixelData_SingleByteRun_DecodesCorrectly() {
    // depth=7, runBits=1, so byte = (colorIndex << 1) | runLength
    // color 0, run 1 = 0x01, color 1, run 1 = 0x03
    var rowNumberByte = (byte)1; // row 1
    // Row index table (1 row, 4 bytes, big-endian offset pointing to byte 4)
    var rowOffset = 4; // row data starts after the 4-byte index table
    var encoded = new byte[] {
      (byte)((rowOffset >> 24) & 0xFF), (byte)((rowOffset >> 16) & 0xFF),
      (byte)((rowOffset >> 8) & 0xFF), (byte)(rowOffset & 0xFF),
      rowNumberByte, 0x01, 0x03, 0x00 // row 1, color0 x1, color1 x1, terminator
    };

    var pixels = BsbReader._DecodePixelData(encoded, 0, 2, 1, 7);

    Assert.That(pixels[0], Is.EqualTo(0));
    Assert.That(pixels[1], Is.EqualTo(1));
  }

  private static byte[] _BuildMinimalBsb(int width, int height, int paletteCount, string name = "NOAA") {
    var sb = new StringBuilder();
    sb.AppendLine("VER/3.0");
    sb.Append("BSB/NA=");
    sb.Append(name);
    sb.Append(",RA=");
    sb.Append(width);
    sb.Append(',');
    sb.AppendLine(height.ToString());

    if (paletteCount >= 1)
      sb.AppendLine("RGB/0,255,0,0");
    if (paletteCount >= 2)
      sb.AppendLine("RGB/1,0,255,0");

    var header = Encoding.ASCII.GetBytes(sb.ToString());

    // Build minimal pixel data: each row has row number + all-zero pixels + terminator
    using var ms = new MemoryStream();
    ms.Write(header, 0, header.Length);
    ms.WriteByte(0x00); // NUL terminator

    // Dummy row index table
    var indexTableStart = (int)ms.Position;
    var indexTable = new byte[height * 4];
    ms.Write(indexTable, 0, indexTable.Length);

    // Encode rows
    var rowOffsets = new int[height];
    var depth = 7;
    var runBits = 8 - depth;
    for (var row = 0; row < height; ++row) {
      rowOffsets[row] = (int)ms.Position;
      ms.WriteByte((byte)(row + 1)); // row number (1-based, small enough for single byte)

      // Write all pixels as color 0
      var remaining = width;
      var maxRun = (1 << runBits) - 1;
      while (remaining > 0) {
        var run = Math.Min(remaining, maxRun);
        ms.WriteByte((byte)run); // color 0 << runBits | run (color 0 so upper bits are 0)
        remaining -= run;
      }

      ms.WriteByte(0x00); // row terminator
    }

    // Patch index table
    var result = ms.ToArray();
    for (var row = 0; row < height; ++row) {
      var offset = indexTableStart + row * 4;
      result[offset] = (byte)((rowOffsets[row] >> 24) & 0xFF);
      result[offset + 1] = (byte)((rowOffsets[row] >> 16) & 0xFF);
      result[offset + 2] = (byte)((rowOffsets[row] >> 8) & 0xFF);
      result[offset + 3] = (byte)(rowOffsets[row] & 0xFF);
    }

    return result;
  }
}

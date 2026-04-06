using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.ExtendedGemImg;
using FileFormat.Core;

namespace FileFormat.ExtendedGemImg.Tests;

#region Reader Tests

[TestFixture]
public sealed class ExtendedGemImgReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ExtendedGemImgReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ExtendedGemImgReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ximg"));
    Assert.Throws<FileNotFoundException>(() => ExtendedGemImgReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ExtendedGemImgReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => ExtendedGemImgReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMonochrome_ParsesCorrectly() {
    var data = _BuildMinimalXimg(16, 4, 1, 2);
    var result = ExtendedGemImgReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.NumPlanes, Is.EqualTo(1));
      Assert.That(result.Version, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidColor_ParsesPalette() {
    var data = _BuildMinimalXimg(16, 2, 4, 16);
    var result = ExtendedGemImgReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.NumPlanes, Is.EqualTo(4));
      Assert.That(result.ColorModel, Is.EqualTo(ExtendedGemImgColorModel.Rgb));
      Assert.That(result.PaletteData, Has.Length.EqualTo(16 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_XimgMarker_IsDetected() {
    var data = _BuildMinimalXimg(8, 1, 1, 2);
    var result = ExtendedGemImgReader.FromBytes(data);

    // If XIMG marker is present, palette data should be populated
    Assert.That(result.PaletteData, Has.Length.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildMinimalXimg(16, 4, 1, 2);
    using var ms = new MemoryStream(data);
    var result = ExtendedGemImgReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
    });
  }

  private static byte[] _BuildMinimalXimg(int width, int height, int numPlanes, int paletteCount) {
    var bytesPerRow = (width + 7) / 8;
    var ximgExtensionSize = 6 + paletteCount * 3 * 2; // marker + color model + palette
    var headerLengthInWords = (16 + ximgExtensionSize) / 2;

    using var ms = new MemoryStream();

    // Standard GEM IMG header (16 bytes)
    var headerBytes = new byte[16];
    BinaryPrimitives.WriteInt16BigEndian(headerBytes.AsSpan(0), 1);                       // Version
    BinaryPrimitives.WriteInt16BigEndian(headerBytes.AsSpan(2), (short)headerLengthInWords); // HeaderLength
    BinaryPrimitives.WriteInt16BigEndian(headerBytes.AsSpan(4), (short)numPlanes);        // NumPlanes
    BinaryPrimitives.WriteInt16BigEndian(headerBytes.AsSpan(6), 2);                       // PatternLength
    BinaryPrimitives.WriteInt16BigEndian(headerBytes.AsSpan(8), 85);                      // PixelWidth
    BinaryPrimitives.WriteInt16BigEndian(headerBytes.AsSpan(10), 85);                     // PixelHeight
    BinaryPrimitives.WriteInt16BigEndian(headerBytes.AsSpan(12), (short)width);           // ScanWidth
    BinaryPrimitives.WriteInt16BigEndian(headerBytes.AsSpan(14), (short)height);          // ScanLines
    ms.Write(headerBytes, 0, headerBytes.Length);

    // XIMG extension: marker
    var extBytes = new byte[ximgExtensionSize];
    BinaryPrimitives.WriteInt16BigEndian(extBytes.AsSpan(0), 0x5849); // "XI"
    BinaryPrimitives.WriteInt16BigEndian(extBytes.AsSpan(2), 0x4D47); // "MG"
    BinaryPrimitives.WriteInt16BigEndian(extBytes.AsSpan(4), 0);      // RGB color model

    // Write palette entries (0-1000 range)
    for (var i = 0; i < paletteCount; ++i) {
      var val = (short)(i * 1000 / Math.Max(paletteCount - 1, 1));
      BinaryPrimitives.WriteInt16BigEndian(extBytes.AsSpan(6 + i * 6), val);     // R
      BinaryPrimitives.WriteInt16BigEndian(extBytes.AsSpan(6 + i * 6 + 2), val); // G
      BinaryPrimitives.WriteInt16BigEndian(extBytes.AsSpan(6 + i * 6 + 4), val); // B
    }
    ms.Write(extBytes, 0, extBytes.Length);

    // Encode scan-line data per-plane using bit-string (0x80) opcodes
    for (var plane = 0; plane < numPlanes; ++plane)
      for (var row = 0; row < height; ++row) {
        ms.WriteByte(0x80);
        ms.WriteByte((byte)bytesPerRow);
        for (var b = 0; b < bytesPerRow; ++b)
          ms.WriteByte((byte)((plane * height + row) * 10 + b));
      }

    return ms.ToArray();
  }
}

#endregion

#region Writer Tests

#endregion

#region Round-Trip Tests

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Monochrome_1Plane() {
    var bytesPerRow = 2; // 16 pixels / 8
    var height = 4;
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 % 256);

    var paletteData = new short[] { 1000, 1000, 1000, 0, 0, 0 }; // White, Black

    var original = new ExtendedGemImgFile {
      Version = 1,
      Width = 16,
      Height = height,
      NumPlanes = 1,
      PatternLength = 2,
      PixelWidth = 85,
      PixelHeight = 85,
      ColorModel = ExtendedGemImgColorModel.Rgb,
      PaletteData = paletteData,
      PixelData = pixelData
    };

    var bytes = ExtendedGemImgWriter.ToBytes(original);
    var restored = ExtendedGemImgReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.NumPlanes, Is.EqualTo(original.NumPlanes));
      Assert.That(restored.Version, Is.EqualTo(original.Version));
      Assert.That(restored.PatternLength, Is.EqualTo(original.PatternLength));
      Assert.That(restored.PixelWidth, Is.EqualTo(original.PixelWidth));
      Assert.That(restored.PixelHeight, Is.EqualTo(original.PixelHeight));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPlane() {
    var bytesPerRow = 4; // 32 pixels / 8
    var height = 3;
    var numPlanes = 4;
    var pixelData = new byte[numPlanes * bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var paletteData = new short[16 * 3];
    for (var i = 0; i < 16; ++i) {
      paletteData[i * 3] = (short)(i * 1000 / 15);
      paletteData[i * 3 + 1] = (short)(i * 1000 / 15);
      paletteData[i * 3 + 2] = (short)(i * 1000 / 15);
    }

    var original = new ExtendedGemImgFile {
      Version = 1,
      Width = 32,
      Height = height,
      NumPlanes = numPlanes,
      PatternLength = 2,
      PixelWidth = 85,
      PixelHeight = 85,
      ColorModel = ExtendedGemImgColorModel.Rgb,
      PaletteData = paletteData,
      PixelData = pixelData
    };

    var bytes = ExtendedGemImgWriter.ToBytes(original);
    var restored = ExtendedGemImgReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.NumPlanes, Is.EqualTo(original.NumPlanes));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PalettePreserved() {
    var paletteData = new short[] { 1000, 0, 0, 0, 1000, 0, 0, 0, 1000, 500, 500, 500 }; // R, G, B, Gray

    var original = new ExtendedGemImgFile {
      Version = 1,
      Width = 16,
      Height = 2,
      NumPlanes = 2,
      PatternLength = 2,
      PixelWidth = 85,
      PixelHeight = 85,
      ColorModel = ExtendedGemImgColorModel.Rgb,
      PaletteData = paletteData,
      PixelData = new byte[2 * 2 * 2] // 2 planes * 2 bytes/row * 2 rows
    };

    var bytes = ExtendedGemImgWriter.ToBytes(original);
    var restored = ExtendedGemImgReader.FromBytes(bytes);

    Assert.That(restored.PaletteData, Is.EqualTo(original.PaletteData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ColorModelPreserved() {
    var original = new ExtendedGemImgFile {
      Version = 1,
      Width = 8,
      Height = 1,
      NumPlanes = 1,
      PatternLength = 1,
      PixelWidth = 85,
      PixelHeight = 85,
      ColorModel = ExtendedGemImgColorModel.Cmy,
      PaletteData = new short[] { 0, 0, 0, 1000, 1000, 1000 },
      PixelData = new byte[1]
    };

    var bytes = ExtendedGemImgWriter.ToBytes(original);
    var restored = ExtendedGemImgReader.FromBytes(bytes);

    Assert.That(restored.ColorModel, Is.EqualTo(ExtendedGemImgColorModel.Cmy));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new ExtendedGemImgFile {
      Version = 1,
      Width = 16,
      Height = 2,
      NumPlanes = 1,
      PatternLength = 2,
      PixelWidth = 85,
      PixelHeight = 85,
      ColorModel = ExtendedGemImgColorModel.Rgb,
      PaletteData = new short[] { 1000, 1000, 1000, 0, 0, 0 },
      PixelData = new byte[2 * 2]
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ximg");
    try {
      File.WriteAllBytes(tmpPath, ExtendedGemImgWriter.ToBytes(original));
      var restored = ExtendedGemImgReader.FromFile(new FileInfo(tmpPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(original.Width));
        Assert.That(restored.Height, Is.EqualTo(original.Height));
        Assert.That(restored.NumPlanes, Is.EqualTo(original.NumPlanes));
        Assert.That(restored.PaletteData, Is.EqualTo(original.PaletteData));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var paletteData = new short[16 * 3];
    for (var i = 0; i < 16; ++i) {
      paletteData[i * 3] = (short)(i * 1000 / 15);
      paletteData[i * 3 + 1] = (short)((15 - i) * 1000 / 15);
      paletteData[i * 3 + 2] = (short)(500);
    }

    var pixelData = new byte[4 * 2 * 4]; // 4 planes * 4 bytes/row * 2 rows
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 16);

    var original = new ExtendedGemImgFile {
      Version = 1,
      Width = 32,
      Height = 2,
      NumPlanes = 4,
      PatternLength = 2,
      PixelWidth = 85,
      PixelHeight = 85,
      ColorModel = ExtendedGemImgColorModel.Rgb,
      PaletteData = paletteData,
      PixelData = pixelData
    };

    var raw = ExtendedGemImgFile.ToRawImage(original);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(32));
      Assert.That(raw.Height, Is.EqualTo(2));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.PaletteCount, Is.EqualTo(16));
    });

    var restored = ExtendedGemImgFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(32));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.NumPlanes, Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PixelAspectRatio_Preserved() {
    var original = new ExtendedGemImgFile {
      Version = 1,
      Width = 8,
      Height = 1,
      NumPlanes = 1,
      PatternLength = 1,
      PixelWidth = 170,
      PixelHeight = 85,
      ColorModel = ExtendedGemImgColorModel.Rgb,
      PaletteData = new short[] { 1000, 1000, 1000, 0, 0, 0 },
      PixelData = new byte[1]
    };

    var bytes = ExtendedGemImgWriter.ToBytes(original);
    var restored = ExtendedGemImgReader.FromBytes(bytes);

    Assert.That(restored.PixelWidth, Is.EqualTo(170));
    Assert.That(restored.PixelHeight, Is.EqualTo(85));
  }
}

#endregion

#region Header Tests

[TestFixture]
public sealed class ExtendedGemImgHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new ExtendedGemImgHeader(1, 8, 4, 2, 85, 170, 320, 200);
    Span<byte> buffer = stackalloc byte[ExtendedGemImgHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = ExtendedGemImgHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[16];
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 1);     // Version
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), 8);     // HeaderLength
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 4);     // NumPlanes
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(6), 2);     // PatternLength
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(8), 85);    // PixelWidth
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(10), 170);  // PixelHeight
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(12), 640);  // ScanWidth
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(14), 400);  // ScanLines

    var header = ExtendedGemImgHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Version, Is.EqualTo(1));
      Assert.That(header.HeaderLength, Is.EqualTo(8));
      Assert.That(header.NumPlanes, Is.EqualTo(4));
      Assert.That(header.PatternLength, Is.EqualTo(2));
      Assert.That(header.PixelWidth, Is.EqualTo(85));
      Assert.That(header.PixelHeight, Is.EqualTo(170));
      Assert.That(header.ScanWidth, Is.EqualTo(640));
      Assert.That(header.ScanLines, Is.EqualTo(400));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = ExtendedGemImgHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(ExtendedGemImgHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = ExtendedGemImgHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is16() {
    Assert.That(ExtendedGemImgHeader.StructSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void XimgMarker_HasCorrectValues() {
    Assert.Multiple(() => {
      Assert.That(ExtendedGemImgHeader.XimgMarker1, Is.EqualTo(0x5849));
      Assert.That(ExtendedGemImgHeader.XimgMarker2, Is.EqualTo(0x4D47));
    });
  }
}

#endregion

#region Data Type Tests

#endregion

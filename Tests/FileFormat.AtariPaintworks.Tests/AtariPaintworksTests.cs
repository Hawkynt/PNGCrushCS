using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.AtariPaintworks;
using FileFormat.Core;

namespace FileFormat.AtariPaintworks.Tests;

#region Reader Tests

[TestFixture]
public sealed class AtariPaintworksReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariPaintworksReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariPaintworksReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cl0"));
    Assert.Throws<FileNotFoundException>(() => AtariPaintworksReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariPaintworksReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => AtariPaintworksReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmallForPixelData_ThrowsInvalidDataException() {
    var tooSmall = new byte[33]; // header but no pixel data
    Assert.Throws<InvalidDataException>(() => AtariPaintworksReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidLowRes_ParsesCorrectly() {
    var data = _BuildPaintworksFile();
    var result = AtariPaintworksReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Resolution, Is.EqualTo(AtariPaintworksResolution.Low));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
      Assert.That(result.Palette, Has.Length.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PaletteIsPreserved() {
    var data = _BuildPaintworksFile();
    var result = AtariPaintworksReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Palette[0], Is.EqualTo(unchecked((short)(0 * 0x111 & 0x777))));
      Assert.That(result.Palette[1], Is.EqualTo(unchecked((short)(1 * 0x111 & 0x777))));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataIsPreserved() {
    var data = _BuildPaintworksFile();
    var result = AtariPaintworksReader.FromBytes(data);
    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithResolution_Medium_ParsesDimensions() {
    var data = _BuildPaintworksFile();
    var result = AtariPaintworksReader.FromBytes(data, AtariPaintworksResolution.Medium);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(640));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Resolution, Is.EqualTo(AtariPaintworksResolution.Medium));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithResolution_High_ParsesDimensions() {
    var data = _BuildPaintworksFile();
    var result = AtariPaintworksReader.FromBytes(data, AtariPaintworksResolution.High);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(640));
      Assert.That(result.Height, Is.EqualTo(400));
      Assert.That(result.Resolution, Is.EqualTo(AtariPaintworksResolution.High));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildPaintworksFile();
    using var ms = new MemoryStream(data);
    var result = AtariPaintworksReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
    });
  }

  private static byte[] _BuildPaintworksFile() {
    var data = new byte[32 + 32000];
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var header = AtariPaintworksHeader.FromPalette(palette);
    header.WriteTo(data.AsSpan());

    for (var i = 0; i < 32000; ++i)
      data[32 + i] = (byte)(i & 0xFF);

    return data;
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
  public void RoundTrip_LowRes_AllFieldsPreserved() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new AtariPaintworksFile {
      Width = 320,
      Height = 200,
      Resolution = AtariPaintworksResolution.Low,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = AtariPaintworksWriter.ToBytes(original);
    var restored = AtariPaintworksReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithResolutionHint_Medium() {
    var palette = new short[16];
    palette[0] = 0x000;
    palette[1] = 0x700;
    palette[2] = 0x070;
    palette[3] = 0x007;

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 4);

    var original = new AtariPaintworksFile {
      Width = 640,
      Height = 200,
      Resolution = AtariPaintworksResolution.Medium,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = AtariPaintworksWriter.ToBytes(original);
    var restored = AtariPaintworksReader.FromBytes(bytes, AtariPaintworksResolution.Medium);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(640));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.Resolution, Is.EqualTo(AtariPaintworksResolution.Medium));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var original = new AtariPaintworksFile {
      Width = 320,
      Height = 200,
      Resolution = AtariPaintworksResolution.Low,
      Palette = palette,
      PixelData = new byte[32000]
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cl0");
    try {
      File.WriteAllBytes(tmpPath, AtariPaintworksWriter.ToBytes(original));
      var restored = AtariPaintworksReader.FromFile(new FileInfo(tmpPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(original.Width));
        Assert.That(restored.Height, Is.EqualTo(original.Height));
        Assert.That(restored.Palette, Is.EqualTo(original.Palette));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_LowRes() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 16); // keep within 4-bit range

    var original = new AtariPaintworksFile {
      Width = 320,
      Height = 200,
      Resolution = AtariPaintworksResolution.Low,
      Palette = palette,
      PixelData = pixelData
    };

    var raw = AtariPaintworksFile.ToRawImage(original);
    var restored = AtariPaintworksFile.FromRawImage(raw);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.Resolution, Is.EqualTo(AtariPaintworksResolution.Low));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new AtariPaintworksFile {
      Width = 320,
      Height = 200,
      Resolution = AtariPaintworksResolution.Low,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = AtariPaintworksWriter.ToBytes(original);
    var restored = AtariPaintworksReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}

#endregion

#region Header Tests

[TestFixture]
public sealed class AtariPaintworksHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var original = AtariPaintworksHeader.FromPalette(palette);
    Span<byte> buffer = stackalloc byte[AtariPaintworksHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = AtariPaintworksHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[AtariPaintworksHeader.StructSize];
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 0x777); // Palette[0] = white
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), 0x700); // Palette[1] = red
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 0x070); // Palette[2] = green
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(6), 0x007); // Palette[3] = blue

    var header = AtariPaintworksHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Palette0, Is.EqualTo(0x777));
      Assert.That(header.Palette1, Is.EqualTo(0x700));
      Assert.That(header.Palette2, Is.EqualTo(0x070));
      Assert.That(header.Palette3, Is.EqualTo(0x007));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = AtariPaintworksHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(AtariPaintworksHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = AtariPaintworksHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is32() {
    Assert.That(AtariPaintworksHeader.StructSize, Is.EqualTo(32));
  }
}

#endregion

#region Data Type Tests

#endregion

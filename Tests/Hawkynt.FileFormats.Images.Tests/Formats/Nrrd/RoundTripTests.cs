using System;
using System.IO;
using FileFormat.Nrrd;

namespace FileFormat.Nrrd.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_UInt8_2D() {
    var pixelData = new byte[12];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new NrrdFile {
      Sizes = [4, 3],
      DataType = NrrdType.UInt8,
      Encoding = NrrdEncoding.Raw,
      Endian = "little",
      PixelData = pixelData
    };

    var bytes = NrrdWriter.ToBytes(original);
    var restored = NrrdReader.FromBytes(bytes);

    Assert.That(restored.Sizes, Is.EqualTo(original.Sizes));
    Assert.That(restored.DataType, Is.EqualTo(original.DataType));
    Assert.That(restored.Encoding, Is.EqualTo(original.Encoding));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Float_PreservesData() {
    var floatValues = new float[] { 1.0f, 2.5f, -3.14f, 0.0f };
    var pixelData = new byte[floatValues.Length * 4];
    Buffer.BlockCopy(floatValues, 0, pixelData, 0, pixelData.Length);

    var original = new NrrdFile {
      Sizes = [4],
      DataType = NrrdType.Float,
      Encoding = NrrdEncoding.Raw,
      Endian = "little",
      PixelData = pixelData
    };

    var bytes = NrrdWriter.ToBytes(original);
    var restored = NrrdReader.FromBytes(bytes);

    Assert.That(restored.Sizes, Is.EqualTo(original.Sizes));
    Assert.That(restored.DataType, Is.EqualTo(NrrdType.Float));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gzip_DecompressesCorrectly() {
    var pixelData = new byte[100];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 10);

    var original = new NrrdFile {
      Sizes = [10, 10],
      DataType = NrrdType.UInt8,
      Encoding = NrrdEncoding.Gzip,
      Endian = "little",
      PixelData = pixelData
    };

    var bytes = NrrdWriter.ToBytes(original);
    var restored = NrrdReader.FromBytes(bytes);

    Assert.That(restored.Sizes, Is.EqualTo(original.Sizes));
    Assert.That(restored.Encoding, Is.EqualTo(NrrdEncoding.Gzip));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ascii_UInt16BigEndian_PreservesValuesAndBytes() {
    byte[] pixelData = [0x00, 0x01, 0x12, 0x34, 0xFF, 0xFE];
    var original = new NrrdFile {
      Sizes = [3],
      DataType = NrrdType.UInt16,
      Encoding = NrrdEncoding.Ascii,
      Endian = "big",
      PixelData = pixelData
    };

    var bytes = NrrdWriter.ToBytes(original);
    var restored = NrrdReader.FromBytes(bytes);
    var text = System.Text.Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("1 4660 65534"));
    Assert.That(restored.Encoding, Is.EqualTo(NrrdEncoding.Ascii));
    Assert.That(restored.Endian, Is.EqualTo("big"));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ascii_FloatAndDouble_PreserveExactBitPatterns() {
    var floatValues = new[] { -3.5f, 0f, 1.25f, float.MaxValue };
    var floatBytes = new byte[floatValues.Length * sizeof(float)];
    Buffer.BlockCopy(floatValues, 0, floatBytes, 0, floatBytes.Length);
    var floatFile = new NrrdFile {
      Sizes = [floatValues.Length],
      DataType = NrrdType.Float,
      Encoding = NrrdEncoding.Ascii,
      Endian = "little",
      PixelData = floatBytes
    };

    var restoredFloat = NrrdReader.FromBytes(NrrdWriter.ToBytes(floatFile));
    Assert.That(restoredFloat.PixelData, Is.EqualTo(floatBytes));

    var doubleValues = new[] { -Math.PI, 0d, Math.E, double.MaxValue };
    var doubleBytes = new byte[doubleValues.Length * sizeof(double)];
    Buffer.BlockCopy(doubleValues, 0, doubleBytes, 0, doubleBytes.Length);
    var doubleFile = new NrrdFile {
      Sizes = [doubleValues.Length],
      DataType = NrrdType.Double,
      Encoding = NrrdEncoding.Ascii,
      Endian = "little",
      PixelData = doubleBytes
    };

    var restoredDouble = NrrdReader.FromBytes(NrrdWriter.ToBytes(doubleFile));
    Assert.That(restoredDouble.PixelData, Is.EqualTo(doubleBytes));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Hex_PreservesPayloadAcrossLineBreaks() {
    var pixelData = new byte[70];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 + 11);

    var original = new NrrdFile {
      Sizes = [70],
      DataType = NrrdType.UInt8,
      Encoding = NrrdEncoding.Hex,
      Endian = "little",
      PixelData = pixelData
    };

    var bytes = NrrdWriter.ToBytes(original);
    var restored = NrrdReader.FromBytes(bytes);

    Assert.That(restored.Encoding, Is.EqualTo(NrrdEncoding.Hex));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiDim_PreservesSizes() {
    var pixelData = new byte[24];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new NrrdFile {
      Sizes = [2, 3, 4],
      DataType = NrrdType.UInt8,
      Encoding = NrrdEncoding.Raw,
      Endian = "little",
      PixelData = pixelData
    };

    var bytes = NrrdWriter.ToBytes(original);
    var restored = NrrdReader.FromBytes(bytes);

    Assert.That(restored.Sizes, Is.EqualTo(new[] { 2, 3, 4 }));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithSpacings_Preserved() {
    var original = new NrrdFile {
      Sizes = [4, 3],
      DataType = NrrdType.UInt8,
      Encoding = NrrdEncoding.Raw,
      Endian = "little",
      Spacings = [1.5, 2.0],
      PixelData = new byte[12]
    };

    var bytes = NrrdWriter.ToBytes(original);
    var restored = NrrdReader.FromBytes(bytes);

    Assert.That(restored.Spacings.Length, Is.EqualTo(2));
    Assert.That(restored.Spacings[0], Is.EqualTo(1.5).Within(0.001));
    Assert.That(restored.Spacings[1], Is.EqualTo(2.0).Within(0.001));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[20];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new NrrdFile {
      Sizes = [5, 4],
      DataType = NrrdType.UInt8,
      Encoding = NrrdEncoding.Raw,
      Endian = "little",
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nrrd");
    try {
      var bytes = NrrdWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = NrrdReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Sizes, Is.EqualTo(original.Sizes));
      Assert.That(restored.DataType, Is.EqualTo(original.DataType));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}

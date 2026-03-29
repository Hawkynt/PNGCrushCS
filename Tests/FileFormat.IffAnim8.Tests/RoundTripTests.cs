using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffAnim8;

namespace FileFormat.IffAnim8.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_MinimalData_PreservesRawData() {
    var rawData = new byte[24];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new IffAnim8File {
      Width = 320,
      Height = 200,
      RawData = rawData,
    };

    var bytes = IffAnim8Writer.ToBytes(original);
    var restored = IffAnim8Reader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(rawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithBmhd_PreservesDimensions() {
    var rawData = _CreateDataWithBmhd(128, 96);

    var original = new IffAnim8File {
      Width = 128,
      Height = 96,
      RawData = rawData,
    };

    var bytes = IffAnim8Writer.ToBytes(original);
    var restored = IffAnim8Reader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(128));
      Assert.That(restored.Height, Is.EqualTo(96));
      Assert.That(restored.RawData, Is.EqualTo(rawData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerData_PreservesAllBytes() {
    var rawData = new byte[1024];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)((i * 13 + 7) % 256);

    var original = new IffAnim8File {
      Width = 320,
      Height = 200,
      RawData = rawData,
    };

    var bytes = IffAnim8Writer.ToBytes(original);
    var restored = IffAnim8Reader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(rawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = _CreateDataWithBmhd(64, 48);
    var original = new IffAnim8File {
      Width = 64,
      Height = 48,
      RawData = rawData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".an8");
    try {
      var bytes = IffAnim8Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = IffAnim8Reader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(64));
        Assert.That(restored.Height, Is.EqualTo(48));
        Assert.That(restored.RawData, Is.EqualTo(rawData));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var rawData = _CreateDataWithBmhd(4, 2);
    var original = new IffAnim8File {
      Width = 4,
      Height = 2,
      RawData = rawData,
    };

    var raw = IffAnim8File.ToRawImage(original);

    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(raw.Width, Is.EqualTo(4));
      Assert.That(raw.Height, Is.EqualTo(2));
      Assert.That(raw.PixelData, Has.Length.EqualTo(4 * 2 * 3));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_PreservesData() {
    var rawData = new byte[IffAnim8File.MinFileSize];

    var original = new IffAnim8File {
      Width = 320,
      Height = 200,
      RawData = rawData,
    };

    var bytes = IffAnim8Writer.ToBytes(original);
    var restored = IffAnim8Reader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(rawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes_PreservesData() {
    var rawData = new byte[IffAnim8File.MinFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new IffAnim8File {
      Width = 320,
      Height = 200,
      RawData = rawData,
    };

    var bytes = IffAnim8Writer.ToBytes(original);
    var restored = IffAnim8Reader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(rawData));
  }

  private static byte[] _CreateDataWithBmhd(int width, int height) {
    var bmhdData = new byte[20];
    bmhdData[0] = (byte)(width >> 8);
    bmhdData[1] = (byte)(width & 0xFF);
    bmhdData[2] = (byte)(height >> 8);
    bmhdData[3] = (byte)(height & 0xFF);

    var data = new byte[12 + 4 + 4 + 20 + 10];
    var offset = 0;

    data[offset++] = (byte)'F';
    data[offset++] = (byte)'O';
    data[offset++] = (byte)'R';
    data[offset++] = (byte)'M';
    offset += 4;
    data[offset++] = (byte)'A';
    data[offset++] = (byte)'N';
    data[offset++] = (byte)'8';
    data[offset++] = (byte)' ';

    data[offset++] = 0x42; // 'B'
    data[offset++] = 0x4D; // 'M'
    data[offset++] = 0x48; // 'H'
    data[offset++] = 0x44; // 'D'

    data[offset++] = 0;
    data[offset++] = 0;
    data[offset++] = 0;
    data[offset++] = 20;

    Array.Copy(bmhdData, 0, data, offset, bmhdData.Length);

    return data;
  }
}

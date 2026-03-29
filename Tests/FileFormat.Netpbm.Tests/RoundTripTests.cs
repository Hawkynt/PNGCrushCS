using System;
using System.Text;
using FileFormat.Netpbm;

namespace FileFormat.Netpbm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_P4_PbmBinary() {
    var pixelData = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0, 0, 1, 0, 1, 0, 1, 0, 1 }; // 8x2

    var original = new NetpbmFile {
      Format = NetpbmFormat.PbmBinary,
      Width = 8,
      Height = 2,
      MaxValue = 1,
      Channels = 1,
      PixelData = pixelData
    };

    var bytes = NetpbmWriter.ToBytes(original);
    var restored = NetpbmReader.FromBytes(bytes);

    Assert.That(restored.Format, Is.EqualTo(NetpbmFormat.PbmBinary));
    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.MaxValue, Is.EqualTo(1));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_P4_NonAlignedWidth() {
    // 5 pixels wide: bits 10101 padded to 10101000 per row
    var pixelData = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0, 1, 0 }; // 5x2

    var original = new NetpbmFile {
      Format = NetpbmFormat.PbmBinary,
      Width = 5,
      Height = 2,
      MaxValue = 1,
      Channels = 1,
      PixelData = pixelData
    };

    var bytes = NetpbmWriter.ToBytes(original);
    var restored = NetpbmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(5));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_P5_PgmBinary() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 21 % 256);

    var original = new NetpbmFile {
      Format = NetpbmFormat.PgmBinary,
      Width = 4,
      Height = 3,
      MaxValue = 255,
      Channels = 1,
      PixelData = pixelData
    };

    var bytes = NetpbmWriter.ToBytes(original);
    var restored = NetpbmReader.FromBytes(bytes);

    Assert.That(restored.Format, Is.EqualTo(NetpbmFormat.PgmBinary));
    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.MaxValue, Is.EqualTo(255));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_P6_PpmBinary() {
    var pixelData = new byte[3 * 2 * 3]; // 3x2 RGB
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new NetpbmFile {
      Format = NetpbmFormat.PpmBinary,
      Width = 3,
      Height = 2,
      MaxValue = 255,
      Channels = 3,
      PixelData = pixelData
    };

    var bytes = NetpbmWriter.ToBytes(original);
    var restored = NetpbmReader.FromBytes(bytes);

    Assert.That(restored.Format, Is.EqualTo(NetpbmFormat.PpmBinary));
    Assert.That(restored.Width, Is.EqualTo(3));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.MaxValue, Is.EqualTo(255));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_P7_PamRgb() {
    var pixelData = new byte[2 * 2 * 3]; // 2x2 RGB
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 % 256);

    var original = new NetpbmFile {
      Format = NetpbmFormat.Pam,
      Width = 2,
      Height = 2,
      MaxValue = 255,
      Channels = 3,
      PixelData = pixelData,
      TupleType = "RGB"
    };

    var bytes = NetpbmWriter.ToBytes(original);
    var restored = NetpbmReader.FromBytes(bytes);

    Assert.That(restored.Format, Is.EqualTo(NetpbmFormat.Pam));
    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.MaxValue, Is.EqualTo(255));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.TupleType, Is.EqualTo("RGB"));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_P7_PamRgba() {
    var pixelData = new byte[2 * 2 * 4]; // 2x2 RGBA
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 19 % 256);

    var original = new NetpbmFile {
      Format = NetpbmFormat.Pam,
      Width = 2,
      Height = 2,
      MaxValue = 255,
      Channels = 4,
      PixelData = pixelData,
      TupleType = "RGB_ALPHA"
    };

    var bytes = NetpbmWriter.ToBytes(original);
    var restored = NetpbmReader.FromBytes(bytes);

    Assert.That(restored.Format, Is.EqualTo(NetpbmFormat.Pam));
    Assert.That(restored.Channels, Is.EqualTo(4));
    Assert.That(restored.TupleType, Is.EqualTo("RGB_ALPHA"));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_P7_PamGrayscale() {
    var pixelData = new byte[3 * 3]; // 3x3 grayscale
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 29 % 256);

    var original = new NetpbmFile {
      Format = NetpbmFormat.Pam,
      Width = 3,
      Height = 3,
      MaxValue = 255,
      Channels = 1,
      PixelData = pixelData,
      TupleType = "GRAYSCALE"
    };

    var bytes = NetpbmWriter.ToBytes(original);
    var restored = NetpbmReader.FromBytes(bytes);

    Assert.That(restored.TupleType, Is.EqualTo("GRAYSCALE"));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_P1_PbmAscii() {
    var pixelData = new byte[] { 1, 0, 1, 0, 1, 0 }; // 3x2

    var original = new NetpbmFile {
      Format = NetpbmFormat.PbmAscii,
      Width = 3,
      Height = 2,
      MaxValue = 1,
      Channels = 1,
      PixelData = pixelData
    };

    var bytes = NetpbmWriter.ToBytes(original);
    var restored = NetpbmReader.FromBytes(bytes);

    Assert.That(restored.Format, Is.EqualTo(NetpbmFormat.PbmAscii));
    Assert.That(restored.Width, Is.EqualTo(3));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_P2_PgmAscii() {
    var pixelData = new byte[] { 0, 64, 128, 255 }; // 2x2

    var original = new NetpbmFile {
      Format = NetpbmFormat.PgmAscii,
      Width = 2,
      Height = 2,
      MaxValue = 255,
      Channels = 1,
      PixelData = pixelData
    };

    var bytes = NetpbmWriter.ToBytes(original);
    var restored = NetpbmReader.FromBytes(bytes);

    Assert.That(restored.Format, Is.EqualTo(NetpbmFormat.PgmAscii));
    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_P3_PpmAscii() {
    var pixelData = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 128, 128, 128 }; // 2x2 RGB

    var original = new NetpbmFile {
      Format = NetpbmFormat.PpmAscii,
      Width = 2,
      Height = 2,
      MaxValue = 255,
      Channels = 3,
      PixelData = pixelData
    };

    var bytes = NetpbmWriter.ToBytes(original);
    var restored = NetpbmReader.FromBytes(bytes);

    Assert.That(restored.Format, Is.EqualTo(NetpbmFormat.PpmAscii));
    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_P7_PamBlackAndWhite() {
    var pixelData = new byte[] { 0, 1, 1, 0 }; // 2x2

    var original = new NetpbmFile {
      Format = NetpbmFormat.Pam,
      Width = 2,
      Height = 2,
      MaxValue = 1,
      Channels = 1,
      PixelData = pixelData,
      TupleType = "BLACKANDWHITE"
    };

    var bytes = NetpbmWriter.ToBytes(original);
    var restored = NetpbmReader.FromBytes(bytes);

    Assert.That(restored.TupleType, Is.EqualTo("BLACKANDWHITE"));
    Assert.That(restored.MaxValue, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}

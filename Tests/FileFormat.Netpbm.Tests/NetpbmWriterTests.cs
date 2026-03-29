using System;
using System.Text;
using FileFormat.Netpbm;

namespace FileFormat.Netpbm.Tests;

[TestFixture]
public sealed class NetpbmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_P6_StartsWithCorrectMagic() {
    var file = new NetpbmFile {
      Format = NetpbmFormat.PpmBinary,
      Width = 2,
      Height = 2,
      MaxValue = 255,
      Channels = 3,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = NetpbmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'P'));
    Assert.That(bytes[1], Is.EqualTo((byte)'6'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_P5_StartsWithCorrectMagic() {
    var file = new NetpbmFile {
      Format = NetpbmFormat.PgmBinary,
      Width = 2,
      Height = 2,
      MaxValue = 255,
      Channels = 1,
      PixelData = new byte[2 * 2]
    };

    var bytes = NetpbmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'P'));
    Assert.That(bytes[1], Is.EqualTo((byte)'5'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_P4_StartsWithCorrectMagic() {
    var file = new NetpbmFile {
      Format = NetpbmFormat.PbmBinary,
      Width = 8,
      Height = 1,
      MaxValue = 1,
      Channels = 1,
      PixelData = new byte[8]
    };

    var bytes = NetpbmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'P'));
    Assert.That(bytes[1], Is.EqualTo((byte)'4'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_P7_StartsWithCorrectMagic() {
    var file = new NetpbmFile {
      Format = NetpbmFormat.Pam,
      Width = 1,
      Height = 1,
      MaxValue = 255,
      Channels = 3,
      PixelData = new byte[3],
      TupleType = "RGB"
    };

    var bytes = NetpbmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'P'));
    Assert.That(bytes[1], Is.EqualTo((byte)'7'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_P6_ContainsDimensionsInHeader() {
    var file = new NetpbmFile {
      Format = NetpbmFormat.PpmBinary,
      Width = 10,
      Height = 20,
      MaxValue = 255,
      Channels = 3,
      PixelData = new byte[10 * 20 * 3]
    };

    var bytes = NetpbmWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes, 0, Math.Min(50, bytes.Length));

    Assert.That(headerText, Does.Contain("10 20"));
    Assert.That(headerText, Does.Contain("255"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_P6_ContainsPixelData() {
    var pixelData = new byte[] { 255, 0, 0, 0, 255, 0 }; // 2 pixels: red, green
    var file = new NetpbmFile {
      Format = NetpbmFormat.PpmBinary,
      Width = 2,
      Height = 1,
      MaxValue = 255,
      Channels = 3,
      PixelData = pixelData
    };

    var bytes = NetpbmWriter.ToBytes(file);

    Assert.That(bytes[^6], Is.EqualTo(255)); // R of pixel 0
    Assert.That(bytes[^5], Is.EqualTo(0));   // G of pixel 0
    Assert.That(bytes[^4], Is.EqualTo(0));   // B of pixel 0
    Assert.That(bytes[^3], Is.EqualTo(0));   // R of pixel 1
    Assert.That(bytes[^2], Is.EqualTo(255)); // G of pixel 1
    Assert.That(bytes[^1], Is.EqualTo(0));   // B of pixel 1
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_P7_ContainsEndhdr() {
    var file = new NetpbmFile {
      Format = NetpbmFormat.Pam,
      Width = 1,
      Height = 1,
      MaxValue = 255,
      Channels = 1,
      PixelData = new byte[1],
      TupleType = "GRAYSCALE"
    };

    var bytes = NetpbmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes, 0, bytes.Length);

    Assert.That(text, Does.Contain("ENDHDR"));
    Assert.That(text, Does.Contain("TUPLTYPE GRAYSCALE"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_P1_StartsWithCorrectMagic() {
    var file = new NetpbmFile {
      Format = NetpbmFormat.PbmAscii,
      Width = 3,
      Height = 1,
      MaxValue = 1,
      Channels = 1,
      PixelData = new byte[] { 1, 0, 1 }
    };

    var bytes = NetpbmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'P'));
    Assert.That(bytes[1], Is.EqualTo((byte)'1'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_P2_StartsWithCorrectMagic() {
    var file = new NetpbmFile {
      Format = NetpbmFormat.PgmAscii,
      Width = 2,
      Height = 1,
      MaxValue = 255,
      Channels = 1,
      PixelData = new byte[] { 100, 200 }
    };

    var bytes = NetpbmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'P'));
    Assert.That(bytes[1], Is.EqualTo((byte)'2'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_P3_StartsWithCorrectMagic() {
    var file = new NetpbmFile {
      Format = NetpbmFormat.PpmAscii,
      Width = 1,
      Height = 1,
      MaxValue = 255,
      Channels = 3,
      PixelData = new byte[] { 128, 64, 32 }
    };

    var bytes = NetpbmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'P'));
    Assert.That(bytes[1], Is.EqualTo((byte)'3'));
  }
}

using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Crw.Tests;

[TestFixture]
public sealed class CrwWriterTests {

  private const int _SENSOR_WIDTH = 64;
  private const int _SENSOR_HEIGHT = 48;
  private const int _LEFT = 2;
  private const int _TOP = 2;
  private const int _WIDTH = 60;
  private const int _HEIGHT = 44;

  [Test]
  [Category("Unit")]
  public void ZeroDifferencesUseThePublishedCanonCodes() {
    var sensor = new ushort[64 * 8];
    Array.Fill(sensor, (ushort)512);
    var file = new CrwFile {
      Width = 64,
      Height = 8,
      PixelData = new byte[64 * 8 * 3],
      Sensor = sensor,
      SensorWidth = 64,
      SensorHeight = 8,
      BitsPerSample = 10,
    };

    var bytes = CrwWriter.ToBytes(file);

    // CIFF 1.2, then eight blocks of first-table 11110 (zero difference) and second-table
    // 111111011 (end of block). Padding brings those 112 bits to fourteen bytes.
    byte[] expected = [0xF7, 0xEF, 0xDF, 0xBF, 0x7E, 0xFD, 0xFB, 0xF7, 0xEF, 0xDF, 0xBF, 0x7E, 0xFD, 0xFB];
    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 18).ToArray(), Is.EqualTo(new byte[] {
        (byte)'I', (byte)'I', 0x1A, 0, 0, 0,
        (byte)'H', (byte)'E', (byte)'A', (byte)'P', (byte)'C', (byte)'C', (byte)'D', (byte)'R',
        0x02, 0x00, 0x01, 0x00,
      }));
      Assert.That(bytes.AsSpan(540, expected.Length).ToArray(), Is.EqualTo(expected));
    });
  }

  [TestCase(10)]
  [TestCase(12)]
  [Category("Unit")]
  public void WriterRoundTripsSensorBordersAndPrecision(int bitsPerSample) {
    var sensor = _Sensor(bitsPerSample);
    var file = new CrwFile {
      Width = _WIDTH,
      Height = _HEIGHT,
      PixelData = new byte[_WIDTH * _HEIGHT * 3],
      Sensor = sensor,
      SensorWidth = _SENSOR_WIDTH,
      SensorHeight = _SENSOR_HEIGHT,
      ImageLeft = _LEFT,
      ImageTop = _TOP,
      BitsPerSample = bitsPerSample,
    };

    var reopened = CrwReader.FromBytes(FormatIO.Write(file));

    Assert.Multiple(() => {
      Assert.That(reopened.Width, Is.EqualTo(file.Width));
      Assert.That(reopened.Height, Is.EqualTo(file.Height));
      Assert.That(reopened.SensorWidth, Is.EqualTo(file.SensorWidth));
      Assert.That(reopened.SensorHeight, Is.EqualTo(file.SensorHeight));
      Assert.That(reopened.ImageLeft, Is.EqualTo(file.ImageLeft));
      Assert.That(reopened.ImageTop, Is.EqualTo(file.ImageTop));
      Assert.That(reopened.BitsPerSample, Is.EqualTo(bitsPerSample));
      Assert.That(reopened.Sensor, Is.EqualTo(sensor));
    });
  }

  [Test]
  [Category("Unit")]
  public void RawImageConversionProducesAWritableCiffImage() {
    const int width = 61;
    const int height = 40;
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 29 + 7);

    var raw = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var crw = CrwFile.FromRawImage(raw);
    var reopened = CrwReader.FromBytes(FormatIO.Write(crw));

    Assert.Multiple(() => {
      Assert.That(crw.SensorWidth, Is.EqualTo(64));
      Assert.That(crw.BitsPerSample, Is.EqualTo(10));
      Assert.That(reopened.Width, Is.EqualTo(width));
      Assert.That(reopened.Height, Is.EqualTo(height));
      Assert.That(reopened.BitsPerSample, Is.EqualTo(10));
      Assert.That(reopened.Sensor, Is.EqualTo(crw.Sensor));
    });
  }

  [Test]
  [Category("Unit")]
  public void IncompleteCompressionBlocksAreRefused() {
    var file = new CrwFile {
      Width = 10,
      Height = 10,
      PixelData = new byte[300],
      Sensor = new ushort[100],
      SensorWidth = 10,
      SensorHeight = 10,
      BitsPerSample = 10,
    };

    Assert.That(() => CrwWriter.ToBytes(file), Throws.InstanceOf<InvalidDataException>());
  }

  private static ushort[] _Sensor(int bitsPerSample) {
    var result = new ushort[_SENSOR_WIDTH * _SENSOR_HEIGHT];
    for (var y = 0; y < _SENSOR_HEIGHT; ++y)
    for (var x = 0; x < _SENSOR_WIDTH; ++x) {
      var high = (x * 37 + y * 53 + (x ^ y) * 11) & 1023;
      result[y * _SENSOR_WIDTH + x] = bitsPerSample == 12
        ? (ushort)((high << 2) | ((x + y * 3) & 3))
        : (ushort)high;
    }

    return result;
  }
}

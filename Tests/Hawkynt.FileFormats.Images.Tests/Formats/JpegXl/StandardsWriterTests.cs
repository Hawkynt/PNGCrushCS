using FileFormat.Core;

namespace FileFormat.JpegXl.Tests;

[TestFixture]
public sealed class StandardsWriterTests {

  [Test]
  [Category("Integration")]
  public void Writer_EmitsBareJpegXlCodestream_AndSpecMetadataParsesIt() {
    var pixels = new byte[17 * 11 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 37 + 11);

    var bytes = JpegXlWriter.ToBytes(new JpegXlFile {
      Width = 17,
      Height = 11,
      ComponentCount = 3,
      PixelData = pixels,
    });

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(0xFF));
      Assert.That(bytes[1], Is.EqualTo(0x0A));
      Assert.That(JpegXlReader.TryReadSpecMetadata(bytes, out var metadata), Is.True);
      Assert.That(metadata.Width, Is.EqualTo(17));
      Assert.That(metadata.Height, Is.EqualTo(11));
      Assert.That(metadata.BitsPerSample, Is.EqualTo(8));
      Assert.That(metadata.IsModularFrame, Is.True);
    });
  }

  [TestCase(1)]
  [TestCase(3)]
  [Category("Integration")]
  public void Writer_Output_DecodesThroughSpecPathExactly(int channels) {
    const int width = 31;
    const int height = 19;
    var pixels = new byte[width * height * channels];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)((i * 29 + i / 7 * 13) & 0xFF);

    var bytes = JpegXlWriter.ToBytes(new JpegXlFile {
      Width = width,
      Height = height,
      ComponentCount = channels,
      PixelData = pixels,
    });
    var decoded = JpegXlReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.ComponentCount, Is.EqualTo(channels));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Integration")]
  public void Writer_Rgba_PreservesAlphaThroughStandardModularPath() {
    const int width = 13;
    const int height = 9;
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 4] = (byte)(i * 3);
      pixels[i * 4 + 1] = (byte)(255 - i * 5);
      pixels[i * 4 + 2] = (byte)(i * 11);
      pixels[i * 4 + 3] = (byte)(i * 17);
    }

    var source = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = pixels,
    };

    var file = JpegXlFile.FromRawImage(source);
    var encoded = JpegXlWriter.ToBytes(file);
    var restored = JpegXlFile.ToRawImage(JpegXlReader.FromBytes(encoded));

    Assert.Multiple(() => {
      Assert.That(file.ComponentCount, Is.EqualTo(4));
      Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(restored.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Integration")]
  public void ExistingCjxlModularFixture_DecodesByteExactlyThroughPublicReader() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "test_4x4_modular.jxl");
    var decoded = JpegXlReader.FromBytes(File.ReadAllBytes(path));
    var expected = new byte[] {
      0,0,0,      64,0,0,    128,0,0,   255,0,0,
      0,64,0,     0,128,0,   0,255,0,   64,64,64,
      0,0,128,    0,64,128,  0,128,255, 0,255,128,
      0,0,255,    64,0,255,  128,0,255, 255,0,255,
    };

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(4));
      Assert.That(decoded.Height, Is.EqualTo(4));
      Assert.That(decoded.ComponentCount, Is.EqualTo(3));
      Assert.That(decoded.PixelData, Is.EqualTo(expected));
    });
  }
}

using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.JpegXr;

namespace FileFormat.JpegXr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  // glencoesoftware/jxrlib fixture red.jxr: 10x10 solid red, 32bpp BGRA with planar alpha.
  private const string _RedFixtureBase64 =
    
      "SUm8ASAAAAAkw91vA07+S7GFPXd2jckPAAAAAAAAAAAKAAG8AQAQAAAACAAAAAK8BAABAAAAAAAAAIC8BAABAAAACgAAAIG8" +
      "BAABAAAACgAAAIK8CwABAAAAnASQQoO8CwABAAAAnASQQsC8BAABAAAAngAAAMG8BAABAAAArwAAAMK8BAABAAAATgEAAMO8" +
      "BAABAAAAeAAAAAAAAABXTVBIT1RPABFFwHEACQAJYADAAAAMAAAAwAAAAAABAAAACgAn//8AAAEBdcSPEXggAAABAgAhgAAI" +
      "BAMAABDAAAQCAYAAAAAAAAAAAAAAAAEDSxbn+jWyIvDIi8dNKJkP8QF9NKId/j3VkH9OmuptW+W7r6byh3ccy7fczLW25ly1" +
      "s5Da2T/qZP+q2N1NP+qBLtjdR43O5bTvVbGxGVb+bl9NxDu7uXb+TE1t+TawAFdNUEhPVE8AEUXAAQAJAAkAgCAIAAABAAAA" +
      "BgAU//8AAAEBkeAAAAECEEBCmGIwhAMQAAAAAQOPOkyUnbp55zhMzxTDQ5GI+9zgfDRwmyipGHRih47kaF+5zA7hjHY6m4J2" +
      "cRPeUNQtwtoeO6ahYnvKGoVLbnTsYA==";

  [Test]
  [Category("Unit")]
  public void Rgb24_LosslessRoundTrip_IsPixelExact() {
    const int width = 19, height = 17;
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var i = (y * width + x) * 3;
      pixels[i] = (byte)(x * 13 + y * 3);
      pixels[i + 1] = (byte)(x * 5 + y * 11);
      pixels[i + 2] = (byte)(255 - x * 7 - y * 2);
    }

    var original = new JpegXrFile { Width = width, Height = height, ComponentCount = 3, PixelData = pixels };
    var encoded = JpegXrWriter.ToBytes(original);
    var decoded = JpegXrReader.FromBytes(encoded);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.ComponentCount, Is.EqualTo(3));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void Gray8_LosslessRoundTrip_IsPixelExact() {
    const int width = 23, height = 9;
    var pixels = Enumerable.Range(0, width * height).Select(i => (byte)(i * 37 + 19)).ToArray();
    var original = new JpegXrFile { Width = width, Height = height, ComponentCount = 1, PixelData = pixels };

    var encoded = JpegXrWriter.ToBytes(original);
    var decoded = JpegXrReader.FromBytes(encoded);

    Assert.That(decoded.ComponentCount, Is.EqualTo(1));
    Assert.That(decoded.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void Rgba32_PlanarAlphaRoundTrip_IsPixelExact() {
    const int width = 17, height = 18;
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      var p = i * 4;
      pixels[p] = (byte)(i * 17 + 3);
      pixels[p + 1] = (byte)(i * 29 + 5);
      pixels[p + 2] = (byte)(255 - i * 11);
      pixels[p + 3] = (byte)(i * 43 + 7);
    }

    var encoded = JpegXrWriter.ToBytes(new JpegXrFile { Width = width, Height = height, ComponentCount = 4, PixelData = pixels });
    var decoded = JpegXrReader.FromBytes(encoded);

    Assert.Multiple(() => {
      Assert.That(decoded.ComponentCount, Is.EqualTo(4));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_UsesStandardWicGuidAndRealWmphotoCodestream() {
    var file = new JpegXrFile {
      Width = 3,
      Height = 2,
      ComponentCount = 3,
      PixelData = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255, 7, 11, 13, 19, 23, 29],
    };

    var bytes = JpegXrWriter.ToBytes(file);
    var ifdOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)));
    var entries = JpegXrIfd.ParseEntries(bytes, ifdOffset);
    var pixelEntry = entries.Single(e => e.Tag == JpegXrIfd.TAG_PIXEL_FORMAT);
    var imageEntry = entries.Single(e => e.Tag == JpegXrIfd.TAG_IMAGE_OFFSET);
    var countEntry = entries.Single(e => e.Tag == JpegXrIfd.TAG_IMAGE_BYTE_COUNT);
    var format = JpegXrIfd.ParsePixelFormat(bytes, pixelEntry);

    Assert.Multiple(() => {
      Assert.That(pixelEntry.Type, Is.EqualTo(JpegXrIfd.TYPE_BYTE));
      Assert.That(pixelEntry.Count, Is.EqualTo(16));
      Assert.That(format.ComponentCount, Is.EqualTo(3));
      Assert.That(format.BgrOrder, Is.False);
      Assert.That(imageEntry.Value % 4, Is.Zero);
      Assert.That(countEntry.Value, Is.GreaterThan(8));
      Assert.That(bytes.AsSpan((int)imageEntry.Value, 8).ToArray(),
        Is.EqualTo(new byte[] { (byte)'W', (byte)'M', (byte)'P', (byte)'H', (byte)'O', (byte)'T', (byte)'O', 0 }));
    });
  }

  /// <summary>
  /// Decodes glencoesoftware/JXRLib's independent frequency-order BGRA fixture through the public
  /// T.833 reader, including its separate frequency-order Y-only planar-alpha codestream.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void PublicReader_DecodesIndependentJxrLibFrequencyAndAlphaFixture() {
    var decoded = JpegXrReader.FromBytes(Convert.FromBase64String(_RedFixtureBase64));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(10));
      Assert.That(decoded.Height, Is.EqualTo(10));
      Assert.That(decoded.ComponentCount, Is.EqualTo(4));
      Assert.That(decoded.PixelData.Length, Is.EqualTo(10 * 10 * 4));
    });

    for (var i = 0; i < 100; ++i) {
      var p = i * 4;
      Assert.Multiple(() => {
        Assert.That(decoded.PixelData[p], Is.EqualTo(255), $"R at pixel {i}");
        Assert.That(decoded.PixelData[p + 1], Is.EqualTo(0), $"G at pixel {i}");
        Assert.That(decoded.PixelData[p + 2], Is.EqualTo(0), $"B at pixel {i}");
        Assert.That(decoded.PixelData[p + 3], Is.EqualTo(255), $"A at pixel {i}");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingThatIsNotAJpegXrAtAll()
    => Assert.Throws<System.IO.InvalidDataException>(() => JpegXrReader.FromBytes(new byte[64]));
}

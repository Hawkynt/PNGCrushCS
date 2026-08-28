using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.JpegXr;
using SharpAstro.Jxr;

namespace FileFormat.JpegXr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  // glencoesoftware/jxrlib fixture red.jxr: 10x10 solid red, 32bpp BGRA with planar alpha.
  // The public JpegXrFile model intentionally remains Gray8/RGB24, so this test exercises the
  // imported T.832 core directly against an independently produced JXRLib codestream.
  private const string _RedFixtureBase64 =
    "SUm8ASAAAAAkw91vA07+S7GFPXd2jckPAAAAAAAAAAAKAAG8AQAQAAAACAAA" +
    "AAK8BAABAAAAAAAAAIC8BAABAAAACgAAAIG8BAABAAAACgAAAIK8CwABAAAA" +
    "nASQQoO8CwABAAAAnASQQsC8BAABAAAAngAAAMG8BAABAAAArwAAAMK8BAAB" +
    "AAAATgEAAMO8BAABAAAAxgEAAAAAAABXTVBIT1RPABFFwHEACQAJYADAAAAM" +
    "AAAAwAAAAAABAAAACgAn//8AAAEBdcSPEXggAAABAgAhgAAIBAMAABDAAAQC" +
    "AYAAAAAAAAAAAAAAAAEDSxbn+jWyIvDIi8dNKJkP8QF9NKId/j3VkH9Omupt" +
    "W+W7r6byh3ccy7fczLW25ly1s5Da2T/qZP+q2N1NP+qBLtjdR43O5bTvVbGx" +
    "GVb+bl9NxDu7uXb+TE1t+TawAFdNUEhPVE8AEUXAAQAJAAkAgCAIAAABAAAA" +
    "BgAU//8AAAEBkeAAAAECEEBCmGIwhAMQAAAAAQOPOkyUnbp55zhMzxTDQ5GI" +
    "+9zgfDRwmyipGHRih47kaF+5zA7hjHY6m4J2cRPeUNQtwtoeO6ahYnvKGoVL" +
    "bnTsYA==";

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

  [Test]
  [Category("Unit")]
  public void ManagedCore_DecodesIndependentJxrLibFrequencyFixture() {
    var bytes = Convert.FromBase64String(_RedFixtureBase64);
    var ifdOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)));
    var entries = JpegXrIfd.ParseEntries(bytes, ifdOffset);
    var imageOffset = entries.Single(e => e.Tag == JpegXrIfd.TAG_IMAGE_OFFSET).Value;
    var imageCount = entries.Single(e => e.Tag == JpegXrIfd.TAG_IMAGE_BYTE_COUNT).Value;

    var decoded = JxrCodestream.Decode(bytes.AsSpan((int)imageOffset, (int)imageCount));

    Assert.Multiple(() => {
      Assert.That(decoded.width, Is.EqualTo(10));
      Assert.That(decoded.height, Is.EqualTo(10));
      Assert.That(decoded.r, Is.All.EqualTo(255));
      Assert.That(decoded.g, Is.All.EqualTo(0));
      Assert.That(decoded.b, Is.All.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingThatIsNotAJpegXrAtAll()
    => Assert.Throws<System.IO.InvalidDataException>(() => JpegXrReader.FromBytes(new byte[64]));
}

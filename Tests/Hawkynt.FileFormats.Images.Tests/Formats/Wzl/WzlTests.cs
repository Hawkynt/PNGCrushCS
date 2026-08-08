using System;
using System.IO;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Wzl;

namespace FileFormat.Wzl.Tests;

[TestFixture]
public sealed class WzlTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 11);
      pixels[i * 3 + 1] = (byte)(i * 5);
      pixels[i * 3 + 2] = (byte)i;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WzlReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_PlainBitmap_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => WzlReader.FromBytes(BmpWriter.ToBytes(BmpFile.FromRawImage(_Picture(8, 8)))));

  [Test]
  [Category("Unit")]
  public void FromBytes_TheStatedLengthMustBeTheFilesLength() {
    var data = WzlWriter.ToBytes(WzlFile.FromRawImage(_Picture(16, 8)));
    Array.Resize(ref data, data.Length + 1);

    Assert.Throws<InvalidDataException>(() => WzlReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ScramblesTheFirst256BytesAndNothingAfterThem() {
    var plain = BmpWriter.ToBytes(BmpFile.FromRawImage(_Picture(64, 64)));
    var scrambled = WzlWriter.ToBytes(new() { Bitmap = plain });

    Assert.Multiple(() => {
      Assert.That(scrambled[0], Is.EqualTo((byte)('B' ^ WzlFile.Key)));
      Assert.That(scrambled[1], Is.EqualTo((byte)('M' ^ WzlFile.Key)));
      Assert.That(scrambled.AsSpan(WzlFile.ScrambledLength).SequenceEqual(plain.AsSpan(WzlFile.ScrambledLength)), Is.True,
        "everything past 256 stands as the bitmap wrote it");
      Assert.That(scrambled[WzlFile.ScrambledLength - 1], Is.EqualTo((byte)(plain[WzlFile.ScrambledLength - 1] ^ WzlFile.Key)),
        "the last scrambled byte is the 256th");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThePixelsComeBackByteForByte() {
    var source = _Picture(23, 9);
    var decoded = WzlFile.ToRawImage(WzlReader.FromBytes(WzlWriter.ToBytes(WzlFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(23));
      Assert.That(decoded.Height, Is.EqualTo(9));
      Assert.That(decoded.EnsureFormat(PixelFormat.Rgb24).PixelData.SequenceEqual(source.PixelData), Is.True);
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_APictureSmallerThanTheScrambledRun() {
    var source = _Picture(2, 2);
    var decoded = WzlFile.ToRawImage(WzlReader.FromBytes(WzlWriter.ToBytes(WzlFile.FromRawImage(source))));

    Assert.That(decoded.EnsureFormat(PixelFormat.Rgb24).PixelData.SequenceEqual(source.PixelData), Is.True,
      "a file shorter than 256 bytes is scrambled to its end and comes back whole");
  }
}

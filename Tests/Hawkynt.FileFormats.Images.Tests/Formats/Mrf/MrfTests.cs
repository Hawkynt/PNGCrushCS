using System;
using System.IO;
using FileFormat.Mrf;

namespace FileFormat.Mrf.Tests;

[TestFixture]
public sealed class MrfTests {

  /// <summary>A header for a picture of the given size, with the reserved byte clear.</summary>
  private static byte[] _Header(int width, int height, byte reserved = 0) => [
    (byte)'M', (byte)'R', (byte)'F', (byte)'1',
    (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
    (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
    reserved,
  ];

  private static byte[] _File(int width, int height, params byte[] stream) {
    var header = _Header(width, height);
    var all = new byte[header.Length + stream.Length];
    header.CopyTo(all, 0);
    stream.CopyTo(all, header.Length);
    return all;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MrfReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MrfReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReservedByteSet_ThrowsInvalidDataException() {
    // A non-zero byte twelve is the colour sibling PRF1, which states a depth and a plane count
    // there and is a different picture entirely.
    var data = _Header(64, 64, 0x07);
    Array.Resize(ref data, data.Length + 1);
    data[^1] = 0x80;

    Assert.Throws<InvalidDataException>(() => MrfReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedStream_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MrfReader.FromBytes(_Header(128, 128)));

  [Test]
  [Category("Unit")]
  public void FromBytes_AUniformTileFillsThePicture() {
    // One bit says the sixty-four square is all one colour and the next says which; 0xC0 is both
    // bits set, so the whole tile is white.
    var file = MrfReader.FromBytes(_File(64, 64, 0xC0));
    var decoded = MrfFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(64));
      Assert.That(decoded.Height, Is.EqualTo(64));
      Assert.That(file.PixelData, Has.Length.EqualTo(64 * 64));
      Assert.That(Array.TrueForAll(file.PixelData, p => p == 1), Is.True, "every pixel is the white the tile states");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ThePictureIsCroppedOutOfTheTiledCanvas() {
    // Squares are coded over whole tiles of sixty-four; a ten by five picture is the corner of one.
    var file = MrfReader.FromBytes(_File(10, 5, 0x80));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(10));
      Assert.That(file.Height, Is.EqualTo(5));
      Assert.That(file.PixelData, Has.Length.EqualTo(50));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ASplitSquareGivesItsQuartersInReadingOrder() {
    // 0 says split, then four uniform quarters: white, black, black, white. Packed most significant
    // first that is 0 11 10 10 11 -> 0111 0101 1... = 0x75, 0x80.
    var file = MrfReader.FromBytes(_File(64, 64, 0x75, 0x80));

    Assert.Multiple(() => {
      Assert.That(file.PixelData[0], Is.EqualTo(1), "top left is white");
      Assert.That(file.PixelData[32], Is.EqualTo(0), "top right is black");
      Assert.That(file.PixelData[32 * 64], Is.EqualTo(0), "bottom left is black");
      Assert.That(file.PixelData[32 * 64 + 32], Is.EqualTo(1), "bottom right is white");
    });
  }
}

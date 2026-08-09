using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.NewsRoom.Tests;

/// <summary>
/// The NewsRoom panel and the header it turned out to have.
/// </summary>
/// <remarks>
/// What stood here before checked nothing but a length of 7680 bytes, so any file of that length was
/// read as a 320x192 panel. XnView's own reader has a ten-byte header and states a size in it, and
/// the size it can state does not reach 320 — both sizes are pairs of single-byte coordinates. The
/// fixtures below are built to that header; what stands outside this file is that the same fixture
/// is read by XnView's converter at the size it states, with the bits as they were put in.
/// </remarks>
[TestFixture]
public sealed class NewsRoomReaderTests {

  private const int _WIDTH = 32;
  private const int _HEIGHT = 16;

  private static byte[] _Bits(int width = _WIDTH, int height = _HEIGHT) {
    var stride = NewsRoomFile.StrideOf(width);
    var bits = new byte[stride * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        if ((x / 4 + y / 4) % 2 == 0)
          bits[y * stride + x / 8] |= (byte)(0x80 >> (x % 8));

    return bits;
  }

  private static byte[] _Build(int width = _WIDTH, int height = _HEIGHT, byte[]? bits = null) {
    bits ??= _Bits(width, height);
    var data = new byte[NewsRoomFile.HeaderSize + bits.Length];
    data[0] = NewsRoomFile.Signature[0];
    data[1] = NewsRoomFile.Signature[1];
    data[NewsRoomFile.HeightPairOffset] = 0;
    data[NewsRoomFile.HeightPairOffset + 1] = (byte)height;
    data[NewsRoomFile.WidthPairOffset] = 0;
    data[NewsRoomFile.WidthPairOffset + 1] = (byte)(width - 1);
    data[NewsRoomFile.LowMarkerOffset] = 0x00;
    data[NewsRoomFile.HighMarkerOffset] = 0xFF;
    bits.CopyTo(data, NewsRoomFile.HeaderSize);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NewsRoomReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NewsRoomReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nsr"));
    Assert.Throws<FileNotFoundException>(() => NewsRoomReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NewsRoomReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NewsRoomReader.FromBytes(new byte[8]));

  /// <summary>
  /// The reader this replaced took any file of exactly 7680 bytes as a panel. It is refused now,
  /// because it carries none of the header the format has.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_SevenThousandSixHundredAndEightyBytesOfNothingIsRefused()
    => Assert.Throws<InvalidDataException>(() => NewsRoomReader.FromBytes(new byte[7680]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheOpeningPairIsRefused() {
    var data = _Build();
    data[1] = 0xA1;

    Assert.Throws<InvalidDataException>(() => NewsRoomReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheClosingPairIsRefused() {
    var data = _Build();
    data[NewsRoomFile.HighMarkerOffset] = 0xFE;

    Assert.Throws<InvalidDataException>(() => NewsRoomReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_FewerBitsThanTheStatedSizeIsRefused() {
    var data = _Build();

    Assert.Throws<InvalidDataException>(() => NewsRoomReader.FromBytes(data[..^8]));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_APanelIsReadAtTheSizeItStates() {
    var read = NewsRoomReader.FromBytes(_Build());

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
      Assert.That(read.PixelData, Is.EqualTo(_Bits()));
    });
  }

  /// <summary>Neither size has to land on a byte in the header, and both are rounded up to one.</summary>
  [Test]
  [Category("Integration")]
  public void FromBytes_ASizeThatIsNotAWholeNumberOfBytesIsRoundedUp() {
    var data = _Build();
    data[NewsRoomFile.WidthPairOffset + 1] = 28;
    data[NewsRoomFile.HeightPairOffset + 1] = 13;

    var read = NewsRoomReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(32));
      Assert.That(read.Height, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_ASetBitIsPaper() {
    var image = NewsRoomFile.ToRawImage(NewsRoomReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(image.PaletteCount, Is.EqualTo(2));
      Assert.That(image.PixelData[0], Is.EqualTo(1));
      Assert.That(image.PixelData[4], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ReadsTheSamePanel() {
    using var ms = new MemoryStream(_Build());

    Assert.That(NewsRoomReader.FromStream(ms).Width, Is.EqualTo(_WIDTH));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThroughTheWriter_PreservesTheBits() {
    var file = NewsRoomReader.FromBytes(_Build());

    var reread = NewsRoomReader.FromBytes(NewsRoomWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }
}

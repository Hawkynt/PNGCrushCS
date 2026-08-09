using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Core;
using FileFormat.Oil;

namespace FileFormat.Oil.Tests;

/// <summary>
/// OIL, the format OpenIL had for the year before it became DevIL.
/// </summary>
/// <remarks>
/// There is no sample of this format to check against, so the fixtures are built here to the
/// specification the reader was written from. What stands outside this file is that the same
/// fixtures, in all three of the compressions and all four of the types, are read by XnView's own
/// converter at the size and depth they were built with.
/// </remarks>
[TestFixture]
public sealed class OilTests {

  private const int _WIDTH = 8;
  private const int _HEIGHT = 6;

  /// <summary>Blue, green and red of the picture's pixel at <paramref name="x"/>, <paramref name="y"/>.</summary>
  private static byte[] _Bgr(int x, int y) => [(byte)(x * 20), (byte)(y * 20), (byte)(x * 7 + y * 3)];

  /// <summary>The pixels as the file stores them: interleaved, and from the bottom row upwards.</summary>
  private static byte[] _StoredPixels() {
    var data = new byte[_WIDTH * _HEIGHT * 3];
    var at = 0;
    for (var row = 0; row < _HEIGHT; ++row) {
      var y = _HEIGHT - 1 - row;
      for (var x = 0; x < _WIDTH; ++x) {
        var bgr = _Bgr(x, y);
        data[at++] = bgr[0];
        data[at++] = bgr[1];
        data[at++] = bgr[2];
      }
    }

    return data;
  }

  private static byte[] _Build(
    byte[] payload,
    byte type = OilFile.TypeBgr,
    byte channels = 3,
    byte compression = OilFile.CompressionNone,
    byte[]? palette = null,
    uint depth = 1,
    byte bytesPerChannel = 1,
    int width = _WIDTH,
    int height = _HEIGHT) {

    var extra = palette == null ? [] : new byte[4 + palette.Length];
    if (palette != null) {
      BinaryPrimitives.WriteUInt32LittleEndian(extra, (uint)palette.Length);
      palette.CopyTo(extra, 4);
    }

    var image = new byte[OilFile.ImageHeaderSize + extra.Length + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(image, (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(4), (uint)height);
    BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(8), depth);
    image[12] = channels;
    image[13] = bytesPerChannel;
    image[14] = type;
    image[15] = compression;
    image[16] = 0;
    BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(17), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(21), (uint)payload.Length);
    extra.CopyTo(image, OilFile.ImageHeaderSize);
    payload.CopyTo(image, OilFile.ImageHeaderSize + extra.Length);

    var directoryOffset = OilFile.HeaderSize;
    var imageOffset = directoryOffset + OilFile.DirectoryEntrySize;
    var file = new byte[imageOffset + image.Length];

    OilFile.Signature.CopyTo(file);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), OilFile.MagicNumber);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(8), OilFile.SupportedVersion);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14), (uint)directoryOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(18), 0);
    Encoding.ASCII.GetBytes(OilFile.HeadString).CopyTo(file, 22);

    Encoding.ASCII.GetBytes("picture").CopyTo(file, directoryOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(directoryOffset + 255), (uint)imageOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(directoryOffset + 259), (uint)image.Length);

    image.CopyTo(file, imageOffset);
    return file;
  }

  /// <summary>The run-length coding the format takes from Targa, counted in pixels.</summary>
  private static byte[] _Pack(byte[] pixels, int channels) {
    var packed = new MemoryStream();
    var count = pixels.Length / channels;
    var at = 0;

    while (at < count) {
      var run = 1;
      while (run < 128 && at + run < count && _Same(pixels, at, at + run, channels))
        ++run;

      if (run > 1) {
        packed.WriteByte((byte)(0x80 | (run - 1)));
        packed.Write(pixels, at * channels, channels);
      } else {
        var literal = 1;
        while (literal < 128 && at + literal < count && !_Same(pixels, at + literal - 1, at + literal, channels))
          ++literal;

        packed.WriteByte((byte)(literal - 1));
        packed.Write(pixels, at * channels, literal * channels);
        run = literal;
      }

      at += run;
    }

    return packed.ToArray();
  }

  private static bool _Same(byte[] pixels, int a, int b, int channels) {
    for (var i = 0; i < channels; ++i)
      if (pixels[a * channels + i] != pixels[b * channels + i])
        return false;

    return true;
  }

  private static byte[] _Deflate(byte[] payload) {
    using var target = new MemoryStream();
    using (var compress = new ZLibStream(target, CompressionLevel.Optimal, leaveOpen: true))
      compress.Write(payload, 0, payload.Length);

    return target.ToArray();
  }

  private static void _AssertIsThePicture(OilFile file) {
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(_WIDTH));
      Assert.That(file.Height, Is.EqualTo(_HEIGHT));
      Assert.That(file.Format, Is.EqualTo(PixelFormat.Rgb24));
    });

    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x) {
      var bgr = _Bgr(x, y);
      var at = (y * _WIDTH + x) * 3;
      Assert.That(new[] { file.PixelData[at], file.PixelData[at + 1], file.PixelData[at + 2] },
        Is.EqualTo(new[] { bgr[2], bgr[1], bgr[0] }), $"pixel {x},{y}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => OilReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_Uncompressed_ReadsTheBlueGreenRedPixelsTheRightWayUp()
    => _AssertIsThePicture(OilReader.FromBytes(_Build(_StoredPixels())));

  [Test]
  [Category("Unit")]
  public void FromBytes_RunLengthCoded_GivesTheSamePicture()
    => _AssertIsThePicture(OilReader.FromBytes(_Build(_Pack(_StoredPixels(), 3), compression: OilFile.CompressionRle)));

  [Test]
  [Category("Unit")]
  public void FromBytes_ZlibCompressed_GivesTheSamePicture()
    => _AssertIsThePicture(OilReader.FromBytes(_Build(_Deflate(_StoredPixels()), compression: OilFile.CompressionZlib)));

  [Test]
  [Category("Unit")]
  public void FromBytes_Luminance_ComesOutGrey() {
    var stored = new byte[_WIDTH * _HEIGHT];
    for (var row = 0; row < _HEIGHT; ++row)
    for (var x = 0; x < _WIDTH; ++x)
      stored[row * _WIDTH + x] = (byte)(x * 8 + (_HEIGHT - 1 - row));

    var file = OilReader.FromBytes(_Build(stored, OilFile.TypeLuminance, channels: 1));

    Assert.Multiple(() => {
      Assert.That(file.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(file.PixelData[0], Is.EqualTo(0), "the bottom row is stored first");
      Assert.That(file.PixelData[1], Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Paletted_TakesThePaletteBlueGreenRedFirst() {
    var palette = new byte[16 * OilFile.PaletteEntrySize];
    for (var i = 0; i < 16; ++i) {
      palette[i * 4] = (byte)(i * 8);
      palette[i * 4 + 1] = (byte)(i * 4);
      palette[i * 4 + 2] = (byte)(i * 2);
      palette[i * 4 + 3] = 255;
    }

    var stored = new byte[_WIDTH * _HEIGHT];
    for (var i = 0; i < stored.Length; ++i)
      stored[i] = (byte)(i % 16);

    var file = OilReader.FromBytes(_Build(stored, OilFile.TypePalette, channels: 1, palette: palette));

    Assert.Multiple(() => {
      Assert.That(file.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(file.PaletteCount, Is.EqualTo(16));
      // Entry three is stored 24, 12, 6 as blue, green, red and comes back the other way round.
      Assert.That(file.Palette![9], Is.EqualTo(6));
      Assert.That(file.Palette[10], Is.EqualTo(12));
      Assert.That(file.Palette[11], Is.EqualTo(24));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NotOil_IsRefused()
    => Assert.Throws<InvalidDataException>(() => OilReader.FromBytes(new byte[OilFile.HeaderSize + 400]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheFormatsOwnDescription_IsRefused() {
    // The description string is where the header ends. A file that opens with the signature and does
    // not carry it is not this format, whatever else it holds.
    var data = _Build(_StoredPixels());
    data[30] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => OilReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DataShorterThanTheSizeCallsFor_IsRefused() {
    var stored = _StoredPixels();
    Array.Resize(ref stored, stored.Length - 3);

    Assert.Throws<InvalidDataException>(() => OilReader.FromBytes(_Build(stored)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ChannelCountDisagreeingWithTheType_IsRefused()
    => Assert.Throws<InvalidDataException>(() => OilReader.FromBytes(_Build(_StoredPixels(), OilFile.TypeBgr, channels: 4)));

  [Test]
  [Category("Unit")]
  public void FromBytes_ADepthOfMoreThanOneSlice_IsRefused()
    => Assert.Throws<InvalidDataException>(() => OilReader.FromBytes(_Build(_StoredPixels(), depth: 2)));

  [Test]
  [Category("Unit")]
  public void FromBytes_LzoCompression_IsRefusedRatherThanGuessedAt()
    => Assert.Throws<InvalidDataException>(() => OilReader.FromBytes(_Build(_StoredPixels(), compression: OilFile.CompressionLzo)));

  [Test]
  [Category("Unit")]
  public void FromBytes_RunLengthCodingThatOverrunsThePicture_IsRefused() {
    // A control byte claiming more pixels than the picture has room for is the failure a reader that
    // trusts its input turns into a picture of somebody else's memory.
    var packed = _Pack(_StoredPixels(), 3);
    packed[0] = 0xFF;

    Assert.Throws<InvalidDataException>(() => OilReader.FromBytes(_Build(packed, compression: OilFile.CompressionRle)));
  }
}

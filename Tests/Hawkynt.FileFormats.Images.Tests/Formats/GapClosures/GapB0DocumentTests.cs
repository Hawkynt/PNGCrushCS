using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.ByLight;
using FileFormat.CImage;
using FileFormat.Core;
using FileFormat.ImnetImage;
using FileFormat.Jpeg;
using FileFormat.LaserData;
using Hawkynt.FileFormats.Images;

namespace FileFormat.GapClosures.Tests;

/// <summary>
/// Covers the four 1990s document-imaging rasters recovered from XnView: LaserData (.lda),
/// IMNET (.imt), CImage (.dsi) and byLight (.bif).
/// </summary>
/// <remarks>
/// Every fixture here is built in code. The CCITT payloads are not: they are the exact bytes
/// XnView's own converter produced for the test bitmap below when asked for a Group 3 and a Group 4
/// TIFF, carried over verbatim so that these tests check the readers against a foreign encoder
/// rather than against a matching encoder of our own. The same bytes, wrapped in the headers built
/// here, were fed back to that converter, which returned the test bitmap unchanged.
/// </remarks>
[TestFixture]
public sealed class GapB0DocumentTests {

  private const int _WIDTH = 64;
  private const int _HEIGHT = 32;
  private const int _BYTES_PER_ROW = _WIDTH / 8;

  /// <summary>The test bitmap: 4x4 pixel blocks in a checkerboard, a set bit being black.</summary>
  private static byte[] _ExpectedPixels() {
    var result = new byte[_BYTES_PER_ROW * _HEIGHT];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x)
        if ((x / 4 + y / 4) % 2 == 1)
          result[y * _BYTES_PER_ROW + (x >> 3)] |= (byte)(0x80 >> (x & 7));

    return result;
  }

  /// <summary>The same bitmap as it sits in a .lda or .dsi file, where a set bit is white.</summary>
  private static byte[] _ExpectedPixelsOnDisk() {
    var pixels = _ExpectedPixels();
    var result = new byte[pixels.Length];
    for (var i = 0; i < pixels.Length; ++i)
      result[i] = (byte)~pixels[i];

    return result;
  }

  private static byte[] _ReverseBits(byte[] data) {
    var result = new byte[data.Length];
    for (var i = 0; i < data.Length; ++i) {
      var value = data[i];
      value = (byte)(((value & 0xF0) >> 4) | ((value & 0x0F) << 4));
      value = (byte)(((value & 0xCC) >> 2) | ((value & 0x33) << 2));
      result[i] = (byte)(((value & 0xAA) >> 1) | ((value & 0x55) << 1));
    }

    return result;
  }

  private static byte[] _Concat(byte[] head, byte[] tail) {
    var result = new byte[head.Length + tail.Length];
    head.CopyTo(result, 0);
    tail.CopyTo(result, head.Length);
    return result;
  }

  /// <summary>A file of a format none of these four readers owns, used for the refusal tests.</summary>
  private static byte[] _ForeignFile() => [
    0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A,
    0x00, 0x00, 0x00, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
    0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00,
  ];

  #region CCITT payloads produced by XnView's converter for the test bitmap

  /// <summary>Group 4 (T.6) coding of the test bitmap, 135 bytes.</summary>
  private static readonly byte[] _GROUP4 = [
    0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    0x26, 0xAC, 0xDB, 0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0x36, 0xCD, 0xBF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    0xFF, 0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    0xFF, 0x26, 0xAC, 0xDB, 0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0x36, 0xCD, 0xBF, 0xFF, 0xFF, 0xFF, 0xFF,
    0xFF, 0xFF, 0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0xFF, 0xFF, 0xFF, 0xFF,
    0xFF, 0xFF, 0x26, 0xAC, 0xDB, 0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0x36, 0xCD, 0xBF, 0xFF, 0xFF, 0xFF,
    0xFF, 0xFF, 0xFF, 0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0xFF, 0xFF, 0xFF,
    0xFF, 0xFF, 0xFF, 0x26, 0xAC, 0xDB, 0x36, 0xCD, 0xB3, 0x6C, 0xDB, 0x36, 0xCD, 0xBF, 0xFF, 0xFF,
    0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x10, 0x01,
  ];

  /// <summary>Group 3 one-dimensional (T.4) coding of the test bitmap, 288 bytes, an EOL per line.</summary>
  private static readonly byte[] _GROUP3 = [
    0x00, 0x1B, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD, 0xB0, 0x01, 0xB7, 0x6E, 0xDD, 0xBB, 0x76, 0xED,
    0xDB, 0x00, 0x1B, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD, 0xB0, 0x01, 0xB7, 0x6E, 0xDD, 0xBB, 0x76,
    0xED, 0xDB, 0x00, 0x13, 0x57, 0x6E, 0xDD, 0xBB, 0x76, 0xED, 0xDB, 0xB0, 0x01, 0x35, 0x76, 0xED,
    0xDB, 0xB7, 0x6E, 0xDD, 0xBB, 0x00, 0x13, 0x57, 0x6E, 0xDD, 0xBB, 0x76, 0xED, 0xDB, 0xB0, 0x01,
    0x35, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD, 0xBB, 0x00, 0x1B, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD,
    0xB0, 0x01, 0xB7, 0x6E, 0xDD, 0xBB, 0x76, 0xED, 0xDB, 0x00, 0x1B, 0x76, 0xED, 0xDB, 0xB7, 0x6E,
    0xDD, 0xB0, 0x01, 0xB7, 0x6E, 0xDD, 0xBB, 0x76, 0xED, 0xDB, 0x00, 0x13, 0x57, 0x6E, 0xDD, 0xBB,
    0x76, 0xED, 0xDB, 0xB0, 0x01, 0x35, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD, 0xBB, 0x00, 0x13, 0x57,
    0x6E, 0xDD, 0xBB, 0x76, 0xED, 0xDB, 0xB0, 0x01, 0x35, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD, 0xBB,
    0x00, 0x1B, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD, 0xB0, 0x01, 0xB7, 0x6E, 0xDD, 0xBB, 0x76, 0xED,
    0xDB, 0x00, 0x1B, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD, 0xB0, 0x01, 0xB7, 0x6E, 0xDD, 0xBB, 0x76,
    0xED, 0xDB, 0x00, 0x13, 0x57, 0x6E, 0xDD, 0xBB, 0x76, 0xED, 0xDB, 0xB0, 0x01, 0x35, 0x76, 0xED,
    0xDB, 0xB7, 0x6E, 0xDD, 0xBB, 0x00, 0x13, 0x57, 0x6E, 0xDD, 0xBB, 0x76, 0xED, 0xDB, 0xB0, 0x01,
    0x35, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD, 0xBB, 0x00, 0x1B, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD,
    0xB0, 0x01, 0xB7, 0x6E, 0xDD, 0xBB, 0x76, 0xED, 0xDB, 0x00, 0x1B, 0x76, 0xED, 0xDB, 0xB7, 0x6E,
    0xDD, 0xB0, 0x01, 0xB7, 0x6E, 0xDD, 0xBB, 0x76, 0xED, 0xDB, 0x00, 0x13, 0x57, 0x6E, 0xDD, 0xBB,
    0x76, 0xED, 0xDB, 0xB0, 0x01, 0x35, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD, 0xBB, 0x00, 0x13, 0x57,
    0x6E, 0xDD, 0xBB, 0x76, 0xED, 0xDB, 0xB0, 0x01, 0x35, 0x76, 0xED, 0xDB, 0xB7, 0x6E, 0xDD, 0xBB,
  ];

  #endregion

  #region LaserData (.lda)

  private static byte[] _BuildLaserData(byte compression, byte fillOrder, byte[] payload) {
    var header = new byte[LaserDataFile.HeaderSize];
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0), 0xDCDC);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), _HEIGHT);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), _WIDTH);
    header[12] = compression;
    header[13] = fillOrder;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), 300);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(18), 150);
    return _Concat(header, payload);
  }

  [Test]
  [Category("Unit")]
  public void LaserData_Uncompressed_ReadsSizeAndPixels() {
    var file = LaserDataReader.FromBytes(_BuildLaserData(0, 1, _ExpectedPixelsOnDisk()));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(_WIDTH));
      Assert.That(file.Height, Is.EqualTo(_HEIGHT));
      Assert.That(file.Compression, Is.EqualTo(LaserDataCompression.Uncompressed));
      Assert.That(file.HorizontalResolution, Is.EqualTo(150));
      Assert.That(file.VerticalResolution, Is.EqualTo(300));
      Assert.That(file.PixelData, Is.EqualTo(_ExpectedPixels()));
    });
  }

  [Test]
  [Category("Unit")]
  public void LaserData_Group4_ReadsPixels() {
    var file = LaserDataReader.FromBytes(_BuildLaserData(5, 1, _GROUP4));

    Assert.Multiple(() => {
      Assert.That(file.Compression, Is.EqualTo(LaserDataCompression.Group4));
      Assert.That(file.PixelData, Is.EqualTo(_ExpectedPixels()));
    });
  }

  [Test]
  [Category("Unit")]
  public void LaserData_Group3_ReadsPixels() {
    var file = LaserDataReader.FromBytes(_BuildLaserData(2, 1, _GROUP3));

    Assert.Multiple(() => {
      Assert.That(file.Compression, Is.EqualTo(LaserDataCompression.Group3));
      Assert.That(file.PixelData, Is.EqualTo(_ExpectedPixels()));
    });
  }

  /// <summary>A zero fill-order byte means the coded bits run the other way round inside each byte.</summary>
  [Test]
  [Category("Unit")]
  public void LaserData_Group4LeastSignificantBitFirst_ReadsPixels() {
    var file = LaserDataReader.FromBytes(_BuildLaserData(5, 0, _ReverseBits(_GROUP4)));

    Assert.Multiple(() => {
      Assert.That(file.IsMostSignificantBitFirst, Is.False);
      Assert.That(file.PixelData, Is.EqualTo(_ExpectedPixels()));
    });
  }

  [Test]
  [Category("Unit")]
  public void LaserData_ToRawImage_PaintsSetBitsBlack() {
    var image = LaserDataFile.ToRawImage(LaserDataReader.FromBytes(_BuildLaserData(0, 1, _ExpectedPixelsOnDisk())));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(_WIDTH));
      Assert.That(image.Height, Is.EqualTo(_HEIGHT));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData[0], Is.EqualTo(255)); // top-left block is white
      Assert.That(image.PixelData[4 * 3], Is.EqualTo(0)); // the block beside it is black
    });
  }

  [Test]
  [Category("Unit")]
  public void LaserData_ForeignFile_Throws()
    => Assert.Throws<InvalidDataException>(() => LaserDataReader.FromBytes(_Concat(_ForeignFile(), new byte[600])));

  [Test]
  [Category("Unit")]
  public void LaserData_Truncated_Throws()
    => Assert.Throws<InvalidDataException>(() => LaserDataReader.FromBytes([0xDC, 0xDC]));

  #endregion

  #region IMNET (.imt)

  private static byte[] _BuildImnet(ushort fillOrder, byte[] payload) {
    var header = new byte[ImnetImageFile.HeaderSize];
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), 0x27433100);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), _HEIGHT);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), _BYTES_PER_ROW);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), 200);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(18), fillOrder);
    return _Concat(header, payload);
  }

  [Test]
  [Category("Unit")]
  public void Imnet_Group4_ReadsSizeAndPixels() {
    var file = ImnetImageReader.FromBytes(_BuildImnet(0, _GROUP4));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(_WIDTH));
      Assert.That(file.Height, Is.EqualTo(_HEIGHT));
      Assert.That(file.Resolution, Is.EqualTo(200));
      Assert.That(file.IsMostSignificantBitFirst, Is.True);
      Assert.That(file.PixelData, Is.EqualTo(_ExpectedPixels()));
    });
  }

  [Test]
  [Category("Unit")]
  public void Imnet_Group4LeastSignificantBitFirst_ReadsPixels() {
    var file = ImnetImageReader.FromBytes(_BuildImnet(1, _ReverseBits(_GROUP4)));

    Assert.Multiple(() => {
      Assert.That(file.IsMostSignificantBitFirst, Is.False);
      Assert.That(file.PixelData, Is.EqualTo(_ExpectedPixels()));
    });
  }

  [Test]
  [Category("Unit")]
  public void Imnet_ToRawImage_PaintsSetBitsBlack() {
    var image = ImnetImageFile.ToRawImage(ImnetImageReader.FromBytes(_BuildImnet(0, _GROUP4)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(_WIDTH));
      Assert.That(image.Height, Is.EqualTo(_HEIGHT));
      Assert.That(image.PixelData[0], Is.EqualTo(255));
      Assert.That(image.PixelData[4 * 3], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void Imnet_ForeignFile_Throws()
    => Assert.Throws<InvalidDataException>(() => ImnetImageReader.FromBytes(_ForeignFile()));

  #endregion

  #region CImage (.dsi)

  private static byte[] _BuildCImage(byte compression, byte[] payload) {
    var header = new byte[CImageFile.HeaderSize];
    header[0] = (byte)'D';
    header[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(CImageFile.HorizontalResolutionOffset), 150);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(CImageFile.VerticalResolutionOffset), 300);
    header[CImageFile.CompressionOffset] = compression;
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(CImageFile.WidthOffset), _WIDTH);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(CImageFile.WidthOffset + 4), _HEIGHT);
    return _Concat(header, payload);
  }

  [Test]
  [Category("Unit")]
  public void CImage_Uncompressed_ReadsSizeAndPixels() {
    var file = CImageReader.FromBytes(_BuildCImage(0, _ExpectedPixelsOnDisk()));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(_WIDTH));
      Assert.That(file.Height, Is.EqualTo(_HEIGHT));
      Assert.That(file.IsGroup4, Is.False);
      Assert.That(file.HorizontalResolution, Is.EqualTo(150));
      Assert.That(file.VerticalResolution, Is.EqualTo(300));
      Assert.That(file.PixelData, Is.EqualTo(_ExpectedPixels()));
    });
  }

  [Test]
  [Category("Unit")]
  public void CImage_Group4_ReadsPixels() {
    var file = CImageReader.FromBytes(_BuildCImage(1, _GROUP4));

    Assert.Multiple(() => {
      Assert.That(file.IsGroup4, Is.True);
      Assert.That(file.PixelData, Is.EqualTo(_ExpectedPixels()));
    });
  }

  [Test]
  [Category("Unit")]
  public void CImage_ToRawImage_PaintsSetBitsBlack() {
    var image = CImageFile.ToRawImage(CImageReader.FromBytes(_BuildCImage(1, _GROUP4)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(_WIDTH));
      Assert.That(image.Height, Is.EqualTo(_HEIGHT));
      Assert.That(image.PixelData[0], Is.EqualTo(255));
      Assert.That(image.PixelData[4 * 3], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void CImage_ForeignFile_Throws()
    => Assert.Throws<InvalidDataException>(() => CImageReader.FromBytes(_Concat(_ForeignFile(), new byte[200])));

  #endregion

  #region byLight (.bif)

  private static byte[] _BuildByLight(byte[] payload) {
    var header = new byte[ByLightFile.HeaderSize];
    header[0] = 0xFA;
    header[1] = 0xBA;
    header[3] = 0x04;
    return _Concat(header, payload);
  }

  private static byte[] _SmallJpeg() {
    var rgb = new byte[16 * 16 * 3];
    for (var y = 0; y < 16; ++y)
      for (var x = 0; x < 16; ++x) {
        var offset = (y * 16 + x) * 3;
        rgb[offset] = (byte)(x * 16);
        rgb[offset + 1] = (byte)(y * 16);
        rgb[offset + 2] = 0x40;
      }

    return JpegWriter.ToBytes(JpegFile.FromRawImage(new() {
      Width = 16,
      Height = 16,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    }));
  }

  [Test]
  [Category("Unit")]
  public void ByLight_CarriesTheJpegThatFollowsTheRecord() {
    var jpeg = _SmallJpeg();
    var file = ByLightReader.FromBytes(_BuildByLight(jpeg));

    Assert.Multiple(() => {
      Assert.That(file.Header, Has.Length.EqualTo(ByLightFile.HeaderSize));
      Assert.That(file.JpegData, Is.EqualTo(jpeg));
    });
  }

  [Test]
  [Category("Unit")]
  public void ByLight_ToRawImage_DecodesTheEmbeddedJpeg() {
    var image = ByLightFile.ToRawImage(ByLightReader.FromBytes(_BuildByLight(_SmallJpeg())));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(16));
      Assert.That(image.Height, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void ByLight_ForeignFile_Throws()
    => Assert.Throws<InvalidDataException>(() => ByLightReader.FromBytes(_Concat(_ForeignFile(), new byte[400])));

  /// <summary>The record must be followed by a JPEG; XnView refuses the file otherwise.</summary>
  [Test]
  [Category("Unit")]
  public void ByLight_WithoutJpegPayload_Throws()
    => Assert.Throws<InvalidDataException>(() => ByLightReader.FromBytes(_BuildByLight(new byte[512])));

  #endregion

  #region reader plumbing

  [Test]
  [Category("Unit")]
  public void Readers_Null_ThrowArgumentNullException()
    => Assert.Multiple(() => {
      Assert.Throws<ArgumentNullException>(() => LaserDataReader.FromBytes(null!));
      Assert.Throws<ArgumentNullException>(() => ImnetImageReader.FromBytes(null!));
      Assert.Throws<ArgumentNullException>(() => CImageReader.FromBytes(null!));
      Assert.Throws<ArgumentNullException>(() => ByLightReader.FromBytes(null!));
      Assert.Throws<ArgumentNullException>(() => LaserDataReader.FromFile(null!));
      Assert.Throws<ArgumentNullException>(() => ImnetImageReader.FromFile(null!));
      Assert.Throws<ArgumentNullException>(() => CImageReader.FromFile(null!));
      Assert.Throws<ArgumentNullException>(() => ByLightReader.FromFile(null!));
      Assert.Throws<ArgumentNullException>(() => LaserDataReader.FromStream(null!));
      Assert.Throws<ArgumentNullException>(() => ImnetImageReader.FromStream(null!));
      Assert.Throws<ArgumentNullException>(() => CImageReader.FromStream(null!));
      Assert.Throws<ArgumentNullException>(() => ByLightReader.FromStream(null!));
    });

  [Test]
  [Category("Unit")]
  public void Readers_FromStream_MatchFromBytes() {
    var lda = _BuildLaserData(5, 1, _GROUP4);
    using var stream = new MemoryStream(lda);

    Assert.That(LaserDataReader.FromStream(stream).PixelData, Is.EqualTo(_ExpectedPixels()));
  }

  /// <summary>Registration is by source generator, so the four names have to turn up on their own.</summary>
  [Test]
  [Category("Unit")]
  public void Readers_AreRegisteredUnderTheirOwnExtensions()
    => Assert.Multiple(() => {
      Assert.That(FormatRegistry.PrimaryExtension(ImageFormat.LaserData), Is.EqualTo(".lda"));
      Assert.That(FormatRegistry.PrimaryExtension(ImageFormat.ImnetImage), Is.EqualTo(".imt"));
      Assert.That(FormatRegistry.PrimaryExtension(ImageFormat.CImage), Is.EqualTo(".dsi"));
      Assert.That(FormatRegistry.PrimaryExtension(ImageFormat.ByLight), Is.EqualTo(".bif"));
    });

  #endregion

}

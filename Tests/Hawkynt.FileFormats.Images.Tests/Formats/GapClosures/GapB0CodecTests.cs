using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using FileFormat.Autologic;
using FileFormat.ChinonEs1000;
using FileFormat.Core;
using FileFormat.FlashImage;

namespace FileFormat.GapClosures.Tests;

/// <summary>
/// Three formats whose payloads are coded rather than raw, all three recovered from XnView's own
/// converter and checked against it: Flash Image (.fi), Autologic (.gm, .gm2, .gm4) and the Chinon
/// ES-1000 (.cmt).
/// </summary>
/// <remarks>
/// Every fixture is built here in code; nothing is loaded from disk. The numbers the Chinon tests
/// compare against were taken from nconvert 7.300's own PNM output for the same fixture, so they
/// are the converter's answer and not this reader's.
/// </remarks>
[TestFixture]
public sealed class GapB0CodecTests {

  #region Flash Image (.fi)

  /// <summary>Builds a Flash Image the way the format carries one: a twenty byte header and a zlib
  /// stream holding the palette first and then the rows, each padded up to a multiple of four.</summary>
  private static byte[] _BuildFlashImage(int width, int height, byte[] palette, int paletteCount, byte[] indices, int mode = 0) {
    var stride = FlashImageFile.RowStride(width);
    var raw = new byte[paletteCount * 3 + stride * height];
    palette.AsSpan(0, paletteCount * 3).CopyTo(raw);
    for (var y = 0; y < height; ++y)
      indices.AsSpan(y * width, width).CopyTo(raw.AsSpan(paletteCount * 3 + y * stride));

    var payload = new MemoryStream();
    using (var zlib = new ZLibStream(payload, CompressionLevel.SmallestSize, true))
      zlib.Write(raw, 0, raw.Length);

    var file = new byte[FlashImageFile.HeaderSize + (int)payload.Length];
    FlashImageFile.Magic.CopyTo(file);
    file[4] = (byte)(width >> 8);
    file[5] = (byte)width;
    file[6] = (byte)(height >> 8);
    file[7] = (byte)height;
    file[8] = (byte)(mode >> 8);
    file[9] = (byte)mode;
    file[14] = (byte)(paletteCount >> 8);
    file[15] = (byte)paletteCount;
    payload.GetBuffer().AsSpan(0, (int)payload.Length).CopyTo(file.AsSpan(FlashImageFile.HeaderSize));
    return file;
  }

  private static byte[] _RampPalette(int count) {
    var palette = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      palette[i * 3 + 0] = (byte)(i * 7);
      palette[i * 3 + 1] = (byte)(i * 13 + 40);
      palette[i * 3 + 2] = (byte)(255 - i * 5);
    }

    return palette;
  }

  [Test]
  [Category("Integration")]
  public void FlashImage_Zlib_PaletteThenRows_ReadsBackWhatWasEncoded() {
    const int width = 7;
    const int height = 5;
    const int colours = 256;
    var palette = _RampPalette(colours);
    var indices = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        indices[y * width + x] = (byte)((x * 3 + y * 5) % colours);

    var file = FlashImageReader.FromBytes(_BuildFlashImage(width, height, palette, colours, indices));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(file.PaletteCount, Is.EqualTo(colours));
      Assert.That(file.PixelData, Is.EqualTo(indices));
      Assert.That(file.Palette, Is.EqualTo(palette));
    });


    var image = FlashImageFile.ToRawImage(file);
    var rgb = image.EnsureFormat(PixelFormat.Rgb24).PixelData;
    var expected = new byte[width * height * 3];
    for (var i = 0; i < indices.Length; ++i)
      palette.AsSpan(indices[i] * 3, 3).CopyTo(expected.AsSpan(i * 3));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(width));
      Assert.That(image.Height, Is.EqualTo(height));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(rgb, Is.EqualTo(expected));
    });
  }

  [Test]
  [Category("Integration")]
  public void FlashImage_RowPaddingIsDroppedNotShownAsPixels() {
    // A width of five pads every row out to eight bytes; the three filler bytes must not appear.
    const int width = 5;
    const int height = 3;
    var palette = _RampPalette(4);
    var indices = new byte[width * height];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)(i % 4);

    var built = _BuildFlashImage(width, height, palette, 4, indices);
    var file = FlashImageReader.FromBytes(built);

    Assert.Multiple(() => {
      Assert.That(FlashImageFile.RowStride(width), Is.EqualTo(8));
      Assert.That(file.PixelData, Is.EqualTo(indices));
    });
  }

  [Test]
  [Category("Integration")]
  public void FlashImage_WriterRoundTripsThroughItsOwnReader() {
    const int width = 11;
    const int height = 4;
    var palette = _RampPalette(16);
    var indices = new byte[width * height];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)(i % 16);

    var written = FlashImageWriter.ToBytes(new() {
      Width = width, Height = height, Mode = 0,
      Palette = palette, PaletteCount = 16, PixelData = indices,
    });
    var reread = FlashImageReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(written[..4], Is.EqualTo(FlashImageFile.Magic.ToArray()));
      Assert.That(reread.Width, Is.EqualTo(width));
      Assert.That(reread.Height, Is.EqualTo(height));
      Assert.That(reread.PixelData, Is.EqualTo(indices));
      Assert.That(reread.Palette[..palette.Length], Is.EqualTo(palette));
    });
  }

  [Test]
  [Category("Integration")]
  public void FlashImage_AnIndexPastTheStatedPaletteReadsOnIntoTheRows() {
    // The converter installs 256 entries whatever the header's count says, so the fifth and sixth
    // colours of a four colour file are the first row's own bytes. These are nconvert's numbers.
    byte[] palette = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120];
    byte[] indices = [0, 1, 4, 5];
    var file = FlashImageReader.FromBytes(_BuildFlashImage(4, 1, palette, 4, indices));
    var rgb = FlashImageFile.ToRawImage(file).EnsureFormat(PixelFormat.Rgb24).PixelData;

    Assert.That(rgb, Is.EqualTo(new byte[] { 10, 20, 30, 40, 50, 60, 0, 1, 4, 5, 0, 0 }));
  }

  [Test]
  [Category("Integration")]
  public void FlashImage_ModeOneCarriesAJpegAt598AndTakesItsSizeFromIt() {
    var source = new RawImage {
      Width = 24, Height = 16, Format = PixelFormat.Rgb24, PixelData = new byte[24 * 16 * 3],
    };
    for (var i = 0; i < source.PixelData.Length; ++i)
      source.PixelData[i] = (byte)(i * 5);

    var jpeg = Jpeg.JpegWriter.ToBytes(Jpeg.JpegFile.FromRawImage(source));
    var container = new byte[FlashImageFile.JpegPayloadOffset + jpeg.Length];
    FlashImageFile.Magic.CopyTo(container);
    // The header still states a size, and the reader in XnView ignores it in this mode.
    container[5] = 3;
    container[7] = 3;
    container[9] = 1;
    jpeg.CopyTo(container.AsSpan(FlashImageFile.JpegPayloadOffset));

    var file = FlashImageReader.FromBytes(container);
    var image = FlashImageFile.ToRawImage(file);
    var direct = Jpeg.JpegFile.ToRawImage(Jpeg.JpegReader.FromBytes(jpeg));

    Assert.Multiple(() => {
      Assert.That(FlashImageFile.JpegPayloadOffset, Is.EqualTo(598));
      Assert.That(file.Mode, Is.EqualTo(1));
      Assert.That(image.Width, Is.EqualTo(24));
      Assert.That(image.Height, Is.EqualTo(16));
      Assert.That(image.PixelData, Is.EqualTo(direct.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FlashImage_RefusesAFractalFileOfTheSameExtension() {
    // SURPRISE.FI, the one file with this extension that could be found, is an Iterated Systems
    // fractal transform file: it opens with FTC and a zero and carries its size little-endian.
    // XnView refuses it and so must this, on the four bytes at the front.
    var foreign = new byte[64];
    foreign[0] = (byte)'F';
    foreign[1] = (byte)'T';
    foreign[2] = (byte)'C';
    foreign[3] = 0x00;
    foreign[8] = 0x80;
    foreign[9] = 0x02;

    Assert.Throws<InvalidDataException>(() => FlashImageReader.FromBytes(foreign));
  }

  [Test]
  [Category("Unit")]
  public void FlashImage_RefusesAPayloadThatIsNotDeflate() {
    var file = new byte[FlashImageFile.HeaderSize + 16];
    FlashImageFile.Magic.CopyTo(file);
    file[5] = 4;
    file[7] = 2;
    file[15] = 2;
    Assert.Throws<InvalidDataException>(() => FlashImageReader.FromBytes(file));
  }

  #endregion

  #region Autologic (.gm, .gm2, .gm4)

  /// <summary>Builds an Autologic bitmap: the fourteen byte opening record, then one data record
  /// holding either raw samples or the line-art byte pairs.</summary>
  private static byte[] _BuildAutologic(int width, int height, int levels, byte[] payload, int tag = AutologicFile.DataRecordTag) {
    if ((payload.Length & 1) != 0)
      payload = [.. payload, (byte)0];

    var file = new byte[AutologicFile.HeaderSize + 4 + payload.Length];
    AutologicFile.Magic.CopyTo(file);
    file[4] = (byte)(width >> 8);
    file[5] = (byte)width;
    file[6] = (byte)(height >> 8);
    file[7] = (byte)height;
    file[17] = (byte)levels;
    file[18] = (byte)(tag >> 8);
    file[19] = (byte)tag;
    file[20] = (byte)(payload.Length / 2 >> 8);
    file[21] = (byte)(payload.Length / 2);
    payload.CopyTo(file.AsSpan(AutologicFile.HeaderSize + 4));
    return file;
  }

  /// <summary>Builds an Autologic bitmap out of several data records, each with its own tag.</summary>
  private static byte[] _BuildAutologicRecords(int width, int height, int levels, (int Tag, byte[] Payload)[] records) {
    var body = new List<byte>();
    foreach (var (tag, payload) in records) {
      var even = (payload.Length & 1) != 0 ? [.. payload, (byte)0] : payload;
      body.Add((byte)(tag >> 8));
      body.Add((byte)tag);
      body.Add((byte)(even.Length / 2 >> 8));
      body.Add((byte)(even.Length / 2));
      body.AddRange(even);
    }

    var file = new byte[AutologicFile.HeaderSize + body.Count];
    AutologicFile.Magic.CopyTo(file);
    file[4] = (byte)(width >> 8);
    file[5] = (byte)width;
    file[6] = (byte)(height >> 8);
    file[7] = (byte)height;
    file[17] = (byte)levels;
    body.CopyTo(file, AutologicFile.HeaderSize);
    return file;
  }

  private static byte[] _ExpectedGrey(byte[] samples, int levels) {
    var top = (1 << AutologicFile.BitsForLevels(levels)) - 1;
    var grey = new byte[samples.Length];
    for (var i = 0; i < grey.Length; ++i)
      grey[i] = (byte)((top - Math.Min((int)samples[i], top)) * 255 / top);

    return grey;
  }

  [Test]
  [Category("Integration")]
  public void Autologic_BytePairs_SampleThenCount() {
    // 03 then 84 is the sample 3 five times; 05 on its own is one pixel of 5.
    const int width = 8;
    const int height = 2;
    byte[] payload = [0x03, 0x84, 0x05, 0x01, 0x07, 0x00, 0x02, 0x87];
    var file = AutologicReader.FromBytes(_BuildAutologic(width, height, 16, payload));

    byte[] expected = [
      3, 3, 3, 3, 3, 5, 1, 7,
      0, 2, 2, 2, 2, 2, 2, 2,
    ];

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(file.BitsPerPixel, Is.EqualTo(4));
      Assert.That(file.PixelData, Is.EqualTo(expected));
    });

    var image = AutologicFile.ToRawImage(file);
    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(image.PixelData, Is.EqualTo(_ExpectedGrey(expected, 16)));
    });
  }

  [Test]
  [Category("Integration")]
  public void Autologic_ACountWithNoSampleBeforeItRepeatsWhatWasWrittenLast() {
    // The very first byte being a count repeats the nought the picture starts on; later on a
    // count that opens a record repeats the sample the record before it ended with.
    const int width = 4;
    const int height = 2;
    byte[] payload = [0x83, 0x07, 0x81, 0x82];
    var file = AutologicReader.FromBytes(_BuildAutologic(width, height, 200, payload));

    byte[] expected = [
      0, 0, 0, 0,
      7, 7, 7, 7,
    ];
    Assert.That(file.PixelData, Is.EqualTo(expected));
  }

  [Test]
  [Category("Integration")]
  public void Autologic_ARunIsCutAtTheEndOfItsRowAndNeverWraps() {
    const int width = 4;
    const int height = 3;
    byte[] payload = [0x01, 0x8F, 0x02, 0x8F, 0x03, 0x8F];
    var file = AutologicReader.FromBytes(_BuildAutologic(width, height, 200, payload));

    byte[] expected = [
      1, 1, 1, 1,
      2, 2, 2, 2,
      3, 3, 3, 3,
    ];
    Assert.That(file.PixelData, Is.EqualTo(expected));
  }

  [Test]
  [Category("Integration")]
  public void Autologic_LevelByteGivesTheDepthAndTheGreyRunsTheOtherWay() {
    (int Levels, int Bits)[] cases = [
      (0, 8), (1, 8), (2, 1), (3, 2), (4, 2), (8, 3), (16, 4),
      (32, 5), (64, 6), (128, 7), (129, 8), (200, 8), (255, 8),
    ];

    Assert.Multiple(() => {
      foreach (var (levels, bits) in cases)
        Assert.That(AutologicFile.BitsForLevels(levels), Is.EqualTo(bits), $"levels {levels}");
    });

    // Two levels: nought is the blank medium and one is full ink.
    var oneBit = AutologicReader.FromBytes(_BuildAutologic(4, 1, 2, [0x00, 0x81, 0x01, 0x81]));
    Assert.That(AutologicFile.ToRawImage(oneBit).PixelData, Is.EqualTo(new byte[] { 255, 255, 0, 0 }));

    // Four levels: the three steps land on 255, 170, 85 and 0.
    var twoBit = AutologicReader.FromBytes(_BuildAutologic(4, 1, 4, [0x00, 0x01, 0x02, 0x03]));
    Assert.That(AutologicFile.ToRawImage(twoBit).PixelData, Is.EqualTo(new byte[] { 255, 170, 85, 0 }));
  }

  [Test]
  [Category("Integration")]
  public void Autologic_LevelByte255CarriesRawEightBitSamples() {
    const int width = 4;
    const int height = 2;
    byte[] samples = [0x00, 0x40, 0x80, 0xC0, 0xFF, 0x7F, 0x01, 0xFE];
    var file = AutologicReader.FromBytes(_BuildAutologic(width, height, AutologicFile.RawLevels, samples, tag: 0x1234));

    Assert.Multiple(() => {
      Assert.That(file.BitsPerPixel, Is.EqualTo(8));
      Assert.That(file.PixelData, Is.EqualTo(samples));
      // The plain form does not check the record tag, which is why 0x1234 above is accepted.
      Assert.That(AutologicFile.ToRawImage(file).PixelData,
                  Is.EqualTo(new byte[] { 0xFF, 0xBF, 0x7F, 0x3F, 0x00, 0x80, 0xFE, 0x01 }));
    });
  }

  [Test]
  [Category("Integration")]
  public void Autologic_WriterRoundTripsBothForms() {
    const int width = 13;
    const int height = 5;
    var coded = new byte[width * height];
    var plain = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        coded[y * width + x] = (byte)((x / 3 + y) % 16);
        plain[y * width + x] = (byte)((x * 17 + y * 5) % 256);
      }

    var codedFile = AutologicReader.FromBytes(AutologicWriter.ToBytes(new() {
      Width = width, Height = height, Levels = 16, PixelData = coded,
    }));
    var plainFile = AutologicReader.FromBytes(AutologicWriter.ToBytes(new() {
      Width = width, Height = height, Levels = AutologicFile.RawLevels, PixelData = plain,
    }));

    Assert.Multiple(() => {
      Assert.That(codedFile.PixelData, Is.EqualTo(coded));
      Assert.That(codedFile.BitsPerPixel, Is.EqualTo(4));
      Assert.That(plainFile.PixelData, Is.EqualTo(plain));
      Assert.That(plainFile.BitsPerPixel, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void Autologic_RefusesAForeignFile() {
    var flashImage = _BuildFlashImage(4, 2, _RampPalette(4), 4, new byte[8]);
    Assert.Throws<InvalidDataException>(() => AutologicReader.FromBytes(flashImage));
  }

  [Test]
  [Category("Integration")]
  public void Autologic_ARecordTaggedWrongIsStillDecodedUnlessItLeavesARowShort() {
    // Built both ways and put through nconvert: the one whose pair finishes the row is drawn, the
    // one whose pair stops short of it is refused.
    var finishesTheRow = _BuildAutologicRecords(8, 2, 200, [
      (AutologicFile.DataRecordTag, [0x01, 0x83]),
      (0xFF06, [0x02, 0x83]),
      (AutologicFile.DataRecordTag, [0x03, 0x87]),
    ]);
    var stopsShort = _BuildAutologicRecords(8, 2, 200, [
      (AutologicFile.DataRecordTag, [0x01, 0x81]),
      (0xFF06, [0x02, 0x81]),
      (AutologicFile.DataRecordTag, [0x03, 0x83]),
      (AutologicFile.DataRecordTag, [0x04, 0x87]),
    ]);

    byte[] expected = [
      1, 1, 1, 1, 2, 2, 2, 2,
      3, 3, 3, 3, 3, 3, 3, 3,
    ];

    Assert.Multiple(() => {
      Assert.That(AutologicReader.FromBytes(finishesTheRow).PixelData, Is.EqualTo(expected));
      Assert.Throws<InvalidDataException>(() => AutologicReader.FromBytes(stopsShort));
    });
  }

  [Test]
  [Category("Integration")]
  public void Autologic_ASampleAboveTheDepthKeepsItsBottomBitAtOneBitAndSaturatesAbove() {
    var oneBit = AutologicReader.FromBytes(_BuildAutologic(4, 1, 2, [0x00, 0x01, 0x02, 0x03]));
    var twoBit = AutologicReader.FromBytes(_BuildAutologic(4, 1, 4, [0x00, 0x01, 0x04, 0x05]));

    Assert.Multiple(() => {
      Assert.That(AutologicFile.ToRawImage(oneBit).PixelData, Is.EqualTo(new byte[] { 255, 0, 255, 0 }));
      Assert.That(AutologicFile.ToRawImage(twoBit).PixelData, Is.EqualTo(new byte[] { 255, 170, 0, 0 }));
    });
  }

  #endregion

  #region Chinon ES-1000 (.cmt)

  /// <summary>Builds a .cmt: the 128 byte file header opening with COMET, a blank camera header,
  /// and 243 lines of 512 raw CCD bytes.</summary>
  private static byte[] _BuildChinon(Func<int, int, byte> cell) {
    var file = new byte[ChinonEs1000File.FileSize];
    ChinonEs1000File.Magic.CopyTo(file);
    var at = ChinonEs1000File.FileHeaderSize + ChinonEs1000File.CameraHeaderSize;
    for (var y = 0; y < ChinonEs1000File.CcdLines; ++y)
      for (var x = 0; x < ChinonEs1000File.CcdColumns; ++x)
        file[at + y * ChinonEs1000File.CcdColumns + x] = cell(x, y);

    return file;
  }

  private static string _Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

  [Test]
  [Category("Integration")]
  public void Chinon_RampCcd_MatchesTheConvertersOwnPixels() {
    var file = ChinonEs1000Reader.FromBytes(_BuildChinon((x, y) => (byte)((x * 7 + y * 3) % 256)));
    var image = ChinonEs1000File.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(500));
      Assert.That(image.Height, Is.EqualTo(241));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData.Length, Is.EqualTo(500 * 241 * 3));
      // Taken from nconvert 7.300's PNM output for this same fixture.
      Assert.That(image.PixelData[..9], Is.EqualTo(new byte[] { 59, 53, 21, 53, 61, 34, 62, 76, 52 }));
      Assert.That(image.PixelData[^9..], Is.EqualTo(new byte[] { 100, 145, 123, 107, 151, 125, 110, 151, 122 }));
      Assert.That(_Sha256(image.PixelData),
                  Is.EqualTo("ffb5639cd772db23488482c7d5b2aa46e64b4eba868eb77e15393acd3235f2ab"));
    });
  }

  [Test]
  [Category("Integration")]
  public void Chinon_ChequerboardCcd_MatchesTheConvertersOwnPixels() {
    var file = ChinonEs1000Reader.FromBytes(_BuildChinon((x, y) => (byte)((((x >> 2) + (y >> 2)) & 1) != 0 ? 200 : 40)));
    var image = ChinonEs1000File.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.PixelData[..9], Is.EqualTo(new byte[] { 32, 91, 72, 13, 87, 97, 143, 246, 225 }));
      Assert.That(_Sha256(image.PixelData),
                  Is.EqualTo("94f29ea487da74aa12ca625b9f7882506968ea752f94f706322d645bf1c042f4"));
    });
  }

  [Test]
  [Category("Integration")]
  public void Chinon_DarkCcd_MatchesTheConvertersOwnPixels() {
    // Six levels of near-darkness: the histogram, the saturation and the gamma all run on almost
    // nothing here, which is where a decoder that only agrees with itself gives itself away.
    var file = ChinonEs1000Reader.FromBytes(_BuildChinon((x, y) => (byte)((unchecked((uint)(x * 1664525 + y * 1013904223)) >> 13) % 6)));
    var image = ChinonEs1000File.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.PixelData[..9], Is.EqualTo(new byte[] { 5, 8, 14, 13, 6, 0, 0, 4, 14 }));
      Assert.That(_Sha256(image.PixelData),
                  Is.EqualTo("2c8296605c127b3278b06d75c359a1a1bab51287423fc4e99d00ed215bf33c2d"));
    });
  }

  [Test]
  [Category("Integration")]
  public void Chinon_BlankCcd_StillMatchesTheConverter() {
    // A CCD of nothing divides nothing by nothing all the way through, so what comes out is not
    // black; it is whatever that arithmetic leaves behind, and the converter leaves this.
    var file = ChinonEs1000Reader.FromBytes(_BuildChinon((_, _) => 0));
    var image = ChinonEs1000File.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.PixelData[..9], Is.EqualTo(new byte[] { 0, 0, 255, 0, 0, 255, 0, 0, 255 }));
      Assert.That(_Sha256(image.PixelData),
                  Is.EqualTo("455cb421e431ef94969b3096bdafd951306236bb717bcdde17ca745e56e7bfdd"));
    });
  }

  [Test]
  [Category("Unit")]
  public void Chinon_RefusesAnythingThatIsNotExactlyTheCamerasLength() {
    var shortFile = new byte[ChinonEs1000File.FileSize - 1];
    ChinonEs1000File.Magic.CopyTo(shortFile);
    var longFile = new byte[ChinonEs1000File.FileSize + 1];
    ChinonEs1000File.Magic.CopyTo(longFile);

    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => ChinonEs1000Reader.FromBytes(shortFile));
      Assert.Throws<InvalidDataException>(() => ChinonEs1000Reader.FromBytes(longFile));
    });
  }

  [Test]
  [Category("Unit")]
  public void Chinon_RefusesAFileOfTheRightLengthWithTheWrongSignature() {
    var file = _BuildChinon((_, _) => 128);
    file[0] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => ChinonEs1000Reader.FromBytes(file));
  }

  #endregion

  #region Common

  [Test]
  [Category("Unit")]
  public void Readers_RejectNullAndMissingInput() {
    var missingFi = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fi"));
    var missingGm = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gm"));
    var missingCmt = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cmt"));

    Assert.Multiple(() => {
      Assert.Throws<ArgumentNullException>(() => FlashImageReader.FromBytes(null!));
      Assert.Throws<ArgumentNullException>(() => AutologicReader.FromBytes(null!));
      Assert.Throws<ArgumentNullException>(() => ChinonEs1000Reader.FromBytes(null!));
      Assert.Throws<ArgumentNullException>(() => FlashImageReader.FromStream(null!));
      Assert.Throws<ArgumentNullException>(() => AutologicReader.FromStream(null!));
      Assert.Throws<ArgumentNullException>(() => ChinonEs1000Reader.FromStream(null!));
      Assert.Throws<FileNotFoundException>(() => FlashImageReader.FromFile(missingFi));
      Assert.Throws<FileNotFoundException>(() => AutologicReader.FromFile(missingGm));
      Assert.Throws<FileNotFoundException>(() => ChinonEs1000Reader.FromFile(missingCmt));
    });
  }

  [Test]
  [Category("Unit")]
  public void Readers_RefuseEachOthersFiles() {
    var fi = _BuildFlashImage(4, 2, _RampPalette(4), 4, [0, 1, 2, 3, 3, 2, 1, 0]);
    var gm = _BuildAutologic(4, 2, 16, [0x01, 0x83, 0x02, 0x83]);
    var cmt = _BuildChinon((x, y) => (byte)(x + y));

    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => FlashImageReader.FromBytes(gm));
      Assert.Throws<InvalidDataException>(() => FlashImageReader.FromBytes(cmt));
      Assert.Throws<InvalidDataException>(() => AutologicReader.FromBytes(fi));
      Assert.Throws<InvalidDataException>(() => AutologicReader.FromBytes(cmt));
      Assert.Throws<InvalidDataException>(() => ChinonEs1000Reader.FromBytes(fi));
      Assert.Throws<InvalidDataException>(() => ChinonEs1000Reader.FromBytes(gm));
    });
  }

  [Test]
  [Category("Integration")]
  public void Readers_TakeTheSameBytesFromAStream() {
    var fi = _BuildFlashImage(4, 2, _RampPalette(4), 4, [0, 1, 2, 3, 3, 2, 1, 0]);
    var gm = _BuildAutologic(4, 2, 16, [0x01, 0x83, 0x02, 0x83]);

    using var fiStream = new MemoryStream(fi);
    using var gmStream = new MemoryStream(gm);

    Assert.Multiple(() => {
      Assert.That(FlashImageReader.FromStream(fiStream).PixelData, Is.EqualTo(new byte[] { 0, 1, 2, 3, 3, 2, 1, 0 }));
      Assert.That(AutologicReader.FromStream(gmStream).PixelData, Is.EqualTo(new byte[] { 1, 1, 1, 1, 2, 2, 2, 2 }));
    });
  }

  #endregion
}

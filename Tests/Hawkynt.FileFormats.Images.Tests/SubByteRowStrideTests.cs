using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Images.Tests;

/// <summary>
/// Pictures narrower than a byte a pixel, at widths that do not divide by eight.
/// </summary>
/// <remarks>
/// Every format that spends one, two or four bits a pixel starts each row on a fresh byte, because
/// that is what gives a row an address; several pad further still, to a word or to four bytes. An
/// <see cref="PixelFormat.Indexed1"/> or <see cref="PixelFormat.Indexed4"/>
/// <see cref="RawImage"/> does not: its indices run straight on across the picture with nothing
/// between the rows, which is what <see cref="RawImage.MinimumPixelDataLength"/> counts and what
/// <see cref="PixelConverter"/> and <see cref="ColorQuantizer.PackIndices"/> read.
/// <para/>
/// Handing the stored rows over as if they were the continuous form costs nothing on the first row
/// and slides every row after it by the padding — a picture that leans rather than one that is
/// obviously broken, and one that round-trips through the same format's own writer without
/// complaint. Every width used here is deliberately not a multiple of eight, because at a multiple
/// of eight the two layouts are the same bytes and nothing can be told apart.
/// <para/>
/// The fixtures are written out byte by byte to each format's specification rather than produced by
/// this library's own writer, and each was checked against a decoder that shares no code with this
/// one before being committed:
/// <list type="bullet">
/// <item>The icons were read by Windows' own icon handling through GDI+ (<c>System.Drawing.Icon</c>)
/// and the bitmaps by GDI+'s BMP decoder. Both drew exactly the picture asserted here at every
/// width below, at one bit and at four.</item>
/// <item>The PCX and Sun raster fixtures were read by ffmpeg 8.1.2, which drew the same pictures.</item>
/// </list>
/// Before the correction the icon decoder was wrong from the second row down at every one of these
/// widths — 18 of 35 pixels at seven wide and one bit, 28 of 35 at four bits — while agreeing with
/// itself on the way back out.
/// </remarks>
[TestFixture]
public sealed class SubByteRowStrideTests {

  private const int _Height = 5;

  /// <summary>The widths that tell the two layouts apart, none of them a multiple of eight.</summary>
  private static readonly int[] _Widths = [1, 3, 7, 9, 17];

  /// <summary>Sixteen colours far enough apart that a pixel taken from the wrong place shows.</summary>
  private static readonly byte[][] _Palette = [
    [0x00, 0x00, 0x00], [0xFF, 0xFF, 0xFF], [0xFF, 0x00, 0x00], [0x00, 0xFF, 0x00],
    [0x00, 0x00, 0xFF], [0xFF, 0xFF, 0x00], [0xFF, 0x00, 0xFF], [0x00, 0xFF, 0xFF],
    [0x80, 0x00, 0x00], [0x00, 0x80, 0x00], [0x00, 0x00, 0x80], [0x80, 0x80, 0x00],
    [0x80, 0x00, 0x80], [0x00, 0x80, 0x80], [0x80, 0x80, 0x80], [0xC0, 0xC0, 0xC0],
  ];

  /// <summary>A picture whose every row differs from the one above, so a row offset cannot hide.</summary>
  private static int[,] _Intended(int width, int bitsPerPixel) {
    var indices = new int[_Height, width];
    for (var y = 0; y < _Height; ++y)
      for (var x = 0; x < width; ++x)
        indices[y, x] = bitsPerPixel == 1 ? (x + y) % 2 : (x * 3 + y * 7 + 1) % 16;

    return indices;
  }

  /// <summary>The picture laid out as the files store it: a row to a stride, starting on a byte.</summary>
  private static byte[] _StoredRows(int[,] indices, int width, int bitsPerPixel, int stride, bool bottomUp) {
    var rows = new byte[stride * _Height];
    for (var y = 0; y < _Height; ++y) {
      var at = (bottomUp ? _Height - 1 - y : y) * stride;
      for (var x = 0; x < width; ++x) {
        var value = indices[y, x];
        if (bitsPerPixel == 1)
          rows[at + (x >> 3)] |= (byte)(value << (7 - (x & 7)));
        else
          rows[at + (x >> 1)] |= (byte)((x & 1) == 0 ? value << 4 : value);
      }
    }

    return rows;
  }

  private static int _PadTo4(int value) => (value + 3) & ~3;

  private static void _AssertDraws(RawImage image, int[,] indices, int width, string what) {
    Assert.That(image.Width, Is.EqualTo(width), $"{what}: width");
    Assert.That(image.Height, Is.EqualTo(_Height), $"{what}: height");

    var rgb = image.ToRgb24();
    Assert.Multiple(() => {
      for (var y = 0; y < _Height; ++y)
        for (var x = 0; x < width; ++x) {
          var at = (y * width + x) * 3;
          var wanted = _Palette[indices[y, x]];
          Assert.That(
            new[] { rgb[at], rgb[at + 1], rgb[at + 2] },
            Is.EqualTo(wanted),
            $"{what}: pixel {x},{y} of a {width} by {_Height} picture");
        }
    });
  }

  // ── The two layouts, and the conversion between them ─────────────────────

  [Test]
  [Category("Unit")]
  public void DropRowPadding_LeavesNoGapBetweenTheRows() {
    // Three pixels a row at one bit: each stored row spends a whole byte and uses three of its bits.
    var padded = new byte[] { 0b1010_0000, 0b0100_0000 };

    var continuous = PackedRows.DropRowPadding(padded, 3, 2, 1);

    // Six bits back to back: 101 then 010, the last two bits of the byte left clear.
    Assert.That(continuous, Is.EqualTo(new byte[] { 0b1010_1000 }));
  }

  [Test]
  [Category("Unit")]
  public void AddRowPadding_PutsEveryRowBackOnItsOwnByte() {
    var continuous = new byte[] { 0b1010_1000 };

    var padded = PackedRows.AddRowPadding(continuous, 3, 2, 1);

    Assert.That(padded, Is.EqualTo(new byte[] { 0b1010_0000, 0b0100_0000 }));
  }

  [Test]
  [Category("Unit")]
  public void DropRowPadding_TakesAStatedStrideRatherThanTheTightestOne() {
    // A Sun raster pads to a word, so one row of three one-bit pixels costs two bytes, not one.
    var padded = new byte[] { 0b1010_0000, 0x00, 0b0100_0000, 0x00 };

    Assert.That(PackedRows.DropRowPadding(padded, 3, 2, 1, 2), Is.EqualTo(new byte[] { 0b1010_1000 }));
  }

  [Test]
  [Category("Boundary")]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(4)]
  public void RowPadding_RoundTripsAtEveryDepthAndWidth(int bitsPerPixel) {
    var random = new Random(bitsPerPixel * 977);
    Assert.Multiple(() => {
      for (var width = 1; width <= 33; ++width) {
        var pixels = new byte[(width * _Height * bitsPerPixel + 7) / 8];
        random.NextBytes(pixels);

        // The bits past the last pixel are padding of the continuous form's own, and nothing
        // promises to carry them back, so they are cleared before the comparison.
        var used = width * _Height * bitsPerPixel;
        if ((used & 7) != 0)
          pixels[^1] &= (byte)(0xFF << (8 - (used & 7)));

        var padded = PackedRows.AddRowPadding(pixels, width, _Height, bitsPerPixel);
        Assert.That(padded, Has.Length.EqualTo(PackedRows.Stride(width, bitsPerPixel) * _Height), $"width {width}");
        Assert.That(PackedRows.DropRowPadding(padded, width, _Height, bitsPerPixel), Is.EqualTo(pixels), $"width {width}");
      }
    });
  }

  [Test]
  [Category("Exception")]
  public void RowPadding_RefusesADepthThatIsAlreadyWholeBytes()
    => Assert.Multiple(() => {
      Assert.That(() => PackedRows.DropRowPadding(new byte[8], 4, 2, 8), Throws.InstanceOf<ArgumentOutOfRangeException>());
      Assert.That(() => PackedRows.AddRowPadding(new byte[8], 4, 2, 8), Throws.InstanceOf<ArgumentOutOfRangeException>());
    });

  // ── Icons ────────────────────────────────────────────────────────────────

  /// <summary>One entry's worth of bitmap: a BITMAPINFOHEADER, a colour table, the rows, the mask.</summary>
  private static byte[] _IconDib(int[,] indices, int width, int bitsPerPixel) {
    var colours = 1 << bitsPerPixel;
    var rows = _StoredRows(indices, width, bitsPerPixel, _PadTo4((width * bitsPerPixel + 7) / 8), bottomUp: true);
    var mask = new byte[_PadTo4((width + 7) / 8) * _Height];
    var data = new byte[40 + colours * 4 + rows.Length + mask.Length];

    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), width);
    // The stated height covers the picture and the mask below it, which is what makes an icon an icon.
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), _Height * 2);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), (short)bitsPerPixel);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), rows.Length + mask.Length);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(32), colours);

    for (var i = 0; i < colours; ++i) {
      var at = 40 + i * 4;
      data[at] = _Palette[i][2];
      data[at + 1] = _Palette[i][1];
      data[at + 2] = _Palette[i][0];
    }

    rows.CopyTo(data, 40 + colours * 4);
    // The mask is left clear, so every pixel is drawn.
    return data;
  }

  private static byte[] _IconFile(byte[] dib, int width, int bitsPerPixel) {
    var data = new byte[22 + dib.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
    data[6] = (byte)width;
    data[7] = _Height;
    data[8] = (byte)(1 << bitsPerPixel);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), (ushort)bitsPerPixel);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), (uint)dib.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(18), 22);
    dib.CopyTo(data, 22);
    return data;
  }

  private static IEnumerable<int> Widths => _Widths;

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void Ico_TwoColourEntryDrawsWhatWindowsDraws(int width) {
    var indices = _Intended(width, 1);
    var icon = _IconFile(_IconDib(indices, width, 1), width, 1);

    var image = FileFormat.Ico.IcoFile.ToRawImage(FileFormat.Ico.IcoReader.FromBytes(icon));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed1));
    _AssertDraws(image, indices, width, "one-bit icon");
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void Ico_SixteenColourEntryDrawsWhatWindowsDraws(int width) {
    var indices = _Intended(width, 4);
    var icon = _IconFile(_IconDib(indices, width, 4), width, 4);

    var image = FileFormat.Ico.IcoFile.ToRawImage(FileFormat.Ico.IcoReader.FromBytes(icon));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed4));
    _AssertDraws(image, indices, width, "four-bit icon");
  }

  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(Widths))]
  public void Ico_EntryCarriesExactlyAsManyBytesAsThePictureNeeds(int width) {
    var icon = _IconFile(_IconDib(_Intended(width, 1), width, 1), width, 1);

    var image = FileFormat.Ico.IcoFile.ToRawImage(FileFormat.Ico.IcoReader.FromBytes(icon));

    // The length is the whole difference: a padded picture is longer than a continuous one, and a
    // consumer reading it as continuous has no way to tell except by the pixels coming out wrong.
    Assert.That(image.PixelData, Has.Length.EqualTo((width * _Height + 7) / 8));
  }

  // ── Bitmaps ──────────────────────────────────────────────────────────────

  private static byte[] _BmpFile(int[,] indices, int width, int bitsPerPixel) {
    var colours = 1 << bitsPerPixel;
    var rows = _StoredRows(indices, width, bitsPerPixel, _PadTo4((width * bitsPerPixel + 7) / 8), bottomUp: true);
    var offset = 14 + 40 + colours * 4;
    var data = new byte[offset + rows.Length];

    data[0] = (byte)'B';
    data[1] = (byte)'M';
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), offset);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), _Height);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), 1);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), (short)bitsPerPixel);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(34), rows.Length);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(46), colours);

    for (var i = 0; i < colours; ++i) {
      var at = 54 + i * 4;
      data[at] = _Palette[i][2];
      data[at + 1] = _Palette[i][1];
      data[at + 2] = _Palette[i][0];
    }

    rows.CopyTo(data, offset);
    return data;
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void Bmp_TwoColourFileDrawsWhatWindowsDraws(int width) {
    var indices = _Intended(width, 1);

    var image = FileFormat.Bmp.BmpFile.ToRawImage(FileFormat.Bmp.BmpReader.FromBytes(_BmpFile(indices, width, 1)));

    _AssertDraws(image, indices, width, "one-bit bitmap");
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void Bmp_SixteenColourFileDrawsWhatWindowsDraws(int width) {
    var indices = _Intended(width, 4);

    var image = FileFormat.Bmp.BmpFile.ToRawImage(FileFormat.Bmp.BmpReader.FromBytes(_BmpFile(indices, width, 4)));

    _AssertDraws(image, indices, width, "four-bit bitmap");
  }

  // ── PCX ──────────────────────────────────────────────────────────────────

  /// <summary>The run-length coding a PCX scanline is stored in.</summary>
  private static byte[] _PcxRuns(ReadOnlySpan<byte> row) {
    var coded = new List<byte>();
    for (var i = 0; i < row.Length;) {
      var value = row[i];
      var run = 1;
      while (i + run < row.Length && row[i + run] == value && run < 63)
        ++run;

      if (run > 1 || value >= 0xC0)
        coded.Add((byte)(0xC0 | run));

      coded.Add(value);
      i += run;
    }

    return coded.ToArray();
  }

  private static byte[] _PcxFile(int[,] indices, int width, int bitsPerPixel) {
    // A PCX scanline is a whole number of bytes and an even one at that.
    var bytesPerLine = ((width * bitsPerPixel + 7) / 8 + 1) & ~1;
    var rows = _StoredRows(indices, width, bitsPerPixel, bytesPerLine, bottomUp: false);

    var header = new byte[128];
    header[0] = 10;
    header[1] = 5;
    header[2] = 1;
    header[3] = (byte)bitsPerPixel;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)(width - 1));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)(_Height - 1));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 72);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), 72);
    for (var i = 0; i < 16; ++i) {
      var at = 16 + i * 3;
      header[at] = _Palette[i][0];
      header[at + 1] = _Palette[i][1];
      header[at + 2] = _Palette[i][2];
    }
    header[65] = 1;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(66), (ushort)bytesPerLine);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(68), 1);

    using var stream = new MemoryStream();
    stream.Write(header);
    for (var y = 0; y < _Height; ++y)
      stream.Write(_PcxRuns(rows.AsSpan(y * bytesPerLine, bytesPerLine)));

    return stream.ToArray();
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void Pcx_TwoColourFileDrawsWhatFfmpegDraws(int width) {
    var indices = _Intended(width, 1);

    var image = FileFormat.Pcx.PcxFile.ToRawImage(FileFormat.Pcx.PcxReader.FromBytes(_PcxFile(indices, width, 1)));

    _AssertDraws(image, indices, width, "one-bit PCX");
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void Pcx_SixteenColourFileDrawsWhatFfmpegDraws(int width) {
    var indices = _Intended(width, 4);

    var image = FileFormat.Pcx.PcxFile.ToRawImage(FileFormat.Pcx.PcxReader.FromBytes(_PcxFile(indices, width, 4)));

    _AssertDraws(image, indices, width, "four-bit PCX");
  }

  // ── Sun raster ───────────────────────────────────────────────────────────

  private static byte[] _SunRasterFile(int[,] indices, int width) {
    // Two paddings at once: the row ends on a byte and the byte count is rounded up to a word.
    var stride = ((width + 7) / 8 + 1) & ~1;
    var rows = _StoredRows(indices, width, 1, stride, bottomUp: false);

    var data = new byte[32 + rows.Length];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 0x59A66A95);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), width);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), _Height);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), 1);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), rows.Length);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), 1);
    rows.CopyTo(data, 32);
    return data;
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void SunRaster_TwoColourFileDrawsWhatFfmpegDraws(int width) {
    var indices = _Intended(width, 1);
    var image = FileFormat.SunRaster.SunRasterFile.ToRawImage(
      FileFormat.SunRaster.SunRasterReader.FromBytes(_SunRasterFile(indices, width)));

    // A set bit is ink in a bare Sun raster, so index one is black and index nought white — the
    // other way round from the palette the rest of these fixtures use.
    Assert.That(image.Width, Is.EqualTo(width));
    var rgb = image.ToRgb24();
    Assert.Multiple(() => {
      for (var y = 0; y < _Height; ++y)
        for (var x = 0; x < width; ++x) {
          var wanted = indices[y, x] == 0 ? (byte)255 : (byte)0;
          var at = (y * width + x) * 3;
          Assert.That(rgb[at], Is.EqualTo(wanted), $"one-bit Sun raster: pixel {x},{y} of a {width} by {_Height} picture");
        }
    });
  }

  // ── What the writers put back ────────────────────────────────────────────

  /// <summary>The picture as an Indexed1 image, indices running straight on across it.</summary>
  private static RawImage _Continuous(int[,] indices, int width) {
    var pixels = new byte[(width * _Height + 7) / 8];
    for (var y = 0; y < _Height; ++y)
      for (var x = 0; x < width; ++x)
        if (indices[y, x] != 0) {
          var at = y * width + x;
          pixels[at >> 3] |= (byte)(0x80 >> (at & 7));
        }

    return new() {
      Width = width,
      Height = _Height,
      Format = PixelFormat.Indexed1,
      PixelData = pixels,
      Palette = [_Palette[0][0], _Palette[0][1], _Palette[0][2], _Palette[1][0], _Palette[1][1], _Palette[1][2]],
      PaletteCount = 2,
    };
  }

  /// <summary>
  /// The rows a file of this width holds, taken from the picture rather than from a writer.
  /// </summary>
  private static byte[] _ExpectedStoredRows(int[,] indices, int width, int stride)
    => _StoredRows(indices, width, 1, stride, bottomUp: false);

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void Msp_StoresEveryRowOnItsOwnByte(int width) {
    var indices = _Intended(width, 1);

    var file = FileFormat.Msp.MspFile.FromRawImage(_Continuous(indices, width));

    // MSP states ceil(width/8) bytes a row and its own validation insists on exactly that many, so
    // a continuous stream stored here is both the wrong picture and the wrong length.
    Assert.That(file.PixelData, Is.EqualTo(_ExpectedStoredRows(indices, width, (width + 7) / 8)));
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void Msp_ReadsBackTheRowsItStored(int width) {
    var indices = _Intended(width, 1);
    var file = FileFormat.Msp.MspFile.FromRawImage(_Continuous(indices, width));

    var image = FileFormat.Msp.MspFile.ToRawImage(file);

    Assert.That(image.PixelData, Is.EqualTo(_Continuous(indices, width).PixelData));
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void BennetYeeFace_StoresEveryRowOnItsOwnWord(int width) {
    var indices = _Intended(width, 1);

    var file = FileFormat.BennetYeeFace.BennetYeeFaceFile.FromRawImage(_Continuous(indices, width));

    Assert.That(file.PixelData, Is.EqualTo(_ExpectedStoredRows(indices, width, ((width + 15) / 16) * 2)));
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void BennetYeeFace_ReadsBackTheRowsItStored(int width) {
    var indices = _Intended(width, 1);
    var file = FileFormat.BennetYeeFace.BennetYeeFaceFile.FromRawImage(_Continuous(indices, width));

    var image = FileFormat.BennetYeeFace.BennetYeeFaceFile.ToRawImage(file);

    _AssertDraws(image, indices, width, "Bennet Yee face");
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void PrintMaster_StoresEveryRowOnItsOwnByte(int width) {
    var indices = _Intended(width, 1);

    var file = FileFormat.PrintMaster.PrintMasterFile.FromRawImage(_Continuous(indices, width));

    Assert.That(file.PixelData, Is.EqualTo(_ExpectedStoredRows(indices, width, (width + 7) / 8)));
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void Ccitt_StoresEveryRowOnItsOwnByte(int width) {
    var indices = _Intended(width, 1);

    var file = FileFormat.Ccitt.CcittFile.FromRawImage(_Continuous(indices, width));

    // The two coders both walk the page a row at a time at ceil(width/8) bytes, so anything else
    // stored here is coded as a different picture from the one that went in.
    Assert.That(file.PixelData, Is.EqualTo(_ExpectedStoredRows(indices, width, (width + 7) / 8)));
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void Tiff_StoresEveryRowOnItsOwnByte(int width) {
    var indices = _Intended(width, 1);

    var file = FileFormat.Tiff.TiffFile.FromRawImage(_Continuous(indices, width));

    Assert.That(file.PixelData, Is.EqualTo(_ExpectedStoredRows(indices, width, (width + 7) / 8)));
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void Pcx_StoresEveryRowOnItsOwnByte(int width) {
    var indices = _Intended(width, 1);

    var file = FileFormat.Pcx.PcxFile.FromRawImage(_Continuous(indices, width));

    Assert.That(file.PixelData, Is.EqualTo(_ExpectedStoredRows(indices, width, (width + 7) / 8)));
  }

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void SunRaster_StoresEveryRowOnItsOwnWord(int width) {
    var indices = _Intended(width, 1);

    var file = FileFormat.SunRaster.SunRasterFile.FromRawImage(_Continuous(indices, width));

    Assert.That(file.PixelData, Is.EqualTo(_ExpectedStoredRows(indices, width, ((width + 7) / 8 + 1) & ~1)));
  }

  // ── The consumers that read a picture a pixel at a time ──────────────────

  [Test]
  [Category("Integration")]
  [TestCaseSource(nameof(Widths))]
  public void PictureConsumers_ReadTheIndicesWhereTheyActuallyAre(int width) {
    var indices = _Intended(width, 1);
    var picture = _Continuous(indices, width);

    // Each of these used to walk the picture at a per-row stride, which for a width that is not a
    // multiple of eight reads past the end of a buffer that never held the padding. They threw
    // where they did not merely lean, so the assertion is that they produce a picture at all and
    // that it is the right one where the format keeps enough of it to say.
    Assert.Multiple(() => {
      Assert.That(() => FileFormat.CokeAtari.CokeAtariFile.FromRawImage(picture), Throws.Nothing, "Coke");
      Assert.That(() => FileFormat.IffRgbn.IffRgbnFile.FromRawImage(picture), Throws.Nothing, "RGBN");
      Assert.That(() => FileFormat.Rembrandt.RembrandtFile.FromRawImage(picture), Throws.Nothing, "Rembrandt");
      Assert.That(() => FileFormat.RiscOsSprite.RiscOsSpriteFile.FromRawImage(picture), Throws.Nothing, "RISC OS sprite");
      Assert.That(() => FileFormat.SharpX68k.SharpX68kFile.FromRawImage(picture), Throws.Nothing, "X68000");
      Assert.That(() => FileFormat.XvThumbnail.XvThumbnailFile.FromRawImage(picture), Throws.Nothing, "xv thumbnail");
      Assert.That(() => FileFormat.NokiaOperatorLogo.NokiaOperatorLogoFile.FromRawImage(picture), Throws.Nothing, "operator logo");
    });

    // The RGBN sprite keeps full colour, so it can be held to the picture exactly.
    var sprite = FileFormat.RiscOsSprite.RiscOsSpriteFile.FromRawImage(picture);
    var drawn = FileFormat.RiscOsSprite.RiscOsSpriteFile.ToRawImage(sprite);
    _AssertDraws(drawn, indices, width, "RISC OS sprite");
  }
}

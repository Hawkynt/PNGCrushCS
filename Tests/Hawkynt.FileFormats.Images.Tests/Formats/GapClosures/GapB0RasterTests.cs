using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Arn;
using FileFormat.Core;
using FileFormat.CoreIdc;
using FileFormat.Iss;
using FileFormat.Optocat;
using FileFormat.Portrait;
using Hawkynt.FileFormats.Images;

namespace FileFormat.GapClosures.Tests;

/// <summary>
/// Covers five raster formats whose layouts were recovered from XnView's own converter: Optocat
/// (.abs), Astronomical Research Network (.arn), Portrait (.cvp), Core IDC (.idc) and ISS (.iss).
/// Every fixture here is the one that was handed to the converter, and the pixels each test expects
/// are the pixels the converter wrote back out of it.
/// </summary>
[TestFixture]
public sealed class GapB0RasterTests {

  private static readonly byte[] _PngHeader = [
    0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A,
    0x00, 0x00, 0x00, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
  ];

  // ---------------------------------------------------------------- Portrait (.cvp)

  private static byte[] _BuildPortrait() {
    var data = new byte[PortraitFile.FileSize];
    for (var plane = 0; plane < 3; ++plane)
    for (var y = 0; y < PortraitFile.Side; ++y)
    for (var x = 0; x < PortraitFile.Side; ++x)
      data[plane * PortraitFile.PlaneSize + y * PortraitFile.Side + x] = (byte)(x * 7 + y * 13 + plane * 61);

    return data;
  }

  [Test]
  [Category("Unit")]
  public void Portrait_ReadsPlanarRgb() {
    var file = PortraitReader.FromBytes(_BuildPortrait());
    var image = PortraitFile.ToRawImage(file);

    Assert.That(image.Width, Is.EqualTo(512));
    Assert.That(image.Height, Is.EqualTo(512));
    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(image.PixelData.Length, Is.EqualTo(512 * 512 * 3));

    for (var y = 0; y < 512; ++y)
    for (var x = 0; x < 512; ++x)
    for (var c = 0; c < 3; ++c)
      Assert.That(image.PixelData[(y * 512 + x) * 3 + c], Is.EqualTo((byte)(x * 7 + y * 13 + c * 61)),
        $"pixel {x},{y} channel {c}");
  }

  [Test]
  [Category("Unit")]
  public void Portrait_RefusesAnythingButItsOwnLength() {
    Assert.Throws<InvalidDataException>(() => PortraitReader.FromBytes(_PngHeader));
    Assert.Throws<InvalidDataException>(() => PortraitReader.FromBytes(new byte[PortraitFile.FileSize - 1]));
  }

  // ---------------------------------------------------------------- Optocat (.abs)

  private static byte[] _BuildOptocat(int width, int height, int samples, bool bigEndian, byte[] pixels) {
    const int offset = 2048;
    var data = new byte[offset + pixels.Length];
    data[0] = bigEndian ? (byte)'M' : (byte)'I';
    data[1] = data[0];
    _Word(data, 4, offset, bigEndian);
    _Word(data, 10, samples, bigEndian);
    _Word(data, 14, width, bigEndian);
    _Word(data, 16, height, bigEndian);
    pixels.CopyTo(data, offset);
    return data;

    static void _Word(byte[] into, int at, int value, bool bigEndian) {
      if (bigEndian)
        BinaryPrimitives.WriteUInt16BigEndian(into.AsSpan(at), (ushort)value);
      else
        BinaryPrimitives.WriteUInt16LittleEndian(into.AsSpan(at), (ushort)value);
    }
  }

  private static byte[] _Ramp(int count, int step, int start = 0)
    => _Fill(count, i => (byte)(start + i * step));

  private static byte[] _Fill(int count, Func<int, byte> of) {
    var result = new byte[count];
    for (var i = 0; i < count; ++i)
      result[i] = of(i);

    return result;
  }

  [Test]
  [Category("Unit")]
  public void Optocat_ReadsOneSampleInBothByteOrders() {
    var pixels = _Fill(9 * 5, i => (byte)(i % 9 * 19 + i / 9 * 37));

    foreach (var bigEndian in new[] { false, true }) {
      var file = OptocatReader.FromBytes(_BuildOptocat(9, 5, 1, bigEndian, pixels));

      Assert.That(file.Width, Is.EqualTo(9));
      Assert.That(file.Height, Is.EqualTo(5));
      Assert.That(file.SamplesPerPixel, Is.EqualTo(1));
      Assert.That(file.IsLittleEndian, Is.EqualTo(!bigEndian));

      var image = OptocatFile.ToRawImage(file);
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(image.PixelData, Is.EqualTo(pixels));
    }
  }

  [Test]
  [Category("Unit")]
  public void Optocat_ReadsThreeSamplesAsInterleavedRgb() {
    var pixels = _Ramp(9 * 5 * 3, 1);
    var image = OptocatFile.ToRawImage(OptocatReader.FromBytes(_BuildOptocat(9, 5, 3, false, pixels)));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(image.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void Optocat_ReadsTwoSamplesAsFifteenBitColour() {
    // The converter widens each five-bit channel by multiplying by 255 and dividing by 31, and it
    // reads the word little-endian even out of a file that announced itself as MM.
    var pixels = _Ramp(9 * 5 * 2, 1);

    foreach (var bigEndian in new[] { false, true }) {
      var image = OptocatFile.ToRawImage(OptocatReader.FromBytes(_BuildOptocat(9, 5, 2, bigEndian, pixels)));

      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData[0], Is.EqualTo((byte)0));
      Assert.That(image.PixelData[1], Is.EqualTo((byte)0x41));
      Assert.That(image.PixelData[2], Is.EqualTo((byte)0));
      Assert.That(image.PixelData[3], Is.EqualTo((byte)0));
      Assert.That(image.PixelData[4], Is.EqualTo((byte)0xC5));
      Assert.That(image.PixelData[5], Is.EqualTo((byte)0x10));
      Assert.That(image.PixelData[6], Is.EqualTo((byte)0x08));
      Assert.That(image.PixelData[7], Is.EqualTo((byte)0x41));
      Assert.That(image.PixelData[8], Is.EqualTo((byte)0x20));
    }
  }

  [Test]
  [Category("Unit")]
  public void Optocat_ReadsFourSamplesAsThree() {
    var pixels = _Ramp(9 * 5 * 4, 1);
    var image = OptocatFile.ToRawImage(OptocatReader.FromBytes(_BuildOptocat(9, 5, 4, false, pixels)));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    for (var i = 0; i < 9 * 5; ++i)
    for (var c = 0; c < 3; ++c)
      Assert.That(image.PixelData[i * 3 + c], Is.EqualTo(pixels[i * 4 + c]));
  }

  [Test]
  [Category("Unit")]
  public void Optocat_RefusesForeignFiles() {
    Assert.Throws<InvalidDataException>(() => OptocatReader.FromBytes(_PngHeader));

    // A TIFF renamed to .abs: II and a length past 2048, but its first IFD stands at offset 8, which
    // is below the offset word this format requires.
    var tiff = new byte[4096];
    tiff[0] = (byte)'I';
    tiff[1] = (byte)'I';
    tiff[2] = 42;
    BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
    Assert.Throws<InvalidDataException>(() => OptocatReader.FromBytes(tiff));

    // Right byte order and offset, but the picture does not fit behind it.
    var short_ = _BuildOptocat(9, 5, 1, false, new byte[9 * 5]);
    Assert.Throws<InvalidDataException>(() => OptocatReader.FromBytes(short_[..^1]));
  }

  // ---------------------------------------------------------------- Core IDC (.idc)

  private static byte[] _BuildCoreIdc(int width, int height, int planes, int depth, byte[] pixels) {
    var data = new byte[pixels.Length + CoreIdcFile.TrailerSize];
    pixels.CopyTo(data, 0);
    var trailer = data.AsSpan(pixels.Length);
    BinaryPrimitives.WriteUInt32BigEndian(trailer, (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(trailer[4..], (uint)height);
    BinaryPrimitives.WriteUInt16BigEndian(trailer[8..], (ushort)planes);
    BinaryPrimitives.WriteUInt16BigEndian(trailer[10..], (ushort)depth);
    CoreIdcFile.Signature.CopyTo(trailer[(CoreIdcFile.TrailerSize - CoreIdcFile.SignatureFromEnd)..]);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void CoreIdc_ReadsEightBitGrey() {
    var pixels = _Fill(5 * 3, i => (byte)(i % 5 * 31 + i / 5 * 11));
    var file = CoreIdcReader.FromBytes(_BuildCoreIdc(5, 3, 1, 8, pixels));

    Assert.That(file.Width, Is.EqualTo(5));
    Assert.That(file.Height, Is.EqualTo(3));
    Assert.That(file.Planes, Is.EqualTo(1));
    Assert.That(file.BitsPerPixel, Is.EqualTo(8));

    var image = CoreIdcFile.ToRawImage(file);
    Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(image.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void CoreIdc_ReadsThreePlanesAsRgb() {
    var planes = new byte[3 * 5 * 3];
    for (var p = 0; p < 3; ++p)
    for (var y = 0; y < 3; ++y)
    for (var x = 0; x < 5; ++x)
      planes[p * 15 + y * 5 + x] = (byte)(x * 17 + y * 5 + p * 80);

    var image = CoreIdcFile.ToRawImage(CoreIdcReader.FromBytes(_BuildCoreIdc(5, 3, 3, 8, planes)));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    for (var y = 0; y < 3; ++y)
    for (var x = 0; x < 5; ++x)
    for (var c = 0; c < 3; ++c)
      Assert.That(image.PixelData[(y * 5 + x) * 3 + c], Is.EqualTo((byte)(x * 17 + y * 5 + c * 80)));
  }

  [Test]
  [Category("Unit")]
  public void CoreIdc_ReadsTwentyFourBitRowsAsRgb() {
    var pixels = _Fill(8 * 3 * 3, i => (byte)(i % 24 * 31 + i / 24 * 11));
    var image = CoreIdcFile.ToRawImage(CoreIdcReader.FromBytes(_BuildCoreIdc(8, 3, 1, 24, pixels)));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(image.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void CoreIdc_ReadsOneAndFourBitAsGrey() {
    // One bit a pixel is black where the bit is clear; a nibble is worth seventeen grey levels.
    var mono = _BuildCoreIdc(8, 3, 1, 1, [0x00, 0x0B, 0x16]);
    var monoImage = CoreIdcFile.ToRawImage(CoreIdcReader.FromBytes(mono));
    Assert.That(monoImage.PixelData[..8], Is.EqualTo(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }));
    Assert.That(monoImage.PixelData[8..16], Is.EqualTo(new byte[] { 0, 0, 0, 0, 255, 0, 255, 255 }));

    var nibbles = _BuildCoreIdc(8, 1, 1, 4, [0x00, 0x1F, 0x3E, 0x5D]);
    var nibbleImage = CoreIdcFile.ToRawImage(CoreIdcReader.FromBytes(nibbles));
    Assert.That(nibbleImage.PixelData, Is.EqualTo(new byte[] { 0x00, 0x00, 0x11, 0xFF, 0x33, 0xEE, 0x55, 0xDD }));
  }

  [Test]
  [Category("Unit")]
  public void CoreIdc_RefusesForeignFiles() {
    Assert.Throws<InvalidDataException>(() => CoreIdcReader.FromBytes(_PngHeader));

    var broken = _BuildCoreIdc(5, 3, 1, 8, new byte[15]);
    broken[^4] = (byte)'2';
    Assert.Throws<InvalidDataException>(() => CoreIdcReader.FromBytes(broken));
  }

  // ---------------------------------------------------------------- ISS (.iss)

  private static byte[] _BuildIss(int kind, int width, int height, byte[] pixels) {
    var data = new byte[IssFile.PixelsOffset + pixels.Length];
    IssFile.Magic.CopyTo(data);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(10), (ushort)kind);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(18), (uint)height);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(22), (uint)width);
    pixels.CopyTo(data, IssFile.PixelsOffset);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void Iss_ReadsEightBitCountingUpFromWhite() {
    var pixels = _Fill(6 * 4, i => (byte)(i % 6 * 40 + i / 6 * 7));
    var file = IssReader.FromBytes(_BuildIss(IssFile.GrayscaleKind, 6, 4, pixels));

    Assert.That(file.Width, Is.EqualTo(6));
    Assert.That(file.Height, Is.EqualTo(4));
    Assert.That(file.Kind, Is.EqualTo(IssFile.GrayscaleKind));

    var image = IssFile.ToRawImage(file);
    Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(image.PixelData[..6], Is.EqualTo(new byte[] { 0xFF, 0xD7, 0xAF, 0x87, 0x5F, 0x37 }));
    for (var i = 0; i < pixels.Length; ++i)
      Assert.That(image.PixelData[i], Is.EqualTo((byte)(255 - pixels[i])));
  }

  [Test]
  [Category("Unit")]
  public void Iss_PadsOneBitRowsToTwoHundredAndFiftySixPixels() {
    const int width = 20;
    const int height = 5;
    var stride = IssFile.RowStride(IssFile.MonochromeKind, width);
    Assert.That(stride, Is.EqualTo(32));

    var rows = new byte[stride * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      if ((x + y) % 3 == 0)
        rows[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));

    var image = IssFile.ToRawImage(IssReader.FromBytes(_BuildIss(IssFile.MonochromeKind, width, height, rows)));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      Assert.That(image.PixelData[y * width + x], Is.EqualTo((x + y) % 3 == 0 ? (byte)0 : (byte)255),
        $"pixel {x},{y}");

    Assert.That(IssFile.RowStride(IssFile.MonochromeKind, 300), Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void Iss_RefusesForeignFilesAndUnknownKinds() {
    Assert.Throws<InvalidDataException>(() => IssReader.FromBytes(_PngHeader));

    var wrongMagic = _BuildIss(IssFile.GrayscaleKind, 6, 4, new byte[24]);
    wrongMagic[7] = (byte)'Q';
    Assert.Throws<InvalidDataException>(() => IssReader.FromBytes(wrongMagic));

    Assert.Throws<InvalidDataException>(() => IssReader.FromBytes(_BuildIss(3, 6, 4, new byte[24])));
  }

  // ---------------------------------------------------------------- Astronomical Research Network (.arn)

  private static byte[] _BuildArn(int recordBytes, int labelRecords, int width, int height,
    byte[] pixels, byte[][] palette, int sampleBits = 8, string simple = "T  / ARN PROVISION") {
    var lines = new List<string> {
      $"SIMPLE = {simple}",
      $"RECORD_BYTES = {recordBytes}",
      $"LABEL_RECORDS = {labelRecords}",
      "OBJECT = IMAGE",
      $"LINES = {height}",
      $"LINE_SAMPLES = {width}",
      $"SAMPLE_BITS = {sampleBits}",
      "END_OBJECT",
      "END",
    };

    var label = Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n");
    var labelEnd = recordBytes * labelRecords;
    var gap = (ArnFile.GapBeforePalette + recordBytes - 1) / recordBytes * recordBytes;
    var planeStride = (ArnFile.PaletteEntries + recordBytes - 1) / recordBytes * recordBytes;

    var data = new byte[labelEnd + gap + planeStride * 3 + pixels.Length];
    Array.Fill(data, (byte)' ', label.Length, labelEnd - label.Length);
    label.CopyTo(data, 0);
    for (var plane = 0; plane < 3; ++plane)
      palette[plane].CopyTo(data, labelEnd + gap + planeStride * plane);

    pixels.CopyTo(data, labelEnd + gap + planeStride * 3);
    return data;
  }

  private static byte[][] _ArnPalette() => [
    _Fill(256, i => (byte)(i * 3)),
    _Fill(256, i => (byte)(i * 5 + 1)),
    _Fill(256, i => (byte)(255 - i)),
  ];

  [Test]
  [Category("Unit")]
  public void Arn_ReadsPaletteAndRowsPastTheGapAfterTheLabel() {
    var pixels = _Fill(7 * 4, i => (byte)(i % 7 * 23 + i / 7 * 41));
    var palette = _ArnPalette();

    // The gap and the palette stride are both rounded to whole records, so all three record sizes
    // have to land on the same pixels.
    foreach (var (recordBytes, labelRecords) in new[] { (256, 4), (512, 2), (1024, 1) }) {
      var file = ArnReader.FromBytes(_BuildArn(recordBytes, labelRecords, 7, 4, pixels, palette));

      Assert.That(file.Width, Is.EqualTo(7));
      Assert.That(file.Height, Is.EqualTo(4));
      Assert.That(file.RecordBytes, Is.EqualTo(recordBytes));
      Assert.That(file.LabelRecords, Is.EqualTo(labelRecords));
      Assert.That(file.PixelData, Is.EqualTo(pixels), $"records of {recordBytes}");

      var image = ArnFile.ToRawImage(file);
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(image.PaletteCount, Is.EqualTo(256));

      // Index 0 is 0,1,255 and index 23 is 69,116,232 in the converter's own output.
      Assert.That(image.Palette![0], Is.EqualTo((byte)0));
      Assert.That(image.Palette[1], Is.EqualTo((byte)1));
      Assert.That(image.Palette[2], Is.EqualTo((byte)255));
      Assert.That(image.Palette[23 * 3], Is.EqualTo((byte)69));
      Assert.That(image.Palette[23 * 3 + 1], Is.EqualTo((byte)116));
      Assert.That(image.Palette[23 * 3 + 2], Is.EqualTo((byte)232));
    }
  }

  [Test]
  [Category("Unit")]
  public void Arn_RefusesForeignFilesAndAnythingButEightBitSamples() {
    Assert.Throws<InvalidDataException>(() => ArnReader.FromBytes(_PngHeader));

    var pixels = new byte[7 * 4];
    var palette = _ArnPalette();

    // A FITS file opens with SIMPLE too, and is refused on the value.
    Assert.Throws<InvalidDataException>(() =>
      ArnReader.FromBytes(_BuildArn(256, 4, 7, 4, pixels, palette, simple: "T / FITS STANDARD")));

    Assert.Throws<InvalidDataException>(() =>
      ArnReader.FromBytes(_BuildArn(256, 4, 7, 4, pixels, palette, sampleBits: 16)));
  }

  // ---------------------------------------------------------------- detection

  [Test]
  [Category("Unit")]
  public void Detection_FindsTheFormatsThatCarryASignature() {
    Assert.That(FormatRegistry.DetectFromBytes(_BuildIss(IssFile.GrayscaleKind, 6, 4, new byte[24])),
      Is.EqualTo(ImageFormat.Iss));

    Assert.That(FormatRegistry.DetectFromBytes(_BuildCoreIdc(5, 3, 1, 8, new byte[15])),
      Is.EqualTo(ImageFormat.CoreIdc));

    Assert.That(FormatRegistry.DetectFromBytes(_BuildArn(256, 4, 7, 4, new byte[28], _ArnPalette())),
      Is.EqualTo(ImageFormat.Arn));
  }

  [Test]
  [Category("Unit")]
  public void Detection_LeavesTiffAndFitsAlone() {
    // A TIFF is a TIFF even though Optocat opens with the same two bytes.
    var tiff = new byte[4096];
    tiff[0] = (byte)'I';
    tiff[1] = (byte)'I';
    tiff[2] = 42;
    BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
    Assert.That(FormatRegistry.DetectFromBytes(tiff), Is.Not.EqualTo(ImageFormat.Optocat));

    // A FITS header opens with SIMPLE but not with this format's value.
    var fits = Encoding.ASCII.GetBytes("SIMPLE  =                    T / FITS STANDARD\r\n");
    Assert.That(FormatRegistry.DetectFromBytes(fits), Is.Not.EqualTo(ImageFormat.Arn));
  }
}

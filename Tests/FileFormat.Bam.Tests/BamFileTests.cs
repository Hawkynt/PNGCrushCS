using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bam;
using FileFormat.Core;

namespace FileFormat.Bam.Tests;

[TestFixture]
public sealed class BamFileTests {

  // ── Test fixtures ─────────────────────────────────────────────────────────

  private static byte[] _BuildBamBytes(int width, int height, byte[]? pixelData = null) {
    var pixels = pixelData ?? new byte[width * height * 4];
    var result = new byte[16 + pixels.Length];
    result[0] = (byte)'B'; result[1] = (byte)'A'; result[2] = (byte)'M'; result[3] = (byte)'F';
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), 1u);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12), (uint)height);
    pixels.CopyTo(result, 16);
    return result;
  }

  private static byte[] _GenerateGradient(int width, int height) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var i = (y * width + x) * 4;
        data[i] = (byte)x;
        data[i + 1] = (byte)y;
        data[i + 2] = (byte)((x + y) & 0xFF);
        data[i + 3] = 255;
      }
    return data;
  }

  // ── Metadata ──────────────────────────────────────────────────────────────

  [Test]
  public void PrimaryExtension_IsBam() {
    Assert.That(BamFile.PrimaryExtension, Is.EqualTo(".bam"));
  }

  [Test]
  public void FileExtensions_ContainsBam() {
    Assert.That(BamFile.FileExtensions, Does.Contain(".bam"));
  }

  // ── FromSpan (core read) ─────────────────────────────────────────────────

  [Test]
  public void FromSpan_ValidData_Parses() {
    var bytes = _BuildBamBytes(4, 3, _GenerateGradient(4, 3));
    var file = BamFile.FromSpan(bytes);
    Assert.That(file.Width, Is.EqualTo(4));
    Assert.That(file.Height, Is.EqualTo(3));
    Assert.That(file.PixelData.Length, Is.EqualTo(48));
  }

  [Test]
  public void FromSpan_TooSmall_Throws() {
    var tiny = new byte[8];
    Assert.Throws<InvalidDataException>(() => BamFile.FromSpan(tiny));
  }

  [Test]
  public void FromSpan_WrongMagic_Throws() {
    var bytes = _BuildBamBytes(2, 2);
    bytes[0] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => BamFile.FromSpan(bytes));
  }

  [Test]
  public void FromSpan_WrongVersion_Throws() {
    var bytes = _BuildBamBytes(2, 2);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 99u);
    Assert.Throws<InvalidDataException>(() => BamFile.FromSpan(bytes));
  }

  [Test]
  public void FromSpan_ZeroWidth_Throws() {
    var bytes = _BuildBamBytes(0, 1);
    Assert.Throws<InvalidDataException>(() => BamFile.FromSpan(bytes));
  }

  [Test]
  public void FromSpan_TruncatedPixelData_Throws() {
    var full = _BuildBamBytes(4, 4);
    var truncated = full.AsSpan(0, 16 + 10).ToArray();
    Assert.Throws<InvalidDataException>(() => BamFile.FromSpan(truncated));
  }

  // ── ToBytes ───────────────────────────────────────────────────────────────

  [Test]
  public void ToBytes_WritesValidHeader() {
    var file = new BamFile { Width = 5, Height = 7, PixelData = new byte[5 * 7 * 4] };
    var bytes = BamFile.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'B'));
    Assert.That(bytes[1], Is.EqualTo((byte)'A'));
    Assert.That(bytes[2], Is.EqualTo((byte)'M'));
    Assert.That(bytes[3], Is.EqualTo((byte)'F'));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)), Is.EqualTo(1u));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8)), Is.EqualTo(5u));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12)), Is.EqualTo(7u));
    Assert.That(bytes.Length, Is.EqualTo(16 + 5 * 7 * 4));
  }

  [Test]
  public void ToBytes_PreservesPixelData() {
    var pixels = _GenerateGradient(3, 3);
    var file = new BamFile { Width = 3, Height = 3, PixelData = pixels };
    var bytes = BamFile.ToBytes(file);
    for (var i = 0; i < pixels.Length; ++i)
      Assert.That(bytes[16 + i], Is.EqualTo(pixels[i]));
  }

  // ── ToRawImage / FromRawImage round-trip ─────────────────────────────────

  [Test]
  public void ToRawImage_ReturnsRgba32() {
    var file = new BamFile { Width = 2, Height = 2, PixelData = new byte[16] };
    var raw = BamFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));
  }

  [Test]
  public void FromRawImage_Rgba32_Works() {
    var raw = new RawImage {
      Width = 3, Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = _GenerateGradient(3, 2),
    };
    var file = BamFile.FromRawImage(raw);
    Assert.That(file.Width, Is.EqualTo(3));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 1, Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[3],
    };
    Assert.Throws<ArgumentException>(() => BamFile.FromRawImage(raw));
  }

  [Test]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => BamFile.FromRawImage(null!));
  }

  // ── Full round-trip via FormatIO ─────────────────────────────────────────

  [Test]
  public void FormatIO_EncodeDecode_RoundTrip() {
    var pixels = _GenerateGradient(8, 6);
    var raw = new RawImage {
      Width = 8, Height = 6,
      Format = PixelFormat.Rgba32,
      PixelData = pixels,
    };

    var bytes = FormatIO.Encode<BamFile>(raw);
    var decoded = FormatIO.Decode<BamFile>(bytes);

    Assert.That(decoded.Width, Is.EqualTo(8));
    Assert.That(decoded.Height, Is.EqualTo(6));
    Assert.That(decoded.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  public void FormatIO_Read_FromByteArray_Works() {
    var bytes = _BuildBamBytes(2, 2, _GenerateGradient(2, 2));
    var file = FormatIO.Read<BamFile>(bytes);
    Assert.That(file.Width, Is.EqualTo(2));
  }

  [Test]
  public void FormatIO_Read_FromSpan_Works() {
    var bytes = _BuildBamBytes(2, 2);
    var file = FormatIO.Read<BamFile>(bytes.AsSpan());
    Assert.That(file.Width, Is.EqualTo(2));
  }

  [Test]
  public void FormatIO_Read_FromStream_Works() {
    var bytes = _BuildBamBytes(2, 2);
    using var ms = new MemoryStream(bytes);
    var file = FormatIO.Read<BamFile>(ms);
    Assert.That(file.Width, Is.EqualTo(2));
  }

  [Test]
  public void FormatIO_Read_FromFile_Works() {
    var bytes = _BuildBamBytes(3, 4);
    var tempPath = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tempPath, bytes);
      var file = FormatIO.Read<BamFile>(new FileInfo(tempPath));
      Assert.That(file.Width, Is.EqualTo(3));
      Assert.That(file.Height, Is.EqualTo(4));
    } finally {
      File.Delete(tempPath);
    }
  }

  // ── ImageInfo (metadata-only, no pixel decode) ───────────────────────────

  [Test]
  public void ReadImageInfo_ValidHeader_Returns() {
    var bytes = _BuildBamBytes(100, 50);
    var info = BamFile.ReadImageInfo(bytes);
    Assert.That(info, Is.Not.Null);
    Assert.That(info!.Value.Width, Is.EqualTo(100));
    Assert.That(info.Value.Height, Is.EqualTo(50));
    Assert.That(info.Value.BitsPerPixel, Is.EqualTo(32));
    Assert.That(info.Value.ColorMode, Is.EqualTo("Rgba32"));
  }

  [Test]
  public void ReadImageInfo_OnlyHeaderBytes_Works() {
    // Metadata should be readable from just the 16-byte header, no pixel data
    var bytes = _BuildBamBytes(512, 256);
    var headerOnly = bytes.AsSpan(0, 16);
    var info = BamFile.ReadImageInfo(headerOnly);
    Assert.That(info, Is.Not.Null);
    Assert.That(info!.Value.Width, Is.EqualTo(512));
    Assert.That(info.Value.Height, Is.EqualTo(256));
  }

  [Test]
  public void ReadImageInfo_TooSmall_ReturnsNull() {
    var tiny = new byte[8];
    Assert.That(BamFile.ReadImageInfo(tiny), Is.Null);
  }

  [Test]
  public void ReadImageInfo_WrongMagic_ReturnsNull() {
    var bytes = _BuildBamBytes(2, 2);
    bytes[0] = (byte)'X';
    Assert.That(BamFile.ReadImageInfo(bytes), Is.Null);
  }

  [Test]
  public void FormatIO_ReadInfo_FromFile_OnlyReadsHeader() {
    // Create a file that's much larger than its pixel data requires
    var bytes = _BuildBamBytes(4, 4, _GenerateGradient(4, 4));
    var tempPath = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tempPath, bytes);
      var info = FormatIO.ReadInfo<BamFile>(new FileInfo(tempPath));
      Assert.That(info, Is.Not.Null);
      Assert.That(info!.Value.Width, Is.EqualTo(4));
      Assert.That(info.Value.Height, Is.EqualTo(4));
    } finally {
      File.Delete(tempPath);
    }
  }

  // ── Value-type semantics (struct verification) ───────────────────────────

  [Test]
  public void BamFile_IsValueType() {
    Assert.That(typeof(BamFile).IsValueType, Is.True);
  }

  [Test]
  public void BamFile_EqualsBasedOnValue() {
    var pixels = new byte[] { 1, 2, 3, 4 };
    var a = new BamFile { Width = 1, Height = 1, PixelData = pixels };
    var b = new BamFile { Width = 1, Height = 1, PixelData = pixels };
    Assert.That(a, Is.EqualTo(b));
  }
}

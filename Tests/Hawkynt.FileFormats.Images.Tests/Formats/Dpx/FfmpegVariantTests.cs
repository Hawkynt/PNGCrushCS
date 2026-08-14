using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Dpx;

namespace FileFormat.Dpx.Tests;

/// <summary>
/// DPX as ffmpeg writes it: generic header only, no industry-specific section.
/// SMPTE 268M splits the header into a generic part (file info 768 + image info 640 +
/// orientation 256 = 1664) and an optional industry-specific part (film 256 + television
/// 128 = 384). ffmpeg omits the latter and puts image data at offset 1664, so a reader
/// that assumes the full 2048 both loses the first 384 bytes of the image and rejects
/// small files outright — a 1x1 ffmpeg frame is only 1668 bytes.
/// </summary>
[TestFixture]
public sealed class FfmpegVariantTests {

  private const int _GENERIC_HEADER_SIZE = 1664;

  [Test]
  [Category("Unit")]
  public void FromBytes_ImageDataOffsetBelow2048_IsHonoured() {
    var data = _BuildFfmpegStyleDpx(4, 3, 16, true, out _);
    var result = DpxReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.ImageDataOffset, Is.EqualTo(_GENERIC_HEADER_SIZE));
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(3));
      Assert.That(result.BitsPerElement, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb16BigEndian_DecodesEveryPixel() {
    var data = _BuildFfmpegStyleDpx(61, 37, 16, true, out var expected);
    var raw = DpxFile.ToRawImage(DpxReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb48));
      Assert.That(raw.Width, Is.EqualTo(61));
      Assert.That(raw.Height, Is.EqualTo(37));
      Assert.That(raw.PixelData, Is.EqualTo(expected));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb8LittleEndian_DecodesEveryPixel() {
    var data = _BuildFfmpegStyleDpx(5, 4, 8, false, out var expected);
    var raw = DpxFile.ToRawImage(DpxReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(raw.PixelData, Is.EqualTo(expected));
    });
  }

  /// <summary>A 1x1 ffmpeg frame is 1668 bytes; the old 2048 floor refused it as "too small".</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_FileSmallerThan2048_IsAccepted() {
    var data = _BuildFfmpegStyleDpx(1, 1, 8, false, out var expected);
    Assert.That(data, Has.Length.LessThan(2048));

    var raw = DpxFile.ToRawImage(DpxReader.FromBytes(data));
    Assert.That(raw.PixelData, Is.EqualTo(expected));
  }

  /// <summary>Trailing slack (ffmpeg pads the packet, not the scanlines) must not shift the image.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_TrailingSlackAfterImage_IsIgnored() {
    var data = _BuildFfmpegStyleDpx(61, 37, 16, true, out var expected);
    var padded = new byte[data.Length + 74];
    data.CopyTo(padded, 0);
    for (var i = data.Length; i < padded.Length; ++i)
      padded[i] = 0xFF;

    var raw = DpxFile.ToRawImage(DpxReader.FromBytes(padded));
    Assert.That(raw.PixelData, Is.EqualTo(expected));
  }

  /// <summary>Short image data is a refusal, never an IndexOutOfRangeException escaping to the caller.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_TruncatedPixelData_ThrowsInvalidDataException() {
    var data = _BuildFfmpegStyleDpx(61, 37, 16, true, out _);
    var truncated = data[..(data.Length - 400)];

    Assert.Throws<InvalidDataException>(() => DpxFile.ToRawImage(DpxReader.FromBytes(truncated)));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_TruncatedLumaData_ThrowsInvalidDataException() {
    var data = _BuildFfmpegStyleDpx(32, 32, 16, true, out _, DpxDescriptor.Luma);
    var truncated = data[..(data.Length - 200)];

    Assert.Throws<InvalidDataException>(() => DpxFile.ToRawImage(DpxReader.FromBytes(truncated)));
  }

  /// <summary>The conventional 2048-byte layout ImageMagick writes must keep decoding unchanged.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_FullHeaderLayout_StillDecodes() {
    var data = _BuildFfmpegStyleDpx(61, 37, 16, true, out var expected, DpxDescriptor.Rgb, 2048);
    var raw = DpxFile.ToRawImage(DpxReader.FromBytes(data));

    Assert.That(raw.PixelData, Is.EqualTo(expected));
  }

  /// <summary>An offset the file cannot satisfy falls back instead of slicing out of range.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ImageDataOffsetPastEndOfFile_DoesNotThrow() {
    var data = _BuildFfmpegStyleDpx(8, 8, 16, true, out _);
    if (data[0] == 0x53)
      BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), int.MaxValue);
    else
      BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), int.MaxValue);

    Assert.DoesNotThrow(() => DpxReader.FromBytes(data));
  }

  /// <summary>
  /// Builds the container ffmpeg produces: image data immediately after the 1664-byte generic
  /// header, scanlines packed tight with no 4-byte row alignment. Pixel values are a
  /// deterministic ramp so the decode can be asserted exactly.
  /// </summary>
  private static byte[] _BuildFfmpegStyleDpx(
    int width,
    int height,
    int bitsPerElement,
    bool isBigEndian,
    out byte[] expectedRaw,
    DpxDescriptor descriptor = DpxDescriptor.Rgb,
    int dataOffset = _GENERIC_HEADER_SIZE
  ) {
    var componentsPerPixel = descriptor == DpxDescriptor.Luma ? 1 : 3;
    var bytesPerComponent = bitsPerElement / 8;
    var pixelCount = width * height;
    var imageBytes = pixelCount * componentsPerPixel * bytesPerComponent;

    var data = new byte[dataOffset + imageBytes];
    var span = data.AsSpan();

    var magic = isBigEndian ? DpxHeader.MagicBigEndian : DpxHeader.MagicLittleEndian;
    BinaryPrimitives.WriteInt32BigEndian(span, magic);

    void Write32(Span<byte> target, int value) {
      if (isBigEndian)
        BinaryPrimitives.WriteInt32BigEndian(target, value);
      else
        BinaryPrimitives.WriteInt32LittleEndian(target, value);
    }

    void Write16(Span<byte> target, short value) {
      if (isBigEndian)
        BinaryPrimitives.WriteInt16BigEndian(target, value);
      else
        BinaryPrimitives.WriteInt16LittleEndian(target, value);
    }

    Write32(span[4..], dataOffset);
    "V1.0\0\0\0\0"u8.CopyTo(span[8..]);
    Write32(span[16..], data.Length);
    Write32(span[24..], dataOffset); // generic header size — ffmpeg reports 1664
    Write32(span[28..], dataOffset == _GENERIC_HEADER_SIZE ? 0 : 384); // industry-specific header size

    var imageInfo = span[768..];
    Write16(imageInfo, 0); // orientation
    Write16(imageInfo[2..], 1); // element count
    Write32(imageInfo[4..], width);
    Write32(imageInfo[8..], height);

    const int elementBase = 768 + 12;
    span[elementBase + 20] = (byte)descriptor; // 800
    span[elementBase + 21] = (byte)DpxTransfer.Linear; // 801
    span[elementBase + 23] = (byte)bitsPerElement; // 803
    Write16(span[(elementBase + 24)..], (short)DpxPacking.Packed); // 804
    Write32(span[(elementBase + 28)..], dataOffset); // 808, element data offset

    // Deterministic ramp, written in the file's own endianness; the expected RawImage is
    // always big-endian because Rgb48/Gray16 are stored high byte first.
    var componentCount = pixelCount * componentsPerPixel;
    expectedRaw = new byte[componentCount * bytesPerComponent];
    for (var i = 0; i < componentCount; ++i) {
      if (bitsPerElement == 8) {
        var value = (byte)(i * 37 % 251);
        data[dataOffset + i] = value;
        expectedRaw[i] = value;
        continue;
      }

      var wide = (ushort)(i * 2731 % 65521);
      var at = dataOffset + i * 2;
      if (isBigEndian) {
        data[at] = (byte)(wide >> 8);
        data[at + 1] = (byte)wide;
      } else {
        data[at] = (byte)wide;
        data[at + 1] = (byte)(wide >> 8);
      }

      expectedRaw[i * 2] = (byte)(wide >> 8);
      expectedRaw[i * 2 + 1] = (byte)wide;
    }

    return data;
  }
}

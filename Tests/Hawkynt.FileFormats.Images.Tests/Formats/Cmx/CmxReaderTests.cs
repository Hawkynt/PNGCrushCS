using System;
using System.Buffers.Binary;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Cmx.Tests;

[TestFixture]
public sealed class CmxReaderTests {

  [TestCase(false, 1, 16)]
  [TestCase(true, 2, 32)]
  [Category("Unit")]
  public void FromSpan_ValidatedContainer_ReadsEmbeddedPreview(bool bigEndian, byte marker, int precision) {
    var image = _Image();
    var data = _Cmx(bigEndian, marker, image);

    var file = CmxReader.FromSpan(data);

    Assert.Multiple(() => {
      Assert.That(file.IsBigEndian, Is.EqualTo(bigEndian));
      Assert.That(file.CoordinatePrecisionBits, Is.EqualTo(precision));
      Assert.That(file.Preview.Width, Is.EqualTo(image.Width));
      Assert.That(file.Preview.Height, Is.EqualTo(image.Height));
      Assert.That(file.Preview.PixelData, Is.EqualTo(image.PixelData));
      Assert.That(file.PreviewOffset, Is.EqualTo(75));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_WrongPrecisionMarker_IsRefused() {
    Assert.That(() => CmxReader.FromSpan(_Cmx(false, 9, _Image())), Throws.TypeOf<System.IO.InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_WrongOuterContainer_IsRefusedEvenWithCmxForm() {
    var data = _Cmx(false, 1, _Image());
    data[0] = (byte)'N';

    Assert.That(() => CmxReader.FromSpan(data), Throws.TypeOf<System.IO.InvalidDataException>());
  }

  private static byte[] _Cmx(bool bigEndian, byte marker, RawImage image) {
    var bmp = BmpWriter.ToBytes(BmpFile.FromRawImage(image));
    var dib = bmp[14..];
    var result = new byte[75 + dib.Length];
    _Ascii4(bigEndian ? "RIFX" : "RIFF", result);
    _Ascii4("CMX1", result.AsSpan(8));
    result[74] = marker;
    dib.CopyTo(result, 75);

    var riffSize = checked((uint)(result.Length - 8));
    if (bigEndian)
      BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), riffSize);
    else
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), riffSize);
    return result;
  }

  private static RawImage _Image() => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Bgr24,
    PixelData = [
      1, 2, 3, 4, 5, 6,
      7, 8, 9, 10, 11, 12,
    ],
  };

  private static void _Ascii4(string value, Span<byte> destination) {
    for (var i = 0; i < 4; ++i)
      destination[i] = checked((byte)value[i]);
  }
}

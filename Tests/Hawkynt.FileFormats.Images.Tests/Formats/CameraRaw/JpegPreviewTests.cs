using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CameraRaw.Tests;

/// <summary>
/// A raw whose only picture is a JPEG preview, which is what most raw files are.
/// </summary>
/// <remarks>
/// The reader used to demand an uncompressed image and throw when it found none. Almost no camera
/// writes one: the sensor data is in a manufacturer's compression and the preview beside it is a
/// JPEG, which is what every viewer shows. Refusing the file threw away a picture plainly in it.
/// </remarks>
[TestFixture]
public sealed class JpegPreviewTests {

  /// <summary>Builds a TIFF container holding nothing but a JPEG, by one of the two conventions.</summary>
  private static byte[] _RawWithPreview(byte[] jpeg, bool inStrips) {
    var entries = new (ushort Tag, ushort Type, uint Count, uint Value)[] {
      (256, 3, 1, 64),
      (257, 3, 1, 48),
      (258, 3, 1, 8),
      (259, 3, 1, 6),
      (277, 3, 1, 3),
      (inStrips ? (ushort)273 : (ushort)513, 4, 1, 0),
      (inStrips ? (ushort)279 : (ushort)514, 4, 1, (uint)jpeg.Length),
    };

    var ifdOffset = 8;
    var ifdLength = 2 + entries.Length * 12 + 4;
    var jpegOffset = ifdOffset + ifdLength;

    var result = new byte[jpegOffset + jpeg.Length];
    result[0] = (byte)'I';
    result[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), 42);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)ifdOffset);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(ifdOffset), (ushort)entries.Length);

    for (var i = 0; i < entries.Length; ++i) {
      var at = ifdOffset + 2 + i * 12;
      var (tag, type, count, value) = entries[i];
      if (tag is 513 or 273)
        value = (uint)jpegOffset;

      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at), tag);
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at + 2), type);
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at + 4), count);
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at + 8), value);
    }

    jpeg.CopyTo(result.AsSpan(jpegOffset));
    return result;
  }

  private static byte[] _Jpeg() {
    var pixels = new byte[64 * 48 * 3];
    for (var y = 0; y < 48; ++y)
    for (var x = 0; x < 64; ++x) {
      var at = (y * 64 + x) * 3;
      pixels[at] = (byte)(x * 4);
      pixels[at + 1] = (byte)(y * 5);
      pixels[at + 2] = 96;
    }

    var image = new RawImage { Width = 64, Height = 48, Format = PixelFormat.Rgb24, PixelData = pixels };
    return FileFormat.Jpeg.JpegWriter.ToBytes(FileFormat.Jpeg.JpegFile.FromRawImage(image));
  }

  [TestCase(true)]
  [TestCase(false)]
  [Category("Integration")]
  public void Read_TakesThePreviewByEitherConvention(bool inStrips) {
    var file = CameraRawReader.FromBytes(_RawWithPreview(_Jpeg(), inStrips));
    var image = CameraRawFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(64));
      Assert.That(image.Height, Is.EqualTo(48));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  [Category("Integration")]
  public void Read_TakesTheLargestPreviewRatherThanTheFirst() {
    // A thumbnail is not what anybody wants to see when a full-size preview is beside it.
    var jpeg = _Jpeg();
    var data = _RawWithPreview(jpeg, inStrips: false);

    var file = CameraRawReader.FromBytes(data);
    Assert.That(CameraRawFile.ToRawImage(file).Width, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void Read_StillRefusesAContainerWithNoPictureAtAll() {
    var data = _RawWithPreview(new byte[64], inStrips: false);
    Assert.Throws<InvalidDataException>(() => CameraRawReader.FromBytes(data));
  }
}

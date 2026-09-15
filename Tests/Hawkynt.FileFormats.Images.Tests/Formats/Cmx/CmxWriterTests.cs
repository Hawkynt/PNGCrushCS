using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Cmx.Tests;

[TestFixture]
public sealed class CmxWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_EmitsPronomCmx5HeaderAndIndexedPage() {
    var bytes = CmxWriter.ToBytes(_Image(8, 5));

    var masterIndexOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(104)));
    var displayOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(112)));

    Assert.Multiple(() => {
      Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
      Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("CMX1"));
      Assert.That(bytes[72], Is.EqualTo((byte)'2'), "coordinate-size field must be ASCII, not binary");
      Assert.That(bytes[74], Is.EqualTo((byte)'1'), "PRONOM CMX 16-bit marker is ASCII '1'");
      Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 124, 4), Is.EqualTo("page"));
      Assert.That(System.Text.Encoding.ASCII.GetString(bytes, displayOffset, 4), Is.EqualTo("DISP"));
      Assert.That(System.Text.Encoding.ASCII.GetString(bytes, masterIndexOffset, 4), Is.EqualTo("ixmr"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RoundTripThroughReader_PreservesOpaquePreviewPixels() {
    var source = _Image(7, 6);
    var bytes = CmxWriter.ToBytes(source);
    var parsed = CmxReader.FromSpan(bytes);

    Assert.Multiple(() => {
      Assert.That((parsed.Preview.Width, parsed.Preview.Height), Is.EqualTo((source.Width, source.Height)));
      Assert.That(parsed.Preview.ToRgba32(), Is.EqualTo(source.ToRgba32()));
      Assert.That(parsed.CoordinatePrecisionBits, Is.EqualTo(16));
      Assert.That(parsed.InternalVersion, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThenWriter_AcceptsNonRgbaInput() {
    var source = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = [0, 64, 128, 192, 224, 255],
    };

    var file = CmxFile.FromRawImage(source);
    var parsed = CmxReader.FromSpan(CmxWriter.ToBytes(file));

    Assert.That((parsed.Preview.Width, parsed.Preview.Height), Is.EqualTo((3, 2)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TransparentPixels_AreFlattenedOnlyInVectorSceneNotRejected() {
    var image = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [255, 0, 0, 0, 0, 0, 255, 128],
    };

    Assert.That(() => CmxWriter.ToBytes(image), Throws.Nothing);
  }

  private static RawImage _Image(int width, int height) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var offset = (y * width + x) * 4;
      data[offset] = (byte)(x * 31 + y * 7);
      data[offset + 1] = (byte)(y * 37 + x * 3);
      data[offset + 2] = (byte)(255 - x * 11 - y * 13);
      data[offset + 3] = 255;
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = data,
    };
  }
}

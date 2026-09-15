using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Sdg.Tests;

[TestFixture]
public sealed class SdgWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleImages_RoundTrip() {
    var first = _Image(0x10);
    var second = _Image(0x50);
    var file = new SdgFile { Images = [first, second] };

    var encoded = SdgWriter.ToBytes(file);
    var decoded = SdgReader.FromSpan(encoded);

    Assert.Multiple(() => {
      Assert.That(decoded.Images, Has.Count.EqualTo(2));
      Assert.That(decoded.Images[0].PixelData, Is.EqualTo(first.PixelData));
      Assert.That(decoded.Images[1].PixelData, Is.EqualTo(second.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TransparentImage_RoundTripsBitmapExMask() {
    var image = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Bgra32,
      PixelData = [
        0x10, 0x20, 0x30, 0x00,
        0x40, 0x50, 0x60, 0x40,
        0x70, 0x80, 0x90, 0x80,
        0xA0, 0xB0, 0xC0, 0xFF,
      ],
    };

    var encoded = SdgWriter.ToBytes(SdgFile.FromRawImage(image));
    var decoded = SdgReader.FromSpan(encoded).Images[0];

    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Bgra32));
      Assert.That(decoded.PixelData, Is.EqualTo(image.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesBitmapV5WithZCompressThumbnail() {
    const uint zCompress = 0x01004453;
    var encoded = SdgWriter.ToBytes(SdgFile.FromRawImage(_Image(0x20)));

    Assert.Multiple(() => {
      Assert.That(encoded.AsSpan(0, 4).ToArray(), Is.EqualTo(new byte[] { (byte)'S', (byte)'G', (byte)'A', (byte)'3' }));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(4)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(6)), Is.EqualTo(5));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(8)), Is.EqualTo(1));
      Assert.That(encoded[10], Is.EqualTo(1));
      Assert.That(encoded[11], Is.EqualTo((byte)'B'));
      Assert.That(encoded[12], Is.EqualTo((byte)'M'));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(41)), Is.EqualTo(zCompress));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyFile_IsRefused() {
    Assert.That(() => SdgWriter.ToBytes(new SdgFile()), Throws.TypeOf<InvalidDataException>());
  }

  private static RawImage _Image(byte seed) => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Bgr24,
    PixelData = [
      seed, (byte)(seed + 1), (byte)(seed + 2), (byte)(seed + 3), (byte)(seed + 4), (byte)(seed + 5),
      (byte)(seed + 6), (byte)(seed + 7), (byte)(seed + 8), (byte)(seed + 9), (byte)(seed + 10), (byte)(seed + 11),
    ],
  };
}

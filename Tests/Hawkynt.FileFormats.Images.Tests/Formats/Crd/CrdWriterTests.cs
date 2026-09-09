using System;
using FileFormat.Core;
using FileFormat.Crd;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Crd.Tests;

[TestFixture]
public sealed class CrdWriterTests {

  private static RawImage _Picture(int width = 13, int height = 9) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)(x * 19 + y * 3);
        pixels[at + 1] = (byte)(x * 5 + y * 23);
        pixels[at + 2] = (byte)(x * 11 + y * 7);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesTheCanonicalHeaderImmediatelyBeforeTheJfif() {
    var file = CrdFile.FromRawImage(_Picture());
    var bytes = CrdWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, CrdFile.Magic.Length).SequenceEqual(CrdFile.Magic), Is.True);
      Assert.That(bytes[CrdFile.Magic.Length], Is.Zero);
      Assert.That(bytes.AsSpan(CrdFile.HeaderSize).SequenceEqual(file.PictureData), Is.True);
    });

    var parsed = CrdReader.FromBytes(bytes);
    Assert.Multiple(() => {
      Assert.That(parsed.PictureOffset, Is.EqualTo(CrdFile.HeaderSize));
      Assert.That(parsed.PictureData, Is.EqualTo(file.PictureData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RefusesAPictureWithoutTheJfifApp0Identifier() {
    byte[] jpeg = [.. CrdFile.FromRawImage(_Picture()).PictureData];
    jpeg[CrdFile.JfifIdentifierOffset] = (byte)'X';

    Assert.That(
      () => CrdWriter.ToBytes(new() { PictureData = jpeg }),
      Throws.ArgumentException.With.Message.Contains("JFIF")
    );
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RefusesBytesBehindTheJpegEndMarker() {
    var jpeg = CrdFile.FromRawImage(_Picture()).PictureData;
    byte[] withTrailingByte = [.. jpeg, 0];

    Assert.That(
      () => CrdWriter.ToBytes(new() { PictureData = withTrailingByte }),
      Throws.ArgumentException.With.Message.Contains("exactly one complete JFIF JPEG")
    );
  }

  [Test]
  [Category("Integration")]
  public void Registry_WritesAndReadsCrd() {
    var source = _Picture();
    var bytes = FormatRegistry.Write(source, ImageFormat.Crd);

    Assert.That(bytes, Is.Not.Null.And.Not.Empty);
    Assert.That(FormatRegistry.DetectFromBytes(bytes!), Is.EqualTo(ImageFormat.Crd));

    var restored = FormatRegistry.Read(bytes!);
    Assert.That(restored, Is.Not.Null);
    Assert.That((restored!.Width, restored.Height), Is.EqualTo((source.Width, source.Height)));
  }
}

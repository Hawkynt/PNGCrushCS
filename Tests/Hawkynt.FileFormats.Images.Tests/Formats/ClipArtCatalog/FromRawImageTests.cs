using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.ClipArtCatalog.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      data[i * 3] = (byte)(i * 7);
      data[i * 3 + 1] = (byte)(i * 13);
      data[i * 3 + 2] = (byte)(i * 29);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static ClipArtCatalogFile _RoundTrip(RawImage image)
    => ClipArtCatalogReader.FromBytes(ClipArtCatalogWriter.ToBytes(ClipArtCatalogFile.FromRawImage(image)));

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_ReproducesEveryPixel() {
    var source = _Gradient(37, 11);
    var decoded = ClipArtCatalogFile.ToRawImage(_RoundTrip(source));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = ClipArtCatalogFile.ToRawImage(_RoundTrip(_Gradient(200, 3)));
    var tall = ClipArtCatalogFile.ToRawImage(_RoundTrip(_Gradient(3, 200)));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwn() {
    var grey = new RawImage { Width = 5, Height = 4, Format = PixelFormat.Gray8, PixelData = new byte[20] };

    Assert.That(ClipArtCatalogFile.ToRawImage(_RoundTrip(grey)).Width, Is.EqualTo(5));
  }

  /// <summary>
  /// The outermost chunk states the length of everything after it, and the walk has to land on the
  /// end of the file and nowhere else. That is what the reader accounts for a catalogue by, and it
  /// only holds when every inner length is written from what actually follows it and the chunks sit
  /// on even boundaries.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_TheOuterLengthAccountsForTheFile() {
    // Five pixels at three bytes is fifteen a row, so the bitmap inside is an odd number of bytes
    // and the pad that keeps the next chunk even is exercised.
    var bytes = ClipArtCatalogWriter.ToBytes(ClipArtCatalogFile.FromRawImage(_Gradient(5, 3)));
    var stated = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 4).SequenceEqual(ClipArtCatalogFile.Magic), Is.True);
      Assert.That(bytes.AsSpan(ClipArtCatalogFile.ChunkHeaderSize, 4).SequenceEqual(ClipArtCatalogFile.ClipTag), Is.True);
      Assert.That(stated + ClipArtCatalogFile.ChunkHeaderSize, Is.EqualTo(bytes.Length));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheNameTheCatalogueRecords() {
    var file = _RoundTrip(_Gradient(37, 11));

    Assert.Multiple(() => {
      Assert.That(ClipArtCatalogFile.ImageCount(file), Is.EqualTo(1));
      Assert.That(file.Entries[0].Name, Is.EqualTo("clipart.pcx"));
    });
  }
}

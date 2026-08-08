using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Core;

namespace FileFormat.TiPicture.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Checks(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = (byte)(((x / 3) + (y / 5)) % 2 == 0 ? 0 : 255);
        var at = (y * width + x) * 3;
        data[at] = data[at + 1] = data[at + 2] = value;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TheScreen_ReproducesEveryPixel() {
    var source = _Checks(TiPictureFile.Width8283, TiPictureFile.ScreenHeight);
    var file = TiPictureFile.FromRawImage(source, ".82i");
    var decoded = TiPictureReader.FromBytes(TiPictureWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((TiPictureFile.Width8283, TiPictureFile.ScreenHeight)));
      Assert.That(decoded.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_ADifferentSize_IsSampledToTheScreenRatherThanRefused() {
    var file = TiPictureFile.FromRawImage(_Checks(37, 11), ".82i");
    var decoded = TiPictureReader.FromBytes(TiPictureWriter.ToBytes(file));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((TiPictureFile.Width8283, TiPictureFile.ScreenHeight)));
  }

  [Test]
  [Category("Unit")]
  [TestCase(".82i", "82", TiPictureFile.Width8283)]
  [TestCase(".83i", "83", TiPictureFile.Width8283)]
  [TestCase(".85i", "85", TiPictureFile.Width8586)]
  [TestCase(".86i", "86", TiPictureFile.Width8586)]
  public void FromRawImage_TakesTheCalculatorFromTheExtension(string extension, string model, int width) {
    var file = TiPictureFile.FromRawImage(_Checks(37, 11), extension);
    var bytes = TiPictureWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 8), Is.EqualTo($"**TI{model}**"));
      Assert.That(TiPictureReader.FromBytes(bytes).Width, Is.EqualTo(width));
    });
  }

  /// <summary>
  /// The two bytes after the entries are the sum of every byte of every one of them, low sixteen
  /// bits. Nothing in this library reads it back, so a wrong one would never show here; the
  /// calculator and the link software both refuse a file whose sum does not come out.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_EndsWithTheSumOfItsEntries() {
    var bytes = TiPictureWriter.ToBytes(TiPictureFile.FromRawImage(_Checks(37, 11), ".86i"));
    var stated = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(TiPictureFile.HeaderSize - 2));

    var sum = 0;
    for (var i = 0; i < stated; ++i)
      sum += bytes[TiPictureFile.HeaderSize + i];

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(TiPictureFile.HeaderSize + stated + 2), "the header, the entries and the checksum are the whole file");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(TiPictureFile.HeaderSize + stated)), Is.EqualTo((ushort)sum));
    });
  }

  /// <summary>The set bit is the lit pixel, which this palette draws black.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_SetsTheBitForTheDarkPixel() {
    var half = new byte[TiPictureFile.Width8283 * TiPictureFile.ScreenHeight * 3];
    Array.Fill(half, (byte)255, 0, TiPictureFile.Width8283 * 3);

    var file = TiPictureFile.FromRawImage(
      new() { Width = TiPictureFile.Width8283, Height = TiPictureFile.ScreenHeight, Format = PixelFormat.Rgb24, PixelData = half },
      ".82i");

    Assert.Multiple(() => {
      Assert.That(file.PixelData[0], Is.EqualTo(0), "the white row leaves its bits clear");
      Assert.That(file.PixelData[TiPictureFile.Width8283 / 8], Is.EqualTo(0xFF), "the black row sets them");
    });
  }
}

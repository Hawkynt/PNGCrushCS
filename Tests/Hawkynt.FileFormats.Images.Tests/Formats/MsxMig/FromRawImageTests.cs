using System;
using FileFormat.Core;

namespace FileFormat.MsxMig.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A picture drawn in screen 8's own colours, so nothing has to be rounded to reach it.</summary>
  private static RawImage _Screen8Colors(int width, int height) {
    var palette = MsxMigWriter.Screen8Palette();
    var data = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var entry = ((x * 7 + y * 11) & 255) * 3;
      var at = (y * width + x) * 3;
      data[at] = palette[entry];
      data[at + 1] = palette[entry + 1];
      data[at + 2] = palette[entry + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Screen8_ReproducesExactly() {
    var source = _Screen8Colors(MsxMigWriter.Columns, MsxMigWriter.Rows);
    var file = MsxMigFile.FromRawImage(source);
    var decoded = MsxMigFile.ToRawImage(MsxMigReader.FromBytes(MsxMigWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((MsxMigWriter.Columns, MsxMigWriter.Rows)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnyOtherSize() {
    // A screen record is one size and no other, so a picture of another is brought to it.
    var file = MsxMigFile.FromRawImage(_Screen8Colors(37, 11));

    Assert.That((file.Width, file.Height), Is.EqualTo((MsxMigWriter.Columns, MsxMigWriter.Rows)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StatesItsOwnLengthPastTheSignature() {
    // The header's length counts from the seventh byte, not from the first, and a file whose count
    // includes its own signature is refused before anything is unpacked.
    var bytes = MsxMigWriter.ToBytes(MsxMigFile.FromRawImage(_Screen8Colors(64, 64)));
    var stated = bytes[6] | (bytes[7] << 8) | (bytes[8] << 16) | (bytes[9] << 24);

    Assert.That(stated, Is.EqualTo(bytes.Length - 6));
  }
}

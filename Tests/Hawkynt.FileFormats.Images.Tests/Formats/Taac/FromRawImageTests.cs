using System;
using System.Globalization;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Taac.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A width that is not a multiple of eight, to catch a stride assumption.</summary>
  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      data[i * 3] = (byte)(i * 7);
      data[i * 3 + 1] = (byte)(i * 13);
      data[i * 3 + 2] = (byte)(i * 29);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Colour_ReproducesEveryPixel() {
    var source = _Gradient(37, 11);
    var bytes = TaacWriter.ToBytes(TaacFile.FromRawImage(source));
    var decoded = TaacFile.ToRawImage(TaacReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(37));
      Assert.That(decoded.Height, Is.EqualTo(11));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    // The header states the size, so there is nothing for a picture to be sampled to.
    var wide = TaacFile.FromRawImage(_Gradient(200, 3));
    var tall = TaacFile.FromRawImage(_Gradient(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
      Assert.That(wide.Bands, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_AcceptsAGreyAsTheSingleBand() {
    var grey = new RawImage { Width = 5, Height = 4, Format = PixelFormat.Gray8, PixelData = new byte[20] };
    var file = TaacFile.FromRawImage(grey);

    Assert.Multiple(() => {
      Assert.That(file.Bands, Is.EqualTo(1));
      Assert.That(file.Palette, Is.Null);
    });
  }

  /// <summary>
  /// The colour map is written blue first. Read the other way round the sample photograph's skin
  /// comes out blue, so this is the one thing about the format a round trip through this reader
  /// alone would never catch.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_WritesTheColourMapBlueFirst() {
    var file = new TaacFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      PixelData = [0],
      Palette = [0x11, 0x22, 0x33],
      PaletteCount = 1,
    };

    var header = Encoding.Latin1.GetString(TaacWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(header, Does.Contain("colormap=332211"));
      Assert.That(header, Does.Contain("colormapsize=1;"));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Indexed_KeepsTheColoursItWentInWith() {
    var source = _Gradient(37, 11).EnsureIndexedAtMost(64);
    var file = TaacFile.FromRawImage(source);
    var decoded = TaacFile.ToRawImage(TaacReader.FromBytes(TaacWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(file.Bands, Is.EqualTo(1));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(
        PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData,
        Is.EqualTo(PixelConverter.Convert(source, PixelFormat.Rgb24).PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StatesAsManyExtentsAsItsRank() {
    var header = Encoding.Latin1.GetString(TaacWriter.ToBytes(TaacFile.FromRawImage(_Gradient(37, 11))));

    Assert.Multiple(() => {
      Assert.That(header, Does.Contain("rank=2;"));
      Assert.That(header, Does.Contain(string.Create(CultureInfo.InvariantCulture, $"size=37 11;")));
      Assert.That(header, Does.Contain("bits=8;"));
    });
  }
}

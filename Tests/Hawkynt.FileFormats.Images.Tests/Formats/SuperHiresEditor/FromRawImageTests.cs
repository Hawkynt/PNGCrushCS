using System;
using FileFormat.Core;

namespace FileFormat.SuperHiresEditor.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A picture the high-resolution screen can hold: two of the machine's colours per cell.</summary>
  private static RawImage _HiresImage(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = y / 8 * (width / 8) + x / 8;
      var index = ((x * 3 + y) & 1) == 0 ? cell % 16 : (cell * 11 + 5) % 16;
      var color = Commodore64Graphics.HexColors[index];
      var offset = (y * width + x) * 3;
      rgb[offset] = (byte)(color >> 16);
      rgb[offset + 1] = (byte)(color >> 8);
      rgb[offset + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_APictureTheScreenCanHold_IsExact() {
    var source = _HiresImage(SuperHiresEditorFile.ImageWidth, SuperHiresEditorFile.ImageHeight);

    var bytes = SuperHiresEditorWriter.ToBytes(_Encode<SuperHiresEditorFile>(source));
    var decoded = SuperHiresEditorFile.ToRawImage(SuperHiresEditorReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<SuperHiresEditorFile>(_HiresImage(640, 400));
    var decoded = SuperHiresEditorFile.ToRawImage(SuperHiresEditorReader.FromBytes(SuperHiresEditorWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(SuperHiresEditorFile.ImageWidth));
      Assert.That(decoded.Height, Is.EqualTo(SuperHiresEditorFile.ImageHeight));
    });
  }

  /// <summary>
  /// Encodes through the interface rather than the type, so this stops compiling if the declaration
  /// goes away — which is what the registry generator reads to decide the format can be written at
  /// all, and nothing else here would notice its absence.
  /// </summary>
  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}

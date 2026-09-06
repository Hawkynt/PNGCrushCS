using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Spectrum512Smoosh.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>The eight levels the ST's three-bit channel reaches, widened as the reference decoder widens them.</summary>
  private static readonly byte[] _Levels = [0, 36, 73, 109, 146, 182, 219, 255];

  /// <summary>
  /// <paramref name="colours"/> non-black colours a line, a different set on every line.
  /// </summary>
  private static RawImage _PerLinePalette(int width, int height, int colours) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = x * colours / width;
      var offset = (y * width + x) * 3;
      // The red channel alone separates the bars, so every one of them is a colour of its own and
      // none of them is black however the other two land.
      rgb[offset] = _Levels[index % 7 + 1];
      rgb[offset + 1] = _Levels[(index * 3 + y) & 7];
      rgb[offset + 2] = _Levels[(index + y * 5) & 7];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FourteenStColoursPerScanline_IsExact() {
    var source = _PerLinePalette(320, 199, Spectrum512SmooshFile.MaxColorsPerScanline);

    var bytes = Spectrum512SmooshWriter.ToBytes(_Encode<Spectrum512SmooshFile>(source));
    var decoded = Spectrum512SmooshFile.ToRawImage(Spectrum512SmooshReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BlackCostsNoneOfTheFourteen() {
    var source = _PerLinePalette(320, 199, Spectrum512SmooshFile.MaxColorsPerScanline);
    // A black pixel dropped into every bar, none of them wide enough to lose its colour: fifteen
    // colours a line, and the format holds them because pen 0 draws black without being named.
    for (var y = 0; y < 199; ++y)
    for (var x = 0; x < 320; x += 40)
      Array.Clear(source.PixelData, (y * 320 + x) * 3, 3);

    var bytes = Spectrum512SmooshWriter.ToBytes(_Encode<Spectrum512SmooshFile>(source));
    var decoded = Spectrum512SmooshFile.ToRawImage(Spectrum512SmooshReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromExactRawImage_RefusesAScanlinePastTheColourBudget() {
    var source = _PerLinePalette(320, 199, Spectrum512SmooshFile.MaxColorsPerScanline + 1);

    var refusal = Assert.Throws<NotSupportedException>(() => Spectrum512SmooshFile.FromExactRawImage(source));
    Assert.That(refusal!.Message, Does.Contain(Spectrum512SmooshFile.MaxColorsPerScanline.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void FromExactRawImage_RefusesAnotherSize() {
    var refusal = Assert.Throws<NotSupportedException>(
      () => Spectrum512SmooshFile.FromExactRawImage(_PerLinePalette(160, 100, 4)));

    Assert.That(refusal!.Message, Does.Contain("320x199"));
  }

  /// <summary>
  /// The registry's writer contract asks for a file out of any picture, so the path behind it reduces
  /// where the strict one refuses — but only the scanlines that need it.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void FromRawImage_ReducesRatherThanRefusing() {
    var source = _PerLinePalette(320, 199, Spectrum512SmooshFile.MaxColorsPerScanline + 6);

    var bytes = Spectrum512SmooshWriter.ToBytes(_Encode<Spectrum512SmooshFile>(source));
    var decoded = Spectrum512SmooshFile.ToRawImage(Spectrum512SmooshReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(199));
      Assert.That(decoded.PixelData, Is.Not.EqualTo(source.PixelData));
    });
  }

  /// <summary>A picture inside the budget goes through the registry's path untouched too.</summary>
  [Test]
  [Category("Integration")]
  public void FromRawImage_LeavesAPictureItCanHoldAlone() {
    var source = _PerLinePalette(320, 199, Spectrum512SmooshFile.MaxColorsPerScanline);

    var bytes = Spectrum512SmooshWriter.ToBytes(_Encode<Spectrum512SmooshFile>(source));
    var decoded = Spectrum512SmooshFile.ToRawImage(Spectrum512SmooshReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  /// <summary>Another size is sampled rather than refused on the registry's path.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<Spectrum512SmooshFile>(_PerLinePalette(160, 100, 4));
    var decoded = Spectrum512SmooshFile.ToRawImage(Spectrum512SmooshReader.FromBytes(Spectrum512SmooshWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(199));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesTheSignatureAndTheSectionLengths() {
    var file = _Encode<Spectrum512SmooshFile>(_PerLinePalette(320, 199, 8));
    var bytes = Spectrum512SmooshWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo((byte)'S'));
      Assert.That(bytes[1], Is.EqualTo((byte)'P'));
      Assert.That(bytes[2], Is.EqualTo(0));
      Assert.That(bytes[3], Is.EqualTo(0));

      var bitmapLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
      var paletteLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8));
      Assert.That(Spectrum512SmooshFile.HeaderSize + bitmapLength + paletteLength, Is.EqualTo((uint)bytes.Length));
    });
  }

  /// <summary>
  /// Which of the two layouts a smooshed file uses is stated by nothing but the parity of its last
  /// byte, so ours has to end on an even one or every decoder reads the bitmap the wrong way round.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_EndsOnAnEvenByte() {
    var bytes = Spectrum512SmooshWriter.ToBytes(_Encode<Spectrum512SmooshFile>(_PerLinePalette(320, 199, 8)));

    Assert.That(bytes[^1] & 1, Is.Zero);
  }

  /// <summary>
  /// Encodes through the interface rather than the type, so this stops compiling if the declaration
  /// goes away — which is what the registry generator reads to decide the format can be written at
  /// all, and nothing else here would notice its absence.
  /// </summary>
  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}

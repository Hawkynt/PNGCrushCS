using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Graphics10Plus;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Graphics10Plus.Tests;

/// <summary>
/// Graphics 10+, which is a GTIA nine-colour screen of sixty rows shown four times as tall.
/// </summary>
/// <remarks>
/// The registers are what make this worth testing rather than the bitmap. A four-bit pixel has
/// sixteen values against nine registers, so seven of them are aliases — the background repeats
/// across four and the four playfield registers each appear a second time — and a reader that took
/// the obvious view of a four-bit index against a nine-entry table would punch a hole in the
/// picture wherever one landed.
/// </remarks>
[TestFixture]
public sealed class Graphics10PlusTests {

  /// <summary>The nine registers, distinct and even, so each can be told from the others.</summary>
  private static readonly byte[] _Registers = [0x24, 0x46, 0x68, 0x8A, 0xAC, 0xCE, 0xE0, 0x12, 0x34];

  private static byte[] _Build(Func<int, int, int> nibble) {
    var data = new byte[Graphics10PlusFile.FileSize];
    for (var y = 0; y < Graphics10PlusFile.ScreenRows; ++y)
    for (var x = 0; x < Graphics10PlusFile.StoredWidth; ++x)
      data[y * Graphics10PlusFile.BytesPerRow + (x >> 1)] |= (byte)((nibble(x, y) & 15) << ((x & 1) == 0 ? 4 : 0));

    _Registers.CopyTo(data.AsSpan(Graphics10PlusFile.RegisterOffset));

    return data;
  }

  private static (byte R, byte G, byte B) _At(RawImage image, int x, int y) {
    var at = (y * image.Width + x) * 3;

    return (image.PixelData[at], image.PixelData[at + 1], image.PixelData[at + 2]);
  }

  /// <summary>The colour a register byte stands for, which is a machine palette entry.</summary>
  private static (byte R, byte G, byte B) _Colour(byte register) {
    var entry = (register & 0xFE) * 3;
    var gtia = Atari8BitGraphics.Palette;

    return (gtia[entry], gtia[entry + 1], gtia[entry + 2]);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NullData_Throws()
    => Assert.Throws<ArgumentNullException>(() => Graphics10PlusReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongLength_Throws() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => Graphics10PlusReader.FromBytes(new byte[2400]));
      Assert.Throws<InvalidDataException>(() => Graphics10PlusReader.FromBytes(new byte[2410]));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IsTheScreenShownFourTimesAsTall() {
    var image = Graphics10PlusFile.ToRawImage(Graphics10PlusReader.FromBytes(_Build((_, _) => 0)));

    Assert.Multiple(() => {
      Assert.That((image.Width, image.Height), Is.EqualTo((320, 240)));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  /// <summary>
  /// A stored pixel is four across and four down, so each one fills a sixteen-pixel block and the
  /// eighty stored across become the three hundred and twenty shown.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_DrawsEachStoredPixelFourWideAndFourTall() {
    // Register 4 (PF0) against register 8 (the background) in alternate stored columns.
    var image = Graphics10PlusFile.ToRawImage(
      Graphics10PlusReader.FromBytes(_Build((x, _) => (x & 1) == 0 ? 4 : 8)));

    var pf0 = _Colour(_Registers[4]);
    var bak = _Colour(_Registers[8]);

    Assert.Multiple(() => {
      for (var y = 0; y < 240; y += 17)
      for (var block = 0; block < 80; block += 7)
      for (var within = 0; within < 4; ++within)
        Assert.That(
          _At(image, block * 4 + within, y),
          Is.EqualTo((block & 1) == 0 ? pf0 : bak),
          $"stored column {block}, pixel {within}, row {y}");
    });
  }

  /// <summary>
  /// Sixteen values against nine registers: four of them are the background and four repeat the
  /// playfield.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_ReadsTheSevenAliasesAsTheRegistersTheyRepeat() {
    var image = Graphics10PlusFile.ToRawImage(Graphics10PlusReader.FromBytes(_Build((x, _) => x & 15)));

    Assert.Multiple(() => {
      for (var value = 0; value < 16; ++value) {
        var register = value switch {
          >= 9 and <= 11 => 8,
          >= 12 => value - 8,
          _ => value,
        };

        Assert.That(_At(image, value * 4 + 1, 100), Is.EqualTo(_Colour(_Registers[register])), $"nibble {value}");
      }
    });
  }

  /// <summary>The chip drops the low bit of a colour register, so an odd one reads as its neighbour.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_DropsTheLowBitOfEveryRegister() {
    var even = _Build((_, _) => 4);
    var odd = (byte[])even.Clone();
    odd[Graphics10PlusFile.RegisterOffset + 4] |= 1;

    Assert.That(
      Graphics10PlusFile.ToRawImage(Graphics10PlusReader.FromBytes(odd)).PixelData,
      Is.EqualTo(Graphics10PlusFile.ToRawImage(Graphics10PlusReader.FromBytes(even)).PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Registers_AreReadFromTheTailOfTheFile() {
    var file = Graphics10PlusReader.FromBytes(_Build((_, _) => 0));

    Assert.That(file.Registers, Is.EqualTo(_Registers));
  }

  /// <summary>
  /// Five characters after the dot, which is why the reference catalogue leaves this one out of its
  /// own list. Nothing here has that limit — <c>.farbfeld</c> and <c>.pspimage</c> are already
  /// registered — and this pins that down rather than leaving it to be rediscovered.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Registry_KnowsAFiveCharacterExtension() {
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.DetectFromExtension(".gr10p"), Is.EqualTo(ImageFormat.Graphics10Plus));
      Assert.That(FormatRegistry.DetectFromExtension(".gr10"), Is.Not.EqualTo(ImageFormat.Graphics10Plus));
    });
  }

  /// <summary>
  /// A picture built from the registers the writer chose comes back as the picture it was, because
  /// nothing in the mode is lossy once the colours are ones it can hold.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void RoundTrip_KeepsThePictureItWrote() {
    var source = Graphics10PlusFile.ToRawImage(Graphics10PlusReader.FromBytes(_Build((x, y) => (x + y) & 15)));

    var written = Graphics10PlusWriter.ToBytes(Graphics10PlusFile.FromRawImage(source));
    var read = Graphics10PlusFile.ToRawImage(Graphics10PlusReader.FromBytes(written));

    Assert.Multiple(() => {
      Assert.That(written, Has.Length.EqualTo(Graphics10PlusFile.FileSize));
      Assert.That(read.PixelData, Is.EqualTo(source.PixelData));
    });
  }
}

using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Some vintage formats can hold everything a picture offers and some cannot. These are the ones
/// that can — either because they carry whole pixels, or because the picture given to them is
/// already within what they hold — and for those, writing and reading back has to return exactly
/// what went in.
/// </summary>
/// <remarks>
/// A weaker check would be that a second round trip agrees with the first, which any writer passes
/// as soon as it is self-consistent, however wrong. Comparing against the input instead is the only
/// version that catches a writer and a reader making the same mistake — which is the failure this
/// project has hit more than once.
/// </remarks>
[TestFixture]
public sealed class LosslessWriterTests {

  /// <summary>A format that loses nothing, and the pictures it is expected to hold exactly.</summary>
  /// <param name="PairedRows">
  /// Whether the format stores half the rows and shows each twice. A picture whose row pairs differ
  /// cannot survive that, so the sample is made with matching pairs — which keeps the test about
  /// the encoding rather than about a halving nobody disputes.
  /// </param>
  public sealed record Case(
    string Name, ImageFormat Format, Palette Palette, int Width, int Height, bool PairedRows = false);

  /// <summary>What the format can hold, and therefore what the picture given to it is built from.</summary>
  public enum Palette {

    /// <summary>Black and white.</summary>
    Monochrome,

    /// <summary>The eight corners of the colour cube, which is one bit a channel.</summary>
    OneBitChannels,

    /// <summary>At most sixteen distinct colours, on a grid of four bits a channel.</summary>
    SixteenColors,

    /// <summary>A picture made of the ZX81's own character shapes.</summary>
    Zx81Glyphs,

    /// <summary>A picture made of the shapes the Semi-Graphic logos editor draws with.</summary>
    AtariGlyphs,

    /// <summary>A picture made of the machine's own character shapes, unpatched.</summary>
    AtariRomGlyphs,

    /// <summary>Two of the Spectrum's colours per character cell, which is all a cell may show.</summary>
    ZxAttributes,

    /// <summary>At most sixteen colours on any one scanline, each on a four-bit grid.</summary>
    SixteenPerLine,

    /// <summary>The eight cube corners, at half the horizontal resolution.</summary>
    OneBitChannelsWide,

    /// <summary>At most sixty-four colours, each channel on a grid of nine levels.</summary>
    SixtyFourColors,

    /// <summary>The Atari's sixteen greys, four screen pixels to each.</summary>
    AtariGreys,

    /// <summary>Anything.</summary>
    Full,
  }

  private static readonly Case[] _Cases = [
    new("HP 48 graphics object", ImageFormat.Hp48Grob, Palette.Monochrome, 131, 37),
    new("HP 48, whole bytes across", ImageFormat.Hp48Grob, Palette.Monochrome, 64, 12),
    new("DEGAS Elite icon", ImageFormat.DegasIcon, Palette.Monochrome, 37, 23),
    new("DEGAS Elite icon, whole words", ImageFormat.DegasIcon, Palette.Monochrome, 32, 8),
    new("Printfox block", ImageFormat.Printfox, Palette.Monochrome, 88, 40),
    new("True-colour GEM image", ImageFormat.TrueColorImg, Palette.Full, 96, 40),
    new("True-colour GEM image, one row", ImageFormat.TrueColorImg, Palette.Full, 300, 1),
    new("Kitty", ImageFormat.Kitty, Palette.OneBitChannels, 640, 400),
    new("Art Master 88", ImageFormat.ArtMaster88, Palette.OneBitChannels, 640, 400, PairedRows: true),
    new("ZXpaintyONE", ImageFormat.ZxPaintyOne, Palette.Zx81Glyphs, 256, 192),
    new("Semi-Graphic logos", ImageFormat.SemiGraphicLogo, Palette.AtariGlyphs, 320, 192),
    new("Dir Logo Maker", ImageFormat.DirLogoMaker, Palette.AtariRomGlyphs, 88, 128),
    new("CHR$", ImageFormat.ChrDollar, Palette.ZxAttributes, 96, 64),
    new("Border Screen by Trefi", ImageFormat.ZxTrefiBorderScreen, Palette.ZxAttributes, 256, 192),
    new("3200 colours, unpacked", ImageFormat.AppleSh3, Palette.SixteenPerLine, 320, 200),
    new("3201", ImageFormat.Apple3201, Palette.SixteenPerLine, 320, 200),
    new("LdPic", ImageFormat.LdPic, Palette.OneBitChannelsWide, 320, 256),
    new("Mapletown NL3", ImageFormat.MapletownNl3, Palette.SixtyFourColors, 160, 100),
    new("Graphics 9", ImageFormat.AtariPi9, Palette.AtariGreys, 320, 192),
  ];

  private static IEnumerable<TestCaseData> Cases() {
    foreach (var one in _Cases)
      yield return new TestCaseData(one).SetName($"{{m}}({one.Name})");
  }

  [TestCaseSource(nameof(Cases))]
  [Category("Unit")]
  public void Written_ReadsBackUnchanged(Case one) {
    var source = _Sample(one.Palette, one.Width, one.Height, one.PairedRows);

    var bytes = FormatRegistry.Write(source, one.Format);
    Assert.That(bytes, Is.Not.Null.And.Not.Empty, $"{one.Name}: produced no bytes");

    // The format is named rather than detected: these carry no signature a sniffer could use, and
    // what is under test is the encoding, not the detection.
    var entry = FormatRegistry.GetEntry(one.Format);
    Assert.That(entry, Is.Not.Null, $"{one.Name}: not registered");

    var read = entry!.LoadRawImageFromBytes(bytes!);
    Assert.That(read, Is.Not.Null, $"{one.Name}: our own output did not read back");

    var actual = PixelConverter.Convert(read!, PixelFormat.Rgb24);
    Assert.Multiple(() => {
      Assert.That(actual.Width, Is.GreaterThanOrEqualTo(one.Width), $"{one.Name}: too narrow");
      Assert.That(actual.Height, Is.GreaterThanOrEqualTo(one.Height), $"{one.Name}: too short");
    });

    // A cell-based format rounds its size up, so only the area the picture covers is compared;
    // what lies outside it was never the picture's to describe.
    for (var y = 0; y < one.Height; ++y)
    for (var x = 0; x < one.Width; ++x) {
      var expected = (y * one.Width + x) * 3;
      var got = (y * actual.Width + x) * 3;

      if (source.PixelData[expected] == actual.PixelData[got]
          && source.PixelData[expected + 1] == actual.PixelData[got + 1]
          && source.PixelData[expected + 2] == actual.PixelData[got + 2])
        continue;

      Assert.Fail(
        $"{one.Name}: pixel {x},{y} went in as "
        + $"({source.PixelData[expected]},{source.PixelData[expected + 1]},{source.PixelData[expected + 2]}) "
        + $"and came back as ({actual.PixelData[got]},{actual.PixelData[got + 1]},{actual.PixelData[got + 2]})");
    }
  }

  /// <summary>
  /// A picture already within what the format holds: black and white for the one-bit formats, and
  /// full colour for the ones that carry whole pixels.
  /// </summary>
  private static RawImage _Sample(Palette palette, int width, int height, bool pairedRows = false) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;

      // Where the format halves the rows, the two of a pair have to agree or nothing could hold
      // them; the pattern is drawn from the even row of each pair.
      var row = pairedRows ? y & ~1 : y;

      switch (palette) {
        // A pattern with runs in it and single pixels between them, so a run-length writer is
        // exercised on both and a stuck bit shows up as a shape rather than a shade.
        case Palette.Monochrome: {
          var ink = (x / 3 + row / 5) % 2 == 0 || (x % 7 == 3 && row % 4 != 1);
          rgb[at] = rgb[at + 1] = rgb[at + 2] = (byte)(ink ? 0 : 255);
          break;
        }

        // Each channel fully on or fully off, and the three varying independently so a swapped
        // pair of channels cannot pass.
        case Palette.OneBitChannels:
          rgb[at] = (byte)((x / 5 + row / 3) % 2 == 0 ? 255 : 0);
          rgb[at + 1] = (byte)((x / 7 + row) % 2 == 0 ? 255 : 0);
          rgb[at + 2] = (byte)((x + row / 11) % 2 == 0 ? 255 : 0);
          break;

        // Sixteen colours drawn from the four-bit grid the palette stores, so a picture using no
        // more than the format holds must come back exactly.
        case Palette.SixteenColors: {
          var index = (x / 9 + row / 7) % 16;
          rgb[at] = (byte)((index * 3 % 16) * 17);
          rgb[at + 1] = (byte)((index * 5 % 16) * 17);
          rgb[at + 2] = (byte)((index * 7 % 16) * 17);
          break;
        }

        // A picture the format can hold by construction: it is what its own shapes draw. Anything
        // else would be testing how good the match is, which is a judgement, not a fact.
        case Palette.Zx81Glyphs or Palette.AtariGlyphs or Palette.AtariRomGlyphs: {
          var glyphs = palette == Palette.Zx81Glyphs ? 64 : 128;
          ReadOnlySpan<byte> font = palette switch {
            Palette.Zx81Glyphs => FileFormat.Core.CharacterRoms.Zx81,
            Palette.AtariGlyphs => FileFormat.SemiGraphicLogo.SemiGraphicLogoFile.CreateFont(),
            _ => FileFormat.Core.CharacterRoms.Atari8,
          };

          var code = ((x / 8) * 7 + (row / 8) * 13) % (glyphs * 2);
          var inverse = code >= glyphs ? 255 : 0;
          var bits = font[((code % glyphs) << 3) + (row & 7)] ^ inverse;
          var lit = (bits >> (7 - x % 8)) & 1;

          // The two machines disagree about what a set bit shows and about what their own white
          // is: the ZX81's is white, and the Atari's brightest grey is a little short of it.
          var (ink, paper) = palette == Palette.Zx81Glyphs
            ? ((byte)0, (byte)255)
            : (FileFormat.Core.Atari8BitGraphics.ApplyPalette([14])[0], (byte)0);

          rgb[at] = rgb[at + 1] = rgb[at + 2] = lit != 0 ? ink : paper;
          break;
        }

        // Two colours a cell and no more, so the picture is within what an attribute can hold —
        // and the two are chosen to share brightness, which the hardware requires of them.
        case Palette.ZxAttributes: {
          var cell = (x / 8) + (row / 8) * 3;
          var bright = cell % 2;
          var ink = (bright * 8 + cell % 8) * 3;
          var paper = (bright * 8 + (cell * 5 + 3) % 8) * 3;
          var entry = (x / 2 + row / 3) % 2 == 0 ? ink : paper;

          rgb[at] = FileFormat.Core.ZxSpectrumGraphics.Palette[entry];
          rgb[at + 1] = FileFormat.Core.ZxSpectrumGraphics.Palette[entry + 1];
          rgb[at + 2] = FileFormat.Core.ZxSpectrumGraphics.Palette[entry + 2];
          break;
        }

        // Sixteen colours a line and a different sixteen on the next, which is what a per-line
        // palette buys — and far more than sixteen across the picture.
        case Palette.SixteenPerLine: {
          var index = (x / 5 + row) % 16;
          rgb[at] = (byte)(((index * 3 + row) % 16) * 17);
          rgb[at + 1] = (byte)(((index * 5 + row / 3) % 16) * 17);
          rgb[at + 2] = (byte)(((index * 7 + row / 7) % 16) * 17);
          break;
        }

        // The same eight colours, but a pixel pair always agreeing, since the mode that shows all
        // eight draws every logical pixel twice.
        case Palette.OneBitChannelsWide: {
          var wide = x & ~1;
          rgb[at] = (byte)((wide / 6 + row / 3) % 2 == 0 ? 255 : 0);
          rgb[at + 1] = (byte)((wide / 8 + row) % 2 == 0 ? 255 : 0);
          rgb[at + 2] = (byte)((wide / 2 + row / 11) % 2 == 0 ? 255 : 0);
          break;
        }

        // Sixty-four colours drawn from the nine-level grid the palette stores.
        case Palette.SixtyFourColors: {
          var index = (x / 3 + row / 2) % 64;
          rgb[at] = (byte)((index % 9) * 255 / 8);
          rgb[at + 1] = (byte)(((index / 9) % 9) * 255 / 8);
          rgb[at + 2] = (byte)(((index * 5) % 9) * 255 / 8);
          break;
        }

        // The chip's own sixteen greys, held across each group of four columns since that is what
        // one stored nibble covers.
        case Palette.AtariGreys: {
          var level = (x / 4 + row / 3) % 16;
          var grey = FileFormat.Core.Atari8BitGraphics.Palette;
          rgb[at] = grey[level * 3];
          rgb[at + 1] = grey[level * 3 + 1];
          rgb[at + 2] = grey[level * 3 + 2];
          break;
        }

        default:
          rgb[at] = (byte)(x * 37 + row);
          rgb[at + 1] = (byte)(row * 53 + x * 3);
          rgb[at + 2] = (byte)(x * row + 17);
          break;
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}

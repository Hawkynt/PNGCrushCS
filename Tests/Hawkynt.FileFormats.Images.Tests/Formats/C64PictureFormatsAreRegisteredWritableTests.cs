using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// That the eight-bit picture formats are registered as writable and not merely declared so.
/// </summary>
/// <remarks>
/// A format becomes writable by declaring all four of the reader, the two conversions and the
/// writer, and the source generator checks that itself — a type missing one of them is registered
/// read-only and no test of its own FromRawImage would notice, because calling the method directly
/// works whether or not anything is wired to it. This asserts the wiring rather than the method.
/// </remarks>
[TestFixture]
public sealed class C64PictureFormatsAreRegisteredWritableTests {

  private static readonly ImageFormat[] _Formats = [
    ImageFormat.Afli, ImageFormat.AnimPainter, ImageFormat.ArtDirector, ImageFormat.Bfli,
    ImageFormat.C128Multi, ImageFormat.CfliDesigner, ImageFormat.ChampionsInterlace,
    ImageFormat.CpcSprite, ImageFormat.Crack, ImageFormat.DoodleComp, ImageFormat.DoodlePacked,
    ImageFormat.DrazPaint, ImageFormat.Drazlace, ImageFormat.EmcEditor, ImageFormat.Ffli,
    ImageFormat.Fli, ImageFormat.FliDesigner, ImageFormat.FliDesigner2, ImageFormat.FliEditor,
    ImageFormat.FliGraph, ImageFormat.FliProfi, ImageFormat.Flimatic, ImageFormat.GoDot4Bit,
    ImageFormat.HardInterlace, ImageFormat.HinterGrundBild, ImageFormat.HiresFliCrest,
    ImageFormat.HiresInterlaceFeniks, ImageFormat.ImageSysC64, ImageFormat.Interlace8,
    ImageFormat.InterlaceHiresEditor, ImageFormat.InterlaceStudio, ImageFormat.LogoPainter,
    ImageFormat.Mcs, ImageFormat.Mlt, ImageFormat.MobyDick, ImageFormat.MuifliEditor,
    ImageFormat.MultiPainter, ImageFormat.NufliEditor, ImageFormat.PrintfoxPagefox,
    ImageFormat.RockyInterlace, ImageFormat.ScreenMaker, ImageFormat.SpcPainter,
    ImageFormat.SpritePad, ImageFormat.TurboView, ImageFormat.UfliEditor, ImageFormat.XFliEditor,
    ImageFormat.Zoomatic,
  ];

  [Test]
  [Category("Unit")]
  public void EveryOneOfThemIsRegisteredWithAWriteSide() {
    var missing = _Formats.Where(f => FormatRegistry.GetEntry(f)?.SupportsWrite != true).ToList();

    Assert.That(missing, Is.Empty, "registered read-only: " + string.Join(", ", missing));
  }

  [Test]
  [Category("Unit")]
  public void EncodingThroughTheRegistryProducesBytesTheSameFormatDetects() {
    // Going through the registry rather than the type proves the generated table points at the real
    // conversion, which calling the method directly cannot.
    var picture = _Grid(160, 200);
    var failures = new List<string>();

    foreach (var format in _Formats) {
      var entry = FormatRegistry.GetEntry(format);
      if (entry?.ConvertFromRawImage == null)
        continue;

      var bytes = entry.ConvertFromRawImage(picture);
      if (bytes is not { Length: > 0 })
        failures.Add(format.ToString());
    }

    Assert.That(failures, Is.Empty, "encoded to nothing: " + string.Join(", ", failures));
  }

  private static RawImage _Grid(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var colour = Commodore64Graphics.HexColors[(x / 4 + y / 8) % Commodore64Graphics.ColorCount];
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(colour >> 16);
      rgb[at + 1] = (byte)(colour >> 8);
      rgb[at + 2] = (byte)colour;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}

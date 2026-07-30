using System;
using System.IO;
using FileFormat.AtariTools800;
using FileFormat.Core;

namespace FileFormat.AtariTools800.Tests;

[TestFixture]
public sealed class AtariTools800Tests {

  private static readonly AtariTools800Kind[] _AllKinds = Enum.GetValues<AtariTools800Kind>();

  /// <summary>Where each sprite sits: its left edge and how many screen pixels it covers.</summary>
  private static (int Left, int Span, int Sprite)[] _BandLayout(AtariTools800Kind kind) {
    var bands = new System.Collections.Generic.List<(int, int, int)>();
    if (AtariTools800File.HasPlayers(kind))
      for (var sprite = 0; sprite < AtariTools800File.SpriteCount; ++sprite)
        bands.Add((sprite * AtariTools800File.PlayerPitch, AtariTools800File.PlayerWidth, sprite));

    if (AtariTools800File.HasMissiles(kind)) {
      var left = AtariTools800File.HasPlayers(kind) ? AtariTools800File.PlayersWidth : 0;
      for (var sprite = 0; sprite < AtariTools800File.SpriteCount; ++sprite)
        bands.Add((left + sprite * AtariTools800File.MissilePitch, AtariTools800File.MissileWidth, sprite));
    }

    return bands.ToArray();
  }

  /// <summary>Each sprite band filled solid in its own colour, with the gaps left black.</summary>
  private static RawImage _Bands(AtariTools800Kind kind) {
    var width = AtariTools800File.WidthFor(kind);
    var data = new byte[width * AtariTools800File.Height * 4];
    byte[][] colors = [[255, 0, 0], [0, 255, 0], [0, 0, 255], [255, 255, 0]];

    foreach (var (left, span, sprite) in _BandLayout(kind))
      for (var y = 0; y < AtariTools800File.Height; ++y)
      for (var x = 0; x < span; ++x) {
        var o = (y * width + left + x) * 4;
        data[o + 2] = colors[sprite][0];
        data[o + 1] = colors[sprite][1];
        data[o] = colors[sprite][2];
      }

    for (var i = 3; i < data.Length; i += 4)
      data[i] = 255;

    return new() { Width = width, Height = AtariTools800File.Height, Format = PixelFormat.Bgra32, PixelData = data };
  }

  [TestCase(AtariTools800Kind.Players, 964)]
  [TestCase(AtariTools800Kind.Missiles, 244)]
  [TestCase(AtariTools800Kind.PlayersAndMissiles, 1204)]
  [Category("Unit")]
  public void FileSizeFor_MatchesWhatReadersExpect(AtariTools800Kind kind, int expected)
    => Assert.That(AtariTools800File.FileSizeFor(kind), Is.EqualTo(expected));

  [TestCase(AtariTools800Kind.Players, 80)]
  [TestCase(AtariTools800Kind.Missiles, 32)]
  [TestCase(AtariTools800Kind.PlayersAndMissiles, 112)]
  [Category("Unit")]
  public void WidthFor_MatchesTheDisplayedSize(AtariTools800Kind kind, int expected)
    => Assert.That(AtariTools800File.WidthFor(kind), Is.EqualTo(expected));

  [TestCaseSource(nameof(_AllKinds))]
  [Category("Unit")]
  public void FromRawImage_PicksTheKindFromTheWidth(AtariTools800Kind kind)
    => Assert.That(AtariTools800File.FromRawImage(_Bands(kind)).Kind, Is.EqualTo(kind));

  [TestCaseSource(nameof(_AllKinds))]
  [Category("Unit")]
  public void RoundTrip_PreservesEverySection(AtariTools800Kind kind) {
    var file = AtariTools800File.FromRawImage(_Bands(kind));
    var restored = AtariTools800Reader.FromBytes(AtariTools800Writer.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Kind, Is.EqualTo(kind));
      Assert.That(restored.Colors, Is.EqualTo(file.Colors));
      Assert.That(restored.PlayerData, Is.EqualTo(file.PlayerData));
      Assert.That(restored.MissileData, Is.EqualTo(file.MissileData));
    });
  }

  [TestCaseSource(nameof(_AllKinds))]
  [Category("Unit")]
  public void EncodeThenDecode_LightsExactlyTheSpriteBands(AtariTools800Kind kind) {
    // The gaps between sprites are part of the picture, not padding: a shape or pitch mistake shows
    // up as a lit pixel outside a band or a dark one inside it.
    var decoded = AtariTools800File.ToRawImage(AtariTools800File.FromRawImage(_Bands(kind)));
    var width = AtariTools800File.WidthFor(kind);
    var bands = _BandLayout(kind);

    for (var y = 0; y < AtariTools800File.Height; y += 37)
    for (var x = 0; x < width; ++x) {
      var withinBand = Array.Exists(bands, band => x >= band.Left && x < band.Left + band.Span);
      var lit = decoded.PixelData[y * width + x] != 0;
      Assert.That(lit, Is.EqualTo(withinBand), $"pixel {x},{y}");
    }
  }

  [Test]
  [Category("Unit")]
  public void MissilesPackFourSpritesIntoOneByte() {
    var file = AtariTools800File.FromRawImage(_Bands(AtariTools800Kind.Missiles));

    Assert.Multiple(() => {
      Assert.That(file.MissileData, Has.Length.EqualTo(AtariTools800File.Height));
      Assert.That(file.MissileData[0], Is.EqualTo(0xFF), "all four missiles lit across both of their pixels");
    });
  }

  [TestCase(".4pl", AtariTools800Kind.Players)]
  [TestCase(".4mi", AtariTools800Kind.Missiles)]
  [TestCase(".4PM", AtariTools800Kind.PlayersAndMissiles)]
  [Category("Unit")]
  public void KindFromExtension_NamesTheDump(string extension, AtariTools800Kind expected)
    => Assert.That(AtariTools800File.KindFromExtension(extension), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnUnknownSize()
    => Assert.Throws<InvalidDataException>(() => AtariTools800Reader.FromBytes(new byte[500]));

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsOtherSizes() {
    var raw = new RawImage { Width = 64, Height = 240, Format = PixelFormat.Bgra32, PixelData = new byte[64 * 240 * 4] };

    Assert.Throws<ArgumentException>(() => AtariTools800File.FromRawImage(raw));
  }
}

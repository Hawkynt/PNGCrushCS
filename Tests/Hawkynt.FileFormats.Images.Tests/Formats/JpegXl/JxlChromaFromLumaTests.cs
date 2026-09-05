using System;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// How much of the luma the other two planes carry, and where that is stated.
/// </summary>
/// <remarks>
/// The two chroma planes are written down as what is left of them once their
/// share of the luma has been taken out, and how large that share is varies
/// across the picture — it is stated per tile of eight blocks by eight, not
/// once for the frame. Taking the frame's own figure everywhere is right only
/// where the encoder found nothing to state, which is to say on a picture with
/// no colour in it; every other picture was being put back together with the
/// wrong share of its luma in both chroma planes at once.
/// </remarks>
[TestFixture]
internal sealed class JxlChromaFromLumaTests {

  private static JxlChannel _Map(int width, int height, Func<int, int, int> value) {
    var pixels = new int[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = value(x, y);

    return new JxlChannel { Width = width, Height = height, HShift = 3, VShift = 3, Pixels = pixels };
  }

  private static JxlVarDctGroup _Group(int x, int y) => new() {
    X = x, Y = y, Width = 256, Height = 256, AcBlocks = [], LfBlocks = [],
  };

  /// <summary>A tile that states nothing leaves the frame's own figure alone.</summary>
  [Test]
  public void ATileStatingNothingKeepsTheFramesOwnFigure() {
    var map = _Map(4, 4, (_, _) => 0);

    var factor = JxlVarDctSpecDecoder._CorrelationAt(map, _Group(0, 0), bx: 0, by: 0, baseCorrelation: 1.0f);

    Assert.That(factor, Is.EqualTo(1.0f).Within(1e-6f));
  }

  /// <summary>What a tile states is added to it, divided by the factor the
  /// format scales these by.</summary>
  [Test]
  public void WhatATileStatesIsAddedToIt() {
    var map = _Map(4, 4, (x, y) => x == 1 && y == 0 ? 84 : 0);

    var inside = JxlVarDctSpecDecoder._CorrelationAt(map, _Group(0, 0), bx: 8, by: 0, baseCorrelation: 0.0f);
    var outside = JxlVarDctSpecDecoder._CorrelationAt(map, _Group(0, 0), bx: 0, by: 0, baseCorrelation: 0.0f);

    Assert.Multiple(() => {
      Assert.That(inside, Is.EqualTo(84.0f / JxlColorCorrelationMap.DefaultColorFactor).Within(1e-6f));
      Assert.That(outside, Is.Zero);
    });
  }

  /// <summary>The tile is eight blocks across, so the first eight blocks of a
  /// row share one figure and the ninth begins the next.</summary>
  /// <param name="bx">The block, counted from the left of the group.</param>
  /// <param name="expectedTile">Which tile it falls in.</param>
  [TestCase(0, 0)]
  [TestCase(7, 0)]
  [TestCase(8, 1)]
  [TestCase(23, 2)]
  public void ATileIsEightBlocksAcross(int bx, int expectedTile) {
    var map = _Map(4, 4, (x, _) => x * 84);

    var factor = JxlVarDctSpecDecoder._CorrelationAt(map, _Group(0, 0), bx, by: 0, baseCorrelation: 0.0f);

    Assert.That(factor,
      Is.EqualTo(expectedTile * 84.0f / JxlColorCorrelationMap.DefaultColorFactor).Within(1e-6f));
  }

  /// <summary>A group starting partway across the picture is counted from where
  /// it starts, not from its own left edge.</summary>
  [Test]
  public void AGroupIsCountedFromWhereItSitsInThePicture() {
    var map = _Map(8, 8, (x, _) => x * 84);

    // A group whose left edge is 256 pixels in starts at block 32, tile 4.
    var factor = JxlVarDctSpecDecoder._CorrelationAt(map, _Group(256, 0), bx: 0, by: 0, baseCorrelation: 0.0f);

    Assert.That(factor, Is.EqualTo(4 * 84.0f / JxlColorCorrelationMap.DefaultColorFactor).Within(1e-6f));
  }
}

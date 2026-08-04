using System;
using FileFormat.Blazing;
using FileFormat.Core;

namespace FileFormat.Blazing.Tests;

/// <summary>
/// Building a Blazing Paddles picture.
/// </summary>
/// <remarks>
/// Two shapes are saved under these names. The writer used to emit only the high-resolution one, so
/// a multicolour picture read in came back out at the wrong length with its colour memory dropped;
/// both samples are multicolour and RECOIL accepts nothing else at .blz or .pi.
/// </remarks>
[TestFixture]
public class BlazingAuthoringTests {

  private static RawImage _Picture() {
    var pixels = new byte[160 * 200 * 3];
    for (var i = 0; i < 160 * 200; ++i) {
      pixels[i * 3] = (byte)(i % 256);
      pixels[i * 3 + 1] = (byte)(i / 160 % 256);
    }

    return new() { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BlazingFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_BuildsTheMulticolourForm() {
    var file = BlazingFile.FromRawImage(_Picture());

    Assert.That(file.ColorData, Is.Not.Null, "which is what makes it the multicolour form");
  }

  [Test]
  public void ToBytes_IsTheLengthTheMulticolourFormHas() {
    // The sections are padded out to whole kilobytes: 2 + 8192 + 1024 + 1024.
    var bytes = BlazingWriter.ToBytes(BlazingFile.FromRawImage(_Picture()));

    Assert.That(bytes, Has.Length.EqualTo(BlazingFile.MulticolorFileSize));
  }

  [Test]
  public void ToBytes_PutsTheScreenWhereTheKilobyteBoundaryIs() {
    var file = BlazingFile.FromRawImage(_Picture());
    var marked = file with { ScreenData = _Filled(0x5A) };

    var bytes = BlazingWriter.ToBytes(marked);

    Assert.Multiple(() => {
      Assert.That(bytes[BlazingFile.MulticolorScreenOffset], Is.EqualTo(0x5A));
      Assert.That(bytes[BlazingFile.MulticolorScreenOffset - 1], Is.EqualTo(0), "the bitmap's padding");
    });
  }

  [Test]
  public void ToBytes_StillWritesTheHiresFormForAFileThatHoldsOne() {
    var hires = new BlazingFile {
      LoadAddress = 0x2000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
    };

    Assert.That(BlazingWriter.ToBytes(hires), Has.Length.EqualTo(BlazingFile.ExpectedFileSize));
  }

  [Test]
  public void RoundTrip_TheMulticolourScreenComesBackUnchanged() {
    var original = BlazingFile.FromRawImage(_Picture());

    var restored = BlazingReader.FromBytes(BlazingWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
      Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    });
  }

  [Test]
  public void FromRawImage_KeepsPatternZeroBlack() {
    // The format records no background anywhere, so the encoder has to be told rather than left to
    // choose one it cannot write down.
    var black = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };

    var drawn = BlazingFile.ToRawImage(BlazingReader.FromBytes(BlazingWriter.ToBytes(BlazingFile.FromRawImage(black)))).ToRgb24();

    Assert.That(drawn, Is.All.EqualTo(0));
  }

  private static byte[] _Filled(byte value) {
    var data = new byte[1000];
    Array.Fill(data, value);
    return data;
  }
}

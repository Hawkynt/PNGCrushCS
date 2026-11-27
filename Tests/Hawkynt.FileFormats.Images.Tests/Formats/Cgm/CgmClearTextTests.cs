using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Cgm;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Cgm.Tests;

/// <summary>The clear-text encoding, and the cell array forms the binary renderer reads.</summary>
[TestFixture]
public sealed class CgmClearTextTests {

  private static byte[] _Command(int elementClass, int elementId, params byte[] parameters) {
    var bytes = new List<byte>();
    var header = (elementClass << 12) | (elementId << 5) | Math.Min(parameters.Length, 31);
    bytes.Add((byte)(header >> 8));
    bytes.Add((byte)header);
    if (parameters.Length >= 31) {
      bytes.Add((byte)(parameters.Length >> 8));
      bytes.Add((byte)parameters.Length);
    }
    bytes.AddRange(parameters);
    if (parameters.Length % 2 == 1)
      bytes.Add(0);
    return [.. bytes];
  }

  private static byte[] _Words(params int[] values) => values.SelectMany(static v => new[] { (byte)(v >> 8), (byte)v }).ToArray();

  private static byte[] _Binary(params byte[][] body) {
    var bytes = new List<byte>();
    bytes.AddRange(_Command(0, 1, 4, (byte)'t', (byte)'e', (byte)'s', (byte)'t'));
    bytes.AddRange(_Command(0, 3, 0));
    bytes.AddRange(_Command(2, 6, _Words(0, 0, 1000, 500)));
    bytes.AddRange(_Command(0, 4));
    foreach (var command in body)
      bytes.AddRange(command);
    bytes.AddRange(_Command(0, 5));
    bytes.AddRange(_Command(0, 2));
    return [.. bytes];
  }

  private static RawImage _Render(byte[] data) => CgmFile.ToRawImage(CgmReader.FromBytes(data));

  [Test]
  [Category("Unit")]
  public void AClearTextPictureDrawsAsTheSameBinaryPictureDoes() {
    var text = """
      % a comment before anything %
      BegMF 'test';
        BegPic "";
          VDCExt 0,0, 1000,500;
        BegPicBody;
          ColrTable 1 255 0 0 / % slash ends an element too %
          IntStyle SOLID; Fill_Colr 1;
          Polygon (100,100) (900,100) 16#1F4 400;
          LineColr 1; LineWidth 3.0;
          Line 50 50 950 450;
        EndPic;
      EndMF;
      """;
    var binary = _Binary(
      _Command(5, 34, 1, 255, 0, 0),
      _Command(5, 22, _Words(1)),
      _Command(5, 23, 1),
      _Command(4, 7, _Words(100, 100, 900, 100, 500, 400)),
      _Command(5, 4, 1),
      _Command(5, 3, 0, 3, 0, 0),
      _Command(4, 1, _Words(50, 50, 950, 450)));
    var fromText = _Render(Encoding.ASCII.GetBytes(text));
    var fromBinary = _Render(binary);
    Assert.That((fromText.Width, fromText.Height), Is.EqualTo((fromBinary.Width, fromBinary.Height)));
    Assert.That(fromText.PixelData, Is.EqualTo(fromBinary.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryKnowsClearTextByItsFirstWord()
    => Assert.That(FormatRegistry.DetectFromBytes(Encoding.ASCII.GetBytes("  %x% BEGMF 'a'; ENDMF;")), Is.EqualTo(ImageFormat.Cgm));

  [Test]
  [Category("Unit")]
  public void AnUnknownEnumerationIsRefusedByName() {
    var ex = Assert.Throws<InvalidDataException>(() => CgmReader.FromBytes(Encoding.ASCII.GetBytes("BEGMF 'a'; BEGPIC 'p'; LINEWIDTHMODE SIDEWAYS; ENDPIC; ENDMF;")));
    Assert.That(ex!.Message, Does.Contain("SIDEWAYS"));
  }

  [Test]
  [Category("Unit")]
  public void AMetafileWithoutEndMfIsRefused()
    => Assert.Throws<InvalidDataException>(() => CgmReader.FromBytes(Encoding.ASCII.GetBytes("BEGMF 'a'; BEGPIC 'p';")));

  /// <summary>Cell arrays state a representation mode after the local precision; packed colours below a byte are bits.</summary>
  [Test]
  [Category("Unit")]
  public void ABitPackedCellArrayAndARunLengthOneDrawTheSame() {
    var table = _Command(5, 34, 0, 255, 255, 255, 255, 0, 0);
    // P (0,0), Q (1000,500), R (1000,0); 4 by 1 cells.
    var corners = _Words(0, 0, 1000, 500, 1000, 0, 4, 1);
    var packed = _Binary(table, _Command(4, 9, [.. corners, .. _Words(1, 1), 0b1010_0000, 0]));
    var runs = _Binary(table, _Command(4, 9, [.. corners, .. _Words(8, 0), .. _Words(1), 1, .. _Words(1), 0, .. _Words(1), 1, .. _Words(1), 0]));
    var a = _Render(packed);
    var b = _Render(runs);
    Assert.That(a.PixelData, Is.EqualTo(b.PixelData));
    int Red(RawImage image, int x) => image.PixelData[(image.Height / 2 * image.Width + x) * 4];
    int Green(RawImage image, int x) => image.PixelData[(image.Height / 2 * image.Width + x) * 4 + 1];
    Assert.That((Red(a, a.Width / 8), Green(a, a.Width / 8)), Is.EqualTo((255, 0)), "the first cell is index one, red");
    Assert.That(Green(a, 3 * a.Width / 8), Is.EqualTo(255), "the second is index nought, white");
  }

  /// <summary>
  /// Opt-in: <c>CGM_SAMPLES</c> names a folder holding telparia's binary CGM samples beside RAL-CGM
  /// 3.50's clear-text translations of them (<c>ralcgm X.cgm -t X.txt</c>); each pair must draw alike.
  /// RAL-CGM truncates very long point lists when it translates, so abydos and SKULL1AF are left out.
  /// </summary>
  [TestCase("corvette.cgm")]
  [TestCase("dessert4.cgm")]
  [TestCase("HAND1RSL.CGM")]
  [TestCase("hv1_02.cgm")]
  [TestCase("HV2_31.CGM")]
  [TestCase("hv3_16.cgm")]
  [TestCase("PTLA2_AF.CGM")]
  [TestCase("techdraw.cgm")]
  [TestCase("test.cgm")]
  [Category("Conformance")]
  [Explicit("Needs the CGM samples and their RAL-CGM translations; point CGM_SAMPLES at them.")]
  public void RalCgmsClearTextOfASampleDrawsAsTheSampleDoes(string name) {
    var directory = Environment.GetEnvironmentVariable("CGM_SAMPLES") ?? string.Empty;
    var binary = Path.Combine(directory, name);
    var text = Path.ChangeExtension(binary, ".txt");
    if (!File.Exists(binary) || !File.Exists(text))
      Assert.Ignore("CGM_SAMPLES does not hold this sample and its translation.");
    Assert.That(_Render(File.ReadAllBytes(text)).PixelData, Is.EqualTo(_Render(File.ReadAllBytes(binary)).PixelData));
  }
}

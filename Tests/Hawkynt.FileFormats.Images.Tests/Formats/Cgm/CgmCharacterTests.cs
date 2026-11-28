using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Cgm.Tests;

/// <summary>The character encoding of ISO/IEC 8632-2, read by translation into binary commands.</summary>
[TestFixture]
public sealed class CgmCharacterTests {

  // --- the character encoding, built by hand from its coding rules ----------------------------

  /// <summary>An integer: sign 0x10 and four bits in the first byte, five in each later one, 0x20 to go on.</summary>
  private static byte[] _Int(long value, bool exponentFollows = false, bool exponentAllowed = false) {
    var magnitude = Math.Abs(value);
    var firstBits = exponentAllowed ? 3 : 4;
    var groups = new List<int>();
    while (magnitude >= 1L << firstBits) {
      groups.Insert(0, (int)(magnitude & 0x1F));
      magnitude >>= 5;
    }

    var bytes = new List<byte>();
    var first = 0x40 | (int)magnitude | (value < 0 ? 0x10 : 0) | (exponentFollows ? 0x08 : 0) | (groups.Count > 0 ? 0x20 : 0);
    bytes.Add((byte)first);
    for (var i = 0; i < groups.Count; ++i)
      bytes.Add((byte)(0x40 | groups[i] | (i < groups.Count - 1 ? 0x20 : 0)));
    return [.. bytes];
  }

  private static byte[] _Str(string s) => [0x1B, 0x58, .. s.Select(static c => (byte)c), 0x1B, 0x5C];

  /// <summary>A direct colour of six-bit components: three bytes, two bits of each component a byte.</summary>
  private static byte[] _Rgb6(int r, int g, int b) {
    var bytes = new byte[3];
    for (var i = 0; i < 3; ++i) {
      var shift = 4 - 2 * i;
      int Bit(int v, int k) => (v >> (shift + k)) & 1;
      bytes[i] = (byte)(0x40 | Bit(r, 1) << 5 | Bit(g, 1) << 4 | Bit(b, 1) << 3 | Bit(r, 0) << 2 | Bit(g, 0) << 1 | Bit(b, 0));
    }

    return bytes;
  }

  private static byte[] _Op(int opcode) => opcode > 0xFF ? [(byte)(opcode >> 8), (byte)opcode] : [(byte)opcode];

  private static byte[] _Character(params byte[][] parts) => [.. parts.SelectMany(static p => p)];

  // --- the binary encoding, for comparison ------------------------------------------------------

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

  private static RawImage _Render(byte[] data) => CgmFile.ToRawImage(CgmReader.FromBytes(data));

  [Test]
  [Category("Unit")]
  public void ACharacterEncodedPictureDrawsAsTheSameBinaryPictureDoes() {
    // A substitution list (NUL and LF), so a line feed inside the stream is noise; incremental
    // points; run-length colour table; a bit-stream cell array of two-bit indices.
    var character = _Character(
      _Op(0x3020), [0x7E, 0x40, 0x7E, 0x4A, 0x1B, 0x5C], _Str("test"),
      _Op(0x3127), _Int(2), // colour index precision: two bits
      _Op(0x3022), _Str("p"),
      _Op(0x3225), _Int(0), _Int(0), _Int(1000), _Int(500),
      _Op(0x3023),
      _Op(0x3630), _Int(1), [0x42], _Rgb6(63, 0, 0), _Int(1), _Rgb6(0, 63, 0), _Int(1), [0x0A],
      _Op(0x3621), _Int(1),
      _Op(0x3622), _Int(1),
      // POLYGON (100,100) (900,100) (500,400) as differences.
      _Op(0x26), _Int(100), _Int(100), _Int(800), _Int(0), _Int(-400), _Int(300),
      _Op(0x3523), _Int(2),
      _Op(0x20), _Int(50), _Int(50), _Int(900), _Int(400),
      // CELL ARRAY P (0,0) Q (1000,500) R (1000,0), 3 by 1, precision 2, bit stream 01 10 01.
      _Op(0x28), _Int(0), _Int(0), _Int(1000), _Int(500), _Int(1000), _Int(0), _Int(3), _Int(1), _Int(2), [0x41, 0x40 | 0b011001],
      _Op(0x3024),
      _Op(0x3021));

    // The binary side states the character encoding's six-bit colour extent so the colours agree.
    var binary = new List<byte>();
    binary.AddRange(_Command(0, 1, 4, (byte)'t', (byte)'e', (byte)'s', (byte)'t'));
    binary.AddRange(_Command(1, 10, 0, 0, 0, 63, 63, 63));
    binary.AddRange(_Command(0, 3, 0));
    binary.AddRange(_Command(2, 6, _Words(0, 0, 1000, 500)));
    binary.AddRange(_Command(0, 4));
    binary.AddRange(_Command(5, 34, 1, 63, 0, 0, 0, 63, 0));
    binary.AddRange(_Command(5, 22, _Words(1)));
    binary.AddRange(_Command(5, 23, 1));
    binary.AddRange(_Command(4, 7, _Words(100, 100, 900, 100, 500, 400)));
    binary.AddRange(_Command(5, 4, 2));
    binary.AddRange(_Command(4, 1, _Words(50, 50, 950, 450)));
    binary.AddRange(_Command(4, 9, [.. _Words(0, 0, 1000, 500, 1000, 0, 3, 1, 8, 1), 1, 2, 1, 0]));
    binary.AddRange(_Command(0, 5));
    binary.AddRange(_Command(0, 2));

    var fromCharacter = _Render(character);
    var fromBinary = _Render([.. binary]);
    Assert.That((fromCharacter.Width, fromCharacter.Height), Is.EqualTo((fromBinary.Width, fromBinary.Height)));
    Assert.That(fromCharacter.PixelData, Is.EqualTo(fromBinary.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void RealCoordinatesTakeTheDefaultExponentOrTheirOwn() {
    // VDC type real, then VDC real precision allowing an exponent, default -1: halves.
    var character = _Character(
      _Op(0x3020), [0x1B, 0x5C], _Str("r"),
      _Op(0x3122), _Int(1),
      _Op(0x3022), _Str("p"),
      _Op(0x3321), _Int(10), _Int(-10), _Int(-1), _Int(0),
      // VDC extent 0,0 to 200 (400 halves), 100 (25 times four, with its own exponent of 2).
      _Op(0x3225), _Int(0, false, true), _Int(0, false, true), _Int(400, false, true), _Int(25, true, true), _Int(2),
      _Op(0x3023),
      _Op(0x3024),
      _Op(0x3021));
    var file = CgmReader.FromBytes(character);
    var image = CgmFile.ToRawImage(file);
    Assert.That((image.Width, image.Height), Is.EqualTo((200, 100)));
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryKnowsTheCharacterEncodingByItsOpcode() {
    Assert.That(FormatRegistry.DetectFromBytes(_Character(_Op(0x3020), [0x1B, 0x5C], _Str("a"), _Op(0x3021))), Is.EqualTo(ImageFormat.Cgm));
    Assert.That(CgmCharacter.Matches("0 hello world\n"u8), Is.False, "two printable characters are not enough");
  }

  [Test]
  [Category("Unit")]
  public void HuffmanCodedPointsAreRefusedByName() {
    var character = _Character(
      _Op(0x3020), [0x1B, 0x5C], _Str("h"),
      _Op(0x3022), _Str("p"),
      _Op(0x3225), _Int(0), _Int(0), _Int(10), _Int(10),
      _Op(0x3023),
      _Op(0x20), [0x50, 0x40],
      _Op(0x3024),
      _Op(0x3021));
    var ex = Assert.Throws<NotSupportedException>(() => CgmReader.FromBytes(character));
    Assert.That(ex!.Message, Does.Contain("Huffman"));
  }

  [Test]
  [Category("Unit")]
  public void AMetafileWithoutEndMetafileIsRefused()
    => Assert.Throws<InvalidDataException>(() => CgmReader.FromBytes(_Character(_Op(0x3020), [0x1B, 0x5C], _Str("a"), _Op(0x3022), _Str("p"))));

  /// <summary>
  /// Opt-in: <c>CGM_SAMPLES</c> names a folder holding telparia's binary CGM samples beside RAL-CGM
  /// 3.50's character-encoded translations of them (<c>ralcgm X.cgm -c X.chr</c>), and the
  /// character-encoded <c>input.chr</c> beside RAL-CGM's binary translation <c>input.bin</c>; each
  /// pair must draw alike.
  /// </summary>
  /// <remarks>
  /// Three samples are left out because RAL-CGM's character writer does not carry them across whole:
  /// it cuts abydos's first polygon set to 27 of its 1236 points and one of SKULL1AF's polygons to
  /// 392 of 514, and it drops one of corvette's LINE WIDTH elements as if it were already current.
  /// The bytes of its output say so; this reader draws what they hold.
  /// </remarks>
  [TestCase("input.bin")]
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
  public void RalCgmsCharacterEncodingOfASampleDrawsAsTheSampleDoes(string name) {
    var directory = Environment.GetEnvironmentVariable("CGM_SAMPLES") ?? string.Empty;
    var binary = Path.Combine(directory, name);
    var character = Path.ChangeExtension(binary, ".chr");
    if (!File.Exists(binary) || !File.Exists(character))
      Assert.Ignore("CGM_SAMPLES does not hold this sample and its translation.");
    Assert.That(_Render(File.ReadAllBytes(character)).PixelData, Is.EqualTo(_Render(File.ReadAllBytes(binary)).PixelData));
  }
}

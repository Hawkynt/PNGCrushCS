using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.TrueType;

namespace FileFormat.TrueType.Tests;

/// <summary>
/// The fixtures are fonts built byte by byte from Microsoft's OpenType specification: a table
/// directory, a head, a maxp, a loca and a glyf, with the malformed ones each breaking one rule.
/// The reader was also checked against a real font — AdwaitaSans-Regular — where its units to the
/// em, its glyph count, its contour sizes and the coordinates and on-curve flags of a simple glyph
/// and of a composite one all match what fontTools reads out of the same file.
/// </summary>
[TestFixture]
public sealed class TrueTypeTests {

  private static void _U16(List<byte> into, int value) {
    into.Add((byte)(value >> 8));
    into.Add((byte)value);
  }

  private static void _U32(List<byte> into, uint value) {
    into.Add((byte)(value >> 24));
    into.Add((byte)(value >> 16));
    into.Add((byte)(value >> 8));
    into.Add((byte)value);
  }

  /// <summary>A head table: fifty-four bytes with the magic number, the em and the offset format.</summary>
  private static byte[] _Head(int unitsPerEm = 1000, int longOffsets = 0, uint magic = TrueTypeFile.HeadMagic) {
    var head = new byte[54];
    head[12] = (byte)(magic >> 24);
    head[13] = (byte)(magic >> 16);
    head[14] = (byte)(magic >> 8);
    head[15] = (byte)magic;
    head[18] = (byte)(unitsPerEm >> 8);
    head[19] = (byte)unitsPerEm;
    head[50] = (byte)(longOffsets >> 8);
    head[51] = (byte)longOffsets;

    return head;
  }

  private static byte[] _Maxp(int glyphCount) {
    var maxp = new List<byte>();
    _U32(maxp, 0x00010000);
    _U16(maxp, glyphCount);

    return maxp.ToArray();
  }

  /// <summary>A loca table in the short format, which stores each offset halved.</summary>
  private static byte[] _Loca(params int[] offsets) {
    var loca = new List<byte>();
    foreach (var offset in offsets)
      _U16(loca, offset / 2);

    return loca.ToArray();
  }

  /// <summary>One closed contour of points, all of them on the curve.</summary>
  private static byte[] _SimpleGlyph(params (int X, int Y)[] points) {
    var glyph = new List<byte>();
    _U16(glyph, 1);
    _U16(glyph, 0);
    _U16(glyph, 0);
    _U16(glyph, 1000);
    _U16(glyph, 1000);
    _U16(glyph, points.Length - 1);
    _U16(glyph, 0);

    // On the curve, with both coordinates written as a signed sixteen-bit step.
    foreach (var _ in points)
      glyph.Add(0x01);

    var x = 0;
    foreach (var point in points) {
      _U16(glyph, (ushort)(short)(point.X - x));
      x = point.X;
    }

    var y = 0;
    foreach (var point in points) {
      _U16(glyph, (ushort)(short)(point.Y - y));
      y = point.Y;
    }

    // A glyph starts on an even offset, which the short loca format requires.
    if ((glyph.Count & 1) != 0)
      glyph.Add(0);

    return glyph.ToArray();
  }

  /// <summary>A glyph that is another glyph placed at an offset.</summary>
  private static byte[] _CompositeGlyph(int component, int dx, int dy) {
    var glyph = new List<byte>();
    _U16(glyph, 0xFFFF);
    _U16(glyph, 0);
    _U16(glyph, 0);
    _U16(glyph, 1000);
    _U16(glyph, 1000);
    _U16(glyph, 0x0003);
    _U16(glyph, component);
    _U16(glyph, (ushort)(short)dx);
    _U16(glyph, (ushort)(short)dy);

    return glyph.ToArray();
  }

  /// <summary>Assembles a font out of its four tables and a directory pointing at them.</summary>
  private static byte[] _Font(byte[] head, byte[] maxp, byte[] loca, byte[] glyf, uint version = TrueTypeFile.TrueTypeVersion, int? statedTableCount = null, int? statedGlyfLength = null) {
    var tables = new (string Tag, byte[] Data)[] { ("glyf", glyf), ("head", head), ("loca", loca), ("maxp", maxp) };
    var count = statedTableCount ?? tables.Length;

    var header = new List<byte>();
    _U32(header, version);
    _U16(header, count);
    _U16(header, 0);
    _U16(header, 0);
    _U16(header, 0);

    var body = new List<byte>();
    var at = 12 + tables.Length * 16;
    foreach (var (tag, data) in tables) {
      header.AddRange(Encoding.ASCII.GetBytes(tag));
      _U32(header, 0);
      _U32(header, (uint)(at + body.Count));
      _U32(header, (uint)(tag == "glyf" && statedGlyfLength.HasValue ? statedGlyfLength.Value : data.Length));
      body.AddRange(data);

      // Every table starts on a four-byte boundary.
      while ((body.Count & 3) != 0)
        body.Add(0);
    }

    return header.Concat(body).ToArray();
  }

  /// <summary>A font of two glyphs: an empty one and a triangle.</summary>
  private static byte[] _Triangle() {
    var glyph = _SimpleGlyph((0, 0), (900, 0), (450, 900));

    return _Font(_Head(), _Maxp(2), _Loca(0, 0, glyph.Length), glyph);
  }

  private static double _Coverage(RawImage image) {
    var pixels = image.PixelData;
    var total = 0.0;
    for (var i = 0; i < pixels.Length; i += 4)
      total += (255 - pixels[i]) / 255.0;

    return total / (image.Width * image.Height);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TrueTypeReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheEmAndTheGlyphCount() {
    var font = TrueTypeReader.FromBytes(_Triangle());

    Assert.Multiple(() => {
      Assert.That(font.UnitsPerEm, Is.EqualTo(1000));
      Assert.That(font.GlyphCount, Is.EqualTo(2));
      Assert.That(font.Glyphs[0].Contours, Is.Empty, "a glyph of no length is a space, not an error");
      Assert.That(font.Glyphs[1].Contours, Has.Count.EqualTo(1));
      Assert.That(font.Glyphs[1].Contours[0], Has.Count.EqualTo(3));
    });
  }

  /// <summary>Coordinates are steps from the point before, not positions.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_CoordinatesAreStepsFromThePointBefore() {
    var font = TrueTypeReader.FromBytes(_Triangle());
    var contour = font.Glyphs[1].Contours[0];

    Assert.Multiple(() => {
      Assert.That(contour[0], Is.EqualTo(new TrueTypePoint(0, 0, true)));
      Assert.That(contour[1], Is.EqualTo(new TrueTypePoint(900, 0, true)));
      Assert.That(contour[2], Is.EqualTo(new TrueTypePoint(450, 900, true)));
    });
  }

  /// <summary>
  /// A flag with the repeat bit set is followed by how many more points share it. A reader that
  /// took one flag a point would read the count as a flag and every coordinate after it wrongly.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ARepeatedFlagStandsForSeveralPoints() {
    var glyph = new List<byte>();
    _U16(glyph, 1);
    _U16(glyph, 0);
    _U16(glyph, 0);
    _U16(glyph, 1000);
    _U16(glyph, 1000);
    _U16(glyph, 2);
    _U16(glyph, 0);
    glyph.Add(0x01 | 0x08);
    glyph.Add(2);
    for (var i = 0; i < 3; ++i)
      _U16(glyph, 100);

    for (var i = 0; i < 3; ++i)
      _U16(glyph, 50);

    var data = glyph.ToArray();
    var font = TrueTypeReader.FromBytes(_Font(_Head(), _Maxp(1), _Loca(0, data.Length), data));

    Assert.That(font.Glyphs[0].Contours[0], Is.EqualTo(new[] {
      new TrueTypePoint(100, 50, true),
      new TrueTypePoint(200, 100, true),
      new TrueTypePoint(300, 150, true)
    }));
  }

  /// <summary>A composite glyph is another glyph placed under an offset.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ACompositeGlyphIsTheGlyphItNamesMovedAcross() {
    var simple = _SimpleGlyph((0, 0), (100, 0), (50, 100));
    var composite = _CompositeGlyph(0, 500, -20);
    var glyf = simple.Concat(composite).ToArray();
    var font = TrueTypeReader.FromBytes(_Font(_Head(), _Maxp(2), _Loca(0, simple.Length, glyf.Length), glyf));

    Assert.That(font.Glyphs[1].Contours[0], Is.EqualTo(new[] {
      new TrueTypePoint(500, -20, true),
      new TrueTypePoint(600, -20, true),
      new TrueTypePoint(550, 80, true)
    }));
  }

  // ---------------------------------------------------------------- refusals

  [Test]
  [Category("Unit")]
  public void FromBytes_AFontWhoseOutlinesAreCharstringsIsRefusedByName()
    => Assert.That(
      Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
        _Font(_Head(), _Maxp(1), _Loca(0, 0), [], TrueTypeFile.OpenTypeCffTag)
      ))!.Message,
      Does.Contain("CFF")
    );

  [Test]
  [Category("Unit")]
  public void FromBytes_ACollectionOfFontsIsRefusedByName()
    => Assert.That(
      Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
        _Font(_Head(), _Maxp(1), _Loca(0, 0), [], TrueTypeFile.CollectionTag)
      ))!.Message,
      Does.Contain("collection")
    );

  [Test]
  [Category("Unit")]
  public void FromBytes_AVersionNoFontStatesIsRefused()
    => Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
      _Font(_Head(), _Maxp(1), _Loca(0, 0), [], 0x12345678)
    ));

  /// <summary>
  /// The head table carries a magic number so that a table pointed at by a wrong offset can be
  /// told from the real one. Reading a wrong table's bytes as an em would size every glyph wrongly.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AHeadWithoutItsMagicNumberIsRefused()
    => Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
      _Font(_Head(magic: 0xDEADBEEF), _Maxp(1), _Loca(0, 0), [])
    ));

  /// <summary>A directory that claims more tables than the file holds records for.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ADirectoryLongerThanTheFileIsRefused()
    => Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
      _Font(_Head(), _Maxp(1), _Loca(0, 0), [], statedTableCount: 400)
    ));

  /// <summary>A table whose stated length runs past the end of the file.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ATableRunningPastTheEndOfTheFileIsRefused()
    => Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
      _Font(_Head(), _Maxp(1), _Loca(0, 0), [], statedGlyfLength: 1 << 20)
    ));

  /// <summary>
  /// The loca table holds one entry more than there are glyphs, the last being where the last one
  /// ends. A shorter one cannot say where the last glyph stops.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ALocaShorterThanTheGlyphCountNeedsIsRefused() {
    var glyph = _SimpleGlyph((0, 0), (100, 0), (50, 100));

    Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
      _Font(_Head(), _Maxp(2), _Loca(0, glyph.Length), glyph)
    ));
  }

  /// <summary>An offset that goes backwards would make a glyph of negative length.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_LocaOffsetsThatGoBackwardsAreRefused() {
    var glyph = _SimpleGlyph((0, 0), (100, 0), (50, 100));

    Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
      _Font(_Head(), _Maxp(2), _Loca(0, glyph.Length, 0), glyph)
    ));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_GlyphsRunningPastTheGlyfTableAreRefused() {
    var glyph = _SimpleGlyph((0, 0), (100, 0), (50, 100));

    Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
      _Font(_Head(), _Maxp(1), _Loca(0, glyph.Length + 64), glyph)
    ));
  }

  /// <summary>The contours end at points that run up the list, so each has to be past the one before.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ContoursEndingOutOfOrderAreRefused() {
    var glyph = new List<byte>();
    _U16(glyph, 2);
    _U16(glyph, 0);
    _U16(glyph, 0);
    _U16(glyph, 1000);
    _U16(glyph, 1000);
    _U16(glyph, 5);
    _U16(glyph, 2);
    _U16(glyph, 0);
    var data = glyph.ToArray();

    Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
      _Font(_Head(), _Maxp(1), _Loca(0, data.Length), data)
    ));
  }

  /// <summary>A glyph that claims more points than it carries coordinates for.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AGlyphThatRunsOutOfCoordinatesIsRefused() {
    var glyph = new List<byte>();
    _U16(glyph, 1);
    _U16(glyph, 0);
    _U16(glyph, 0);
    _U16(glyph, 1000);
    _U16(glyph, 1000);
    _U16(glyph, 9);
    _U16(glyph, 0);
    glyph.Add(0x01);
    var data = glyph.ToArray();

    Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
      _Font(_Head(), _Maxp(1), _Loca(0, data.Length), data)
    ));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AGlyphThatIsItsOwnComponentIsRefused() {
    var composite = _CompositeGlyph(0, 0, 0);

    Assert.Throws<InvalidDataException>(() => TrueTypeReader.FromBytes(
      _Font(_Head(), _Maxp(1), _Loca(0, composite.Length), composite)
    ));
  }

  // ---------------------------------------------------------------- the sheet

  /// <summary>
  /// The sheet is sixteen glyphs to a row at a fixed cell, so its size follows the glyph count and
  /// nothing else.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_TheSheetIsSixteenGlyphsToARow() {
    var image = TrueTypeFile.ToRawImage(TrueTypeReader.FromBytes(_Triangle()));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(2 * TrueTypeFile.SheetCell));
      Assert.That(image.Height, Is.EqualTo(TrueTypeFile.SheetCell));
    });
  }

  /// <summary>
  /// The outline is filled rather than traced, and a triangle covers about half of what its box
  /// does. A reader that only stroked the outline would lay down a fraction of that.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_TheOutlineIsFilled() {
    var image = TrueTypeFile.ToRawImage(TrueTypeReader.FromBytes(_Triangle()));

    Assert.That(_Coverage(image), Is.GreaterThan(0.1).And.LessThan(0.5));
  }
}

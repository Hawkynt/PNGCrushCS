using System;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Dxf;

namespace FileFormat.Dxf.Tests;

/// <summary>
/// No sample drawing was available, so every fixture here is built from Autodesk's DXF Reference
/// group-code tables — the pairs are written out the way the reference says a file writes them, and
/// the malformed ones break exactly one rule each.
/// </summary>
[TestFixture]
public sealed class DxfTests {

  /// <summary>Writes a run of group codes the way a DXF file does: code line, then value line.</summary>
  private static string _Pairs(params (int Code, string Value)[] pairs)
    => string.Concat(pairs.Select(pair => $"{pair.Code,3}\r\n{pair.Value}\r\n"));

  private static string _Section(string name, string body)
    => _Pairs((0, "SECTION"), (2, name)) + body + _Pairs((0, "ENDSEC"));

  /// <summary>A whole drawing: the stated extents, then the entities, then the end.</summary>
  private static string _Drawing(string entities, string? extents = "0\n0\n10\n10", string extra = "") {
    var header = string.Empty;
    if (extents != null) {
      var values = extents.Split('\n');
      header = _Pairs(
        (9, "$EXTMIN"), (10, values[0]), (20, values[1]),
        (9, "$EXTMAX"), (10, values[2]), (20, values[3])
      );
    }

    return _Section("HEADER", header) + extra + _Section("ENTITIES", entities) + _Pairs((0, "EOF"));
  }

  private static DxfFile _Read(string text) => DxfReader.FromBytes(Encoding.ASCII.GetBytes(text));

  private static RawImage _Draw(string text) => DxfFile.ToRawImage(_Read(text));

  /// <summary>How much of the picture is ink, taking the drawing as made on white paper.</summary>
  private static double _Coverage(RawImage image) {
    var pixels = image.PixelData;
    var total = 0.0;
    for (var i = 0; i < pixels.Length; i += 4)
      total += (255 - pixels[i]) / 255.0;

    return total / (image.Width * image.Height);
  }

  /// <summary>
  /// Whether any pixel leans hard towards one channel, which is what a coloured line looks like on
  /// white paper once its edges have been softened against the background.
  /// </summary>
  private static bool _Leans(RawImage image, int channel) {
    var pixels = image.PixelData;
    for (var i = 0; i < pixels.Length; i += 4) {
      var others = 0;
      for (var other = 0; other < 3; ++other)
        if (other != channel)
          others = Math.Max(others, pixels[i + other]);

      if (pixels[i + channel] - others > 100)
        return true;
    }

    return false;
  }

  private const string _Line = "  0\r\nLINE\r\n 10\r\n1\r\n 20\r\n1\r\n 11\r\n9\r\n 21\r\n9\r\n";

  // ---------------------------------------------------------------- structure

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DxfReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsEveryGroupCodeAndValue() {
    var file = _Read(_Drawing(_Line));

    Assert.Multiple(() => {
      Assert.That(file.Pairs.Any(pair => pair.Code == 0 && pair.Value == "SECTION"));
      Assert.That(file.Pairs.Count(pair => pair.Code == 0 && pair.Value == "ENDSEC"), Is.EqualTo(2));
      Assert.That(file.Pairs[^1], Is.EqualTo(new DxfPair(0, "EOF")));
    });
  }

  /// <summary>The group codes are written padded to three columns, and that padding is not part of them.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_PaddedGroupCodesAreTheSameCodes() {
    var file = _Read(_Drawing(_Line));

    Assert.That(file.Pairs.Any(pair => pair.Code == 9 && pair.Value == "$EXTMIN"));
  }

  /// <summary>
  /// A drawing is text with no magic number, so prose has to be refused on structure alone. A reader
  /// that only looked for the word SECTION would take any document that contained it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ProseIsNotADrawing()
    => Assert.Throws<InvalidDataException>(() => _Read("The quick brown fox\r\njumps over SECTION\r\nthe lazy dog\r\n"));

  [Test]
  [Category("Unit")]
  public void FromBytes_TheBinaryFormIsRefusedByNameRatherThanMisparsed() {
    var data = Encoding.ASCII.GetBytes(DxfFile.BinarySentinel + "\r\n\0").Concat(new byte[64]).ToArray();

    Assert.That(
      Assert.Throws<InvalidDataException>(() => DxfReader.FromBytes(data))!.Message,
      Does.Contain("binary")
    );
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AGroupCodeWithNoValueAfterItIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Section("HEADER", string.Empty) + "  0\r\n"));

  [Test]
  [Category("Unit")]
  public void FromBytes_AFileThatNeverReachesEofIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Section("ENTITIES", _Line)));

  [Test]
  [Category("Unit")]
  public void FromBytes_ASectionThatIsNeverClosedIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Pairs((0, "SECTION"), (2, "ENTITIES")) + _Line + _Pairs((0, "EOF"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_ASectionOpenedInsideAnotherIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(
      _Pairs((0, "SECTION"), (2, "HEADER"), (0, "SECTION"), (2, "ENTITIES"), (0, "ENDSEC"), (0, "EOF"))
    ));

  [Test]
  [Category("Unit")]
  public void FromBytes_AnEndsecWithNothingOpenIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(
      _Section("ENTITIES", _Line) + _Pairs((0, "ENDSEC"), (0, "EOF"))
    ));

  [Test]
  [Category("Unit")]
  public void FromBytes_AFileWithNoEntitiesSectionIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Section("HEADER", string.Empty) + _Pairs((0, "EOF"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_AGroupCodeOutsideTheDefinedRangeIsRefused()
    => Assert.Throws<InvalidDataException>(() => _Read(_Section("ENTITIES", _Pairs((9999, "x"))) + _Pairs((0, "EOF"))));

  // ---------------------------------------------------------------- entities

  /// <summary>
  /// Group 90 states how many vertices an LWPOLYLINE has. A file where it does not match what
  /// follows has been written or cut wrongly, and drawing it anyway draws a shape nobody stated.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_AnLwpolylineWhoseStatedVertexCountIsWrongIsRefused() {
    var polyline = _Pairs(
      (0, "LWPOLYLINE"), (90, "4"), (70, "1"),
      (10, "0"), (20, "0"),
      (10, "10"), (20, "0"),
      (10, "10"), (20, "10")
    );

    Assert.That(
      Assert.Throws<InvalidDataException>(() => _Draw(_Drawing(polyline)))!.Message,
      Does.Contain("4")
    );
  }

  /// <summary>A POLYLINE's vertices run until a SEQEND, and a run that never reaches one has no end.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_APolylineWithNoSeqendIsRefused() {
    var polyline = _Pairs(
      (0, "POLYLINE"), (70, "0"),
      (0, "VERTEX"), (10, "0"), (20, "0"),
      (0, "VERTEX"), (10, "10"), (20, "10")
    );

    Assert.Throws<InvalidDataException>(() => _Draw(_Drawing(polyline)));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AnInsertNamingABlockTheFileDoesNotDefineIsRefused() {
    var insert = _Pairs((0, "INSERT"), (2, "MISSING"), (10, "0"), (20, "0"));

    Assert.Throws<InvalidDataException>(() => _Draw(_Drawing(insert)));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ABlockThatIsNeverClosedIsRefused() {
    var blocks = _Section("BLOCKS", _Pairs((0, "BLOCK"), (2, "PART"), (10, "0"), (20, "0")) + _Line);

    Assert.Throws<InvalidDataException>(() => _Draw(_Drawing(_Line, extra: blocks)));
  }

  // ---------------------------------------------------------------- geometry

  /// <summary>
  /// The size is the extent the header states, so a drawing stated twice as wide comes out twice as
  /// wide. A reader that fitted everything to one square would give the same two numbers for both.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_TheSizeFollowsTheStatedExtents() {
    var square = _Draw(_Drawing(_Line, "0\n0\n10\n10"));
    var wide = _Draw(_Drawing(_Line, "0\n0\n20\n10"));

    Assert.Multiple(() => {
      Assert.That(square.Width, Is.EqualTo(square.Height).Within(2));
      Assert.That((double)wide.Width / wide.Height, Is.GreaterThan(1.7));
    });
  }

  /// <summary>
  /// AutoCAD writes 1.0E+20 into the extents of a drawing whose extents it has never computed. That
  /// is not a box, and a reader that took it literally would try to draw a picture 1e20 units wide.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_UncomputedExtentsFallBackToWhatTheGeometryCovers() {
    var line = _Pairs((0, "LINE"), (10, "0"), (20, "0"), (11, "40"), (21, "20"));
    var image = _Draw(_Drawing(line, "1.0E+20\n1.0E+20\n-1.0E+20\n-1.0E+20"));

    Assert.That((double)image.Width / image.Height, Is.EqualTo(2).Within(0.1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ACircleIsRoundAndIsARimRatherThanADisc() {
    var image = _Draw(_Drawing(_Pairs((0, "CIRCLE"), (10, "5"), (20, "5"), (40, "4"))));

    Assert.Multiple(() => {
      Assert.That((double)image.Width / image.Height, Is.EqualTo(1).Within(0.05));
      Assert.That(_Coverage(image), Is.GreaterThan(0.001).And.LessThan(0.3));
    });
  }

  /// <summary>
  /// An arc runs anticlockwise from its start angle to its end angle, so ninety to a hundred and
  /// eighty is the top left quarter. Taking the pair the other way round would draw the other three
  /// quarters, which is three times the ink.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_AnArcRunsAnticlockwiseFromItsStartAngleToItsEnd() {
    var quarter = _Draw(_Drawing(_Pairs((0, "ARC"), (10, "5"), (20, "5"), (40, "4"), (50, "90"), (51, "180"))));
    var whole = _Draw(_Drawing(_Pairs((0, "CIRCLE"), (10, "5"), (20, "5"), (40, "4"))));

    Assert.That(_Coverage(quarter), Is.EqualTo(_Coverage(whole) / 4).Within(_Coverage(whole) * 0.15));
  }

  /// <summary>
  /// The four corners of a SOLID are stored with the last two the far pair round, so joining them
  /// in the order they are written gives a bow tie that covers half the square.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_ASolidCoversItsSquareRatherThanCrossingItself() {
    var solid = _Pairs(
      (0, "SOLID"),
      (10, "0"), (20, "0"),
      (11, "10"), (21, "0"),
      (12, "0"), (22, "10"),
      (13, "10"), (23, "10")
    );

    Assert.That(_Coverage(_Draw(_Drawing(solid))), Is.GreaterThan(0.85));
  }

  /// <summary>
  /// A bulge is the tangent of a quarter of the arc's included angle, made negative when the arc
  /// runs clockwise, so a bulge of one over a chord ten long is a semicircle five deep. A reader
  /// that ignored the bulge would draw the chord and the picture would have no height at all.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_ABulgeOfOneTurnsASegmentIntoASemicircle() {
    var polyline = _Pairs(
      (0, "LWPOLYLINE"), (90, "2"), (70, "0"),
      (10, "0"), (20, "0"), (42, "1"),
      (10, "10"), (20, "0")
    );

    var image = _Draw(_Drawing(polyline, extents: null));

    Assert.That((double)image.Width / image.Height, Is.EqualTo(2).Within(0.15));
  }

  /// <summary>
  /// A closed LWPOLYLINE joins its last vertex back to its first. Leaving that segment out would
  /// draw three sides of the square rather than four.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_AClosedPolylineDrawsTheSegmentBackToItsStart() {
    string Square(string flag) => _Pairs(
      (0, "LWPOLYLINE"), (90, "4"), (70, flag),
      (10, "1"), (20, "1"),
      (10, "9"), (20, "1"),
      (10, "9"), (20, "9"),
      (10, "1"), (20, "9")
    );

    Assert.That(_Coverage(_Draw(_Drawing(Square("1")))), Is.GreaterThan(_Coverage(_Draw(_Drawing(Square("0")))) * 1.2));
  }

  /// <summary>An INSERT places a block, and a row and column count places it more than once.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_AnInsertPlacesTheBlockItNames() {
    var blocks = _Section("BLOCKS",
      _Pairs((0, "BLOCK"), (2, "TICK"), (10, "0"), (20, "0"), (0, "LINE"), (10, "0"), (20, "0"), (11, "0"), (21, "4"), (0, "ENDBLK"))
    );

    var once = _Pairs((0, "INSERT"), (2, "TICK"), (10, "2"), (20, "1"));
    var twice = _Pairs((0, "INSERT"), (2, "TICK"), (10, "2"), (20, "1"), (70, "2"), (44, "5"));

    Assert.That(
      _Coverage(_Draw(_Drawing(twice, extra: blocks))),
      Is.EqualTo(_Coverage(_Draw(_Drawing(once, extra: blocks))) * 2).Within(0.005)
    );
  }

  /// <summary>
  /// Group 62 is the colour number and index one is red. An entity that states nothing takes its
  /// layer's colour, which is what the LAYER table carries.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_AnEntityDrawsInItsOwnColourIndex() {
    var red = _Pairs((0, "LINE"), (62, "1"), (10, "1"), (20, "5"), (11, "9"), (21, "5"));

    Assert.That(_Leans(_Draw(_Drawing(red)), 0), "index one is red");
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AnEntityWithoutAColourTakesTheOneItsLayerCarries() {
    var tables = _Section("TABLES", _Pairs((0, "TABLE"), (2, "LAYER"), (0, "LAYER"), (2, "PIPES"), (62, "5"), (0, "ENDTAB")));
    var line = _Pairs((0, "LINE"), (8, "PIPES"), (10, "1"), (20, "5"), (11, "9"), (21, "5"));

    Assert.That(_Leans(_Draw(_Drawing(line, extra: tables)), 2), "index five is blue");
  }

  /// <summary>
  /// TEXT names a style, a style names a font file, and the font file is not in the drawing. The
  /// entity is therefore read and passed over rather than approximated by a box.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_TextIsNotDrawn() {
    var text = _Pairs((0, "TEXT"), (10, "1"), (20, "1"), (40, "8"), (1, "HELLO"));

    Assert.That(_Coverage(_Draw(_Drawing(_Line + text))), Is.EqualTo(_Coverage(_Draw(_Drawing(_Line)))).Within(0.0001));
  }
}

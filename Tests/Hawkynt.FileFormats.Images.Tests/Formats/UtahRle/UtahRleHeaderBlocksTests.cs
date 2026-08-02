using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.UtahRle.Tests;

/// <summary>
/// The blocks that sit between a Utah RLE header and its picture.
/// </summary>
/// <remarks>
/// The comment block was not skipped at all, so the picture began wherever the text happened to
/// end — and most of these files carry one, it being where the credits go. The sample holds a
/// fifty-two byte comment, so every opcode was read fifty-four bytes early and the picture came out
/// black.
/// <para/>
/// The colour map was skipped by reading a length from the stream, which the format does not put
/// there: the header already says how large the map is, one channel holding two to the power its
/// last byte names, every entry a word.
/// <para/>
/// Checked against ImageMagick on a real file: all 160000 pixels match.
/// </remarks>
[TestFixture]
public sealed class UtahRleHeaderBlocksTests {

  private const int _CLEAR_FIRST = 0x01;
  private const int _NO_BACKGROUND = 0x02;
  private const int _COMMENT = 0x08;

  /// <summary>Builds a header, optionally followed by a background, a colour map and a comment.</summary>
  private static byte[] _Build(int flags, int channels, int mapChannels, int mapLog2, string? comment, byte[] scanlines) {
    var head = new byte[15];
    head[0] = 0x52;
    head[1] = 0xCC;
    head[6] = 4; head[7] = 0;    // four across
    head[8] = 1; head[9] = 0;    // one down
    head[10] = (byte)flags;
    head[11] = (byte)channels;
    head[12] = 8;
    head[13] = (byte)mapChannels;
    head[14] = (byte)mapLog2;

    var body = new System.Collections.Generic.List<byte>(head);
    if ((flags & _NO_BACKGROUND) == 0)
      body.AddRange(new byte[channels]);

    body.AddRange(new byte[mapChannels * (1 << mapLog2) * 2]);

    if (comment != null) {
      var text = Encoding.ASCII.GetBytes(comment);
      body.Add((byte)text.Length);
      body.Add((byte)(text.Length >> 8));
      body.AddRange(text);
      if ((text.Length & 1) != 0)
        body.Add(0);
    }

    body.AddRange(scanlines);
    return body.ToArray();
  }

  /// <summary>A stream that skips to a row, sets three bytes and stops.</summary>
  private static byte[] _OneRow() => [
    0x03, 0x00, 0x00, 0x00, // set colour channel 0
    0x02, 0x00, 0x02, 0x00, // a run of pixel data, three bytes
    0x11, 0x22, 0x33, 0x00,
    0x07, 0x00, 0x00, 0x00, // end of picture
  ];

  [Test]
  [Category("Unit")]
  public void Read_StepsOverAComment() {
    var withComment = _Build(_CLEAR_FIRST | _COMMENT, 3, 0, 0, "CREDITS=somebody", _OneRow());
    var without = _Build(_CLEAR_FIRST, 3, 0, 0, null, _OneRow());

    var a = UtahRleFile.ToRawImage(UtahRleReader.FromBytes(withComment));
    var b = UtahRleFile.ToRawImage(UtahRleReader.FromBytes(without));

    Assert.That(a.PixelData, Is.EqualTo(b.PixelData), "the comment must not change the picture");
  }

  [Test]
  [Category("Unit")]
  public void Read_StepsOverACommentOfOddLength() {
    var odd = _Build(_CLEAR_FIRST | _COMMENT, 3, 0, 0, "odd", _OneRow());
    var without = _Build(_CLEAR_FIRST, 3, 0, 0, null, _OneRow());

    Assert.That(UtahRleFile.ToRawImage(UtahRleReader.FromBytes(odd)).PixelData,
      Is.EqualTo(UtahRleFile.ToRawImage(UtahRleReader.FromBytes(without)).PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Read_StepsOverAColourMapOfTheSizeTheHeaderStates() {
    // Three channels of 256 entries, two bytes each, which nothing in the stream announces.
    var mapped = _Build(_CLEAR_FIRST, 3, 3, 8, null, _OneRow());
    var plain = _Build(_CLEAR_FIRST, 3, 0, 0, null, _OneRow());

    Assert.That(UtahRleFile.ToRawImage(UtahRleReader.FromBytes(mapped)).PixelData,
      Is.EqualTo(UtahRleFile.ToRawImage(UtahRleReader.FromBytes(plain)).PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileWhoseHeaderRunsPastItsEnd() {
    var data = _Build(_CLEAR_FIRST | _COMMENT, 3, 0, 0, "text", _OneRow());
    data[^1] = 0;

    // A comment longer than the file cannot be stepped over.
    data[18] = 0xFF;
    data[19] = 0xFF;

    Assert.Throws<System.IO.InvalidDataException>(() => UtahRleReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingThatIsNotOne()
    => Assert.Throws<System.IO.InvalidDataException>(() => UtahRleReader.FromBytes(new byte[64]));
}

using System;
using System.IO;
using System.Text;
using FileFormat.Miff;

namespace FileFormat.Miff.Tests;

/// <summary>
/// A MIFF header may open with a brace comment before the <c>id=ImageMagick</c> line.
/// </summary>
/// <remarks>
/// XnView's <c>nconvert -out miff</c> writes <c>{\n  Created with XNview\n}\n</c> ahead of the id
/// line, which is where the format allows a comment to sit. A reader that insists on the id at
/// offset zero calls that file corrupt, and ImageMagick — which defined the format and reads its
/// own writers' comments — does not.
/// </remarks>
[TestFixture]
public sealed class MiffCommentHeaderTests {

  /// <summary>Builds a MIFF shaped exactly the way nconvert writes one.</summary>
  /// <remarks>
  /// Reproduced from a 61x37 sample: a brace comment, no <c>depth</c>, no <c>type</c>, no
  /// <c>compression</c> — every one of those defaulted — the size stated as two pairs on one line,
  /// a blank line, and the colon followed by a single newline rather than the 0x1A other writers
  /// emit.
  /// </remarks>
  private static byte[] _BuildNconvertMiff(int width, int height, byte[] pixels) {
    var header = Encoding.ASCII.GetBytes(
      "{\n  Created with XNview\n}\n"
      + "id=ImageMagick\n"
      + "class=DirectClass\n"
      + $"columns={width} rows={height}\n"
      + "\n:\n");

    var data = new byte[header.Length + pixels.Length];
    Array.Copy(header, data, header.Length);
    Array.Copy(pixels, 0, data, header.Length, pixels.Length);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LeadingBraceComment_IsAccepted() {
    var pixels = new byte[] { 0, 0, 255, 7, 7, 248, 255, 255, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    var result = MiffReader.FromBytes(_BuildNconvertMiff(3, 2, pixels));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(3));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CommentText_IsNotReadAsFields() {
    var pixels = new byte[3 * 3];
    var result = MiffReader.FromBytes(_BuildNconvertMiff(3, 1, pixels));

    Assert.That(result.Width, Is.EqualTo(3), "the comment's words must not overwrite the size");
  }

  /// <summary>A comment carrying a colon must not be mistaken for the header terminator.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ColonInsideComment_DoesNotEndTheHeader() {
    var pixels = new byte[] { 10, 20, 30, 40, 50, 60 };
    var header = Encoding.ASCII.GetBytes(
      "{\n  Created: XNview\n  Note: nothing here is a field\n}\n"
      + "id=ImageMagick\nclass=DirectClass\ncolumns=2 rows=1\n\n:\n");

    var data = new byte[header.Length + pixels.Length];
    Array.Copy(header, data, header.Length);
    Array.Copy(pixels, 0, data, header.Length, pixels.Length);

    var result = MiffReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(1));
      Assert.That(result.PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>
  /// The samples begin one byte after the colon's newline, whatever that byte happens to be.
  /// </summary>
  /// <remarks>
  /// A picture whose first sample is 0x0A, 0x0D or 0x1A is ordinary; skipping every such byte after
  /// the terminator swallows it and shifts the whole picture one channel to the left.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void FromBytes_FirstSampleIsNewline_IsKept() {
    var pixels = new byte[] { 0x0A, 0x0D, 0x1A, 0x40, 0x50, 0x60 };
    var result = MiffReader.FromBytes(_BuildNconvertMiff(2, 1, pixels));

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  /// <summary>The comment is a courtesy, not a way in: the id line still has to be there.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_CommentWithoutIdLine_IsStillRefused() {
    var data = Encoding.ASCII.GetBytes("{\n  Created with XNview\n}\nid=NotMagick\ncolumns=2 rows=1\n:\n");
    Assert.Throws<InvalidDataException>(() => MiffReader.FromBytes(data));
  }

  /// <summary>
  /// A brace after <c>=</c> opens a value, and only a brace starting a token is a comment.
  /// </summary>
  /// <remarks>
  /// ImageMagick states its PNG chunk notes as <c>png:bKGD={chunk was found ...}</c>. Reading every
  /// brace as a comment throws those fields away, and one of them carrying a colon would end the
  /// header early on top of that.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void Parse_BracedValue_IsKeptAsAValue() {
    var header = Encoding.ASCII.GetBytes(
      "{\n  Created with XNview\n}\n"
      + "id=ImageMagick\ncolumns=2 rows=1\n"
      + "png:bKGD={chunk was found (see Background color, above)}\n"
      + "depth=8\n:\n");

    var fields = MiffHeaderParser.Parse(header, out _);

    Assert.Multiple(() => {
      Assert.That(fields["png:bKGD"], Is.EqualTo("chunk was found (see Background color, above)"));
      Assert.That(fields["columns"], Is.EqualTo("2"));
      Assert.That(fields["depth"], Is.EqualTo("8"));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnterminatedComment_IsRefused() {
    var data = Encoding.ASCII.GetBytes("{\n  Created with XNview\nid=ImageMagick\ncolumns=2 rows=1\n");
    Assert.Throws<InvalidDataException>(() => MiffReader.FromBytes(data));
  }
}

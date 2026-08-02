using System;
using System.IO;
using FileFormat.JpegXr;

namespace FileFormat.JpegXr.Tests;

/// <summary>
/// What our JPEG XR support actually does, as opposed to what it appeared to do.
/// </summary>
/// <remarks>
/// This file used to hold eleven round trips: build a picture, write it, read it back, compare. They
/// all passed and none of them meant anything. The writer does not produce a JPEG XR bitstream — it
/// stores the pixels — and the reader recovered them through a fallback that copied the compressed
/// data into the picture whenever the plane header would not parse. Writer and reader agreed with
/// each other about a format neither was speaking.
/// <para/>
/// The two real files in the corpus settle it: the codec runs to completion on both and draws
/// neither, differing from XnView's rendering by 117 and 83 of 255 a channel. So the reader declines
/// now rather than returning a picture that is not the one in the file, and these tests say that
/// instead of dressing the bubble up as a round trip.
/// <para/>
/// The container above the codec is sound and worth keeping: the four tags naming where the picture
/// begins were written 0xBCE0 to 0xBCE3, where the standard puts them at 0xBCC0 to 0xBCC3, so no
/// JPEG XR was ever found at all. That is fixed, which is why the size below is read correctly.
/// </remarks>
[TestFixture]
public sealed class RoundTripTests {

  /// <summary>Builds the smallest well-formed container: header, IFD, and somewhere to point at.</summary>
  private static byte[] _Container(int width, int height) {
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);

    writer.Write((byte)0x49);
    writer.Write((byte)0x49);
    writer.Write((byte)0xBC);
    writer.Write((byte)0x01);
    writer.Write(8);

    writer.Write((ushort)4);

    void Entry(ushort tag, uint value) {
      writer.Write(tag);
      writer.Write((ushort)4);
      writer.Write(1u);
      writer.Write(value);
    }

    Entry(0xBC80, (uint)width);
    Entry(0xBC81, (uint)height);
    Entry(0xBCC0, 62);
    Entry(0xBCC1, 16);
    writer.Write(0);

    while (stream.Length < 62 + 16)
      writer.Write((byte)0);

    return stream.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesTheSizeFromTheTagsTheStandardNames() {
    // 0xBC80 and 0xBC81 for the size, 0xBCC0 and 0xBCC1 for where the picture is — the last two were
    // looked for at 0xBCE0 and 0xBCE1, which no file writes.
    var failure = Assert.Catch<Exception>(() => JpegXrReader.FromBytes(_Container(800, 600)));

    Assert.That(failure, Is.Not.Null, "the codec should decline rather than return a picture");
    Assert.That(failure!.Message, Does.Contain("800x600"),
      "the container should have been read even though the codec declines");
  }

  [Test]
  [Category("Unit")]
  public void Read_DeclinesRatherThanReturningAPictureItCannotDecode() {
    var failure = Assert.Catch<NotSupportedException>(() => JpegXrReader.FromBytes(_Container(4, 3)));

    Assert.That(failure!.Message, Does.Contain("does not reproduce"));
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingThatIsNotAJpegXrAtAll()
    => Assert.Throws<InvalidDataException>(() => JpegXrReader.FromBytes(new byte[64]));
}

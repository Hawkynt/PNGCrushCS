using System;
using System.IO;
using System.Linq;
using FileFormat.Avi.Tests;
using FileFormat.Jpeg;

namespace FileFormat.Mjpeg.Tests;

[TestFixture]
public sealed class MjpegReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MjpegReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromSpan_NotAJpeg_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MjpegReader.FromBytes([0x00, 0x01, 0x02, 0x03]));

  [Test]
  [Category("Unit")]
  public void FromSpan_SingleJpeg_IsOneFrame() {
    var file = MjpegReader.FromBytes(AviReaderTests._Jpeg(0));

    Assert.That(MjpegFile.ImageCount(file), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_ConcatenatedJpegs_AreSeparateFrames() {
    var file = MjpegReader.FromBytes(_Concatenate(AviReaderTests._Jpeg(0), AviReaderTests._Jpeg(1), AviReaderTests._Jpeg(2)));

    Assert.That(MjpegFile.ImageCount(file), Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void EachFrameEqualsTheSameJpegDecodedOnItsOwn() {
    var jpegs = new[] { AviReaderTests._Jpeg(0), AviReaderTests._Jpeg(1), AviReaderTests._Jpeg(2) };
    var file = MjpegReader.FromBytes(_Concatenate(jpegs));

    for (var index = 0; index < jpegs.Length; ++index) {
      var throughStream = MjpegFile.ToRawImage(file, index);
      var direct = JpegFile.ToRawImage(JpegReader.FromBytes(jpegs[index]));

      Assert.That(throughStream.ToRgb24(), Is.EqualTo(direct.ToRgb24()), $"frame {index}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FramesSurviveAnEntropySegmentThatLooksLikeAnEndMarker() {
    // The split walks the marker structure rather than searching for FF D9, because entropy-coded
    // data may contain those two bytes and a search would cut a frame in half there.
    var jpegs = Enumerable.Range(0, 6).Select(AviReaderTests._Jpeg).ToArray();
    var file = MjpegReader.FromBytes(_Concatenate(jpegs));

    Assert.That(MjpegFile.ImageCount(file), Is.EqualTo(jpegs.Length));
    for (var index = 0; index < jpegs.Length; ++index)
      Assert.That(MjpegFile.ToRawImage(file, index).ToRgb24(),
        Is.EqualTo(JpegFile.ToRawImage(JpegReader.FromBytes(jpegs[index])).ToRgb24()), $"frame {index}");
  }

  [Test]
  [Category("Unit")]
  public void TruncatedTrailingFrame_IsNotCounted() {
    // Half a JPEG is not a picture; the frames before it still are.
    var complete = AviReaderTests._Jpeg(0);
    var truncated = AviReaderTests._Jpeg(1);
    var stream = _Concatenate(complete, truncated[..(truncated.Length / 2)]);

    var file = MjpegReader.FromBytes(stream);

    Assert.That(MjpegFile.ImageCount(file), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ALongStreamSplitsIntoAllOfItsFrames() {
    var frame = AviReaderTests._Jpeg(0);

    Assert.That(MjpegReader.FromBytes(_Repeat(frame, 400)).Frames.Count, Is.EqualTo(400));
  }

  [Test]
  [Category("Performance")]
  public void ManyFramesSplitInTimeProportionalToTheStream() {
    // The split used to call the whole chunk enumeration on the rest of the stream once per frame,
    // and that walks everything past the frame's end marker as trailing metadata — quadratic in the
    // file's length, which is the one place a Motion JPEG file is not small. Doubling the frame count
    // must not quadruple the work. Wall-clock, so advisory: it is here to catch the shape coming back.
    var frame = AviReaderTests._Jpeg(0);
    var shortTime = _TimeSplit(_Repeat(frame, 200));
    var longTime = _TimeSplit(_Repeat(frame, 400));

    // Linear is about 2, quadratic about 4.
    Assert.That(longTime.TotalMilliseconds, Is.LessThan(Math.Max(shortTime.TotalMilliseconds, 5) * 3),
      $"400 frames took {longTime.TotalMilliseconds} ms where 200 took {shortTime.TotalMilliseconds} ms");
  }

  private static TimeSpan _TimeSplit(byte[] stream) {
    MjpegReader.FromBytes(stream); // once to warm the code path up
    var started = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < 5; ++i)
      MjpegReader.FromBytes(stream);

    return started.Elapsed / 5;
  }

  private static byte[] _Repeat(byte[] frame, int count) {
    var result = new byte[frame.Length * count];
    for (var i = 0; i < count; ++i)
      frame.CopyTo(result, i * frame.Length);

    return result;
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IndexOutOfRange_Throws() {
    var file = MjpegReader.FromBytes(AviReaderTests._Jpeg(0));

    Assert.Throws<ArgumentOutOfRangeException>(() => MjpegFile.ToRawImage(file, 1));
    Assert.Throws<ArgumentOutOfRangeException>(() => MjpegFile.ToRawImage(file, -1));
  }

  private static byte[] _Concatenate(params byte[][] parts) {
    var result = new byte[parts.Sum(p => p.Length)];
    var offset = 0;
    foreach (var part in parts) {
      part.CopyTo(result, offset);
      offset += part.Length;
    }

    return result;
  }
}

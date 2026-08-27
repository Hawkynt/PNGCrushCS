extern alias Images;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FileFormat.Avi.Tests;
using FileFormat.Core;
using JpegFile = Images::FileFormat.Jpeg.JpegFile;
using JpegReader = Images::FileFormat.Jpeg.JpegReader;
using JpegWriter = Images::FileFormat.Jpeg.JpegWriter;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Mjpeg.Tests;

/// <summary>
/// The raw Motion JPEG reader's behaviour, carried over from when it lived in the image package.
/// </summary>
/// <remarks>
/// One behaviour deliberately changed. The old reader split the whole stream in its constructor and
/// threw if the result held no complete frame; the split is now walked one frame at a time, so a
/// stream that begins with a start-of-image marker and then stops walks to no packets instead of
/// refusing at open. What it refuses eagerly — data that is not a JPEG at all — is unchanged.
/// </remarks>
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
  public void FromSpan_SingleJpeg_IsOneFrame()
    => Assert.That(_Packets(AviReaderTests._Jpeg(0)).Count, Is.EqualTo(1));

  [Test]
  [Category("Unit")]
  public void FromSpan_ConcatenatedJpegs_AreSeparateFrames()
    => Assert.That(_Packets(_Concatenate(AviReaderTests._Jpeg(0), AviReaderTests._Jpeg(1), AviReaderTests._Jpeg(2))).Count, Is.EqualTo(3));

  [Test]
  [Category("Unit")]
  public void EachFrameEqualsTheSameJpegDecodedOnItsOwn() {
    var jpegs = new[] { AviReaderTests._Jpeg(0), AviReaderTests._Jpeg(1), AviReaderTests._Jpeg(2) };
    var frames = _Frames(_Concatenate(jpegs));

    for (var index = 0; index < jpegs.Length; ++index) {
      var direct = JpegFile.ToRawImage(JpegReader.FromBytes(jpegs[index]));

      Assert.That(frames[index].ToRgb24(), Is.EqualTo(direct.ToRgb24()), $"frame {index}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FramesSurviveAnEntropySegmentThatLooksLikeAnEndMarker() {
    // The split walks the marker structure rather than searching for FF D9, because entropy-coded
    // data may contain those two bytes and a search would cut a frame in half there.
    var jpegs = Enumerable.Range(0, 6).Select(AviReaderTests._Jpeg).ToArray();
    var frames = _Frames(_Concatenate(jpegs));

    Assert.That(frames.Count, Is.EqualTo(jpegs.Length));
    for (var index = 0; index < jpegs.Length; ++index)
      Assert.That(frames[index].ToRgb24(),
        Is.EqualTo(JpegFile.ToRawImage(JpegReader.FromBytes(jpegs[index])).ToRgb24()), $"frame {index}");
  }

  [Test]
  [Category("Unit")]
  public void TruncatedTrailingFrame_IsNotCounted() {
    // Half a JPEG is not a picture; the frames before it still are.
    var complete = AviReaderTests._Jpeg(0);
    var truncated = AviReaderTests._Jpeg(1);

    Assert.That(_Packets(_Concatenate(complete, truncated[..(truncated.Length / 2)])).Count, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ALongStreamSplitsIntoAllOfItsFrames()
    => Assert.That(_Packets(_Repeat(AviReaderTests._Jpeg(0), 400)).Count, Is.EqualTo(400));

  [Test]
  [Category("Unit")]
  public void EveryFrameIsAPointDecodingMayStartAt() {
    // Motion JPEG has no prediction between frames, so every packet stands on its own. A container
    // that said otherwise would make a seek to any frame impossible.
    Assert.That(_Packets(_Repeat(AviReaderTests._Jpeg(0), 3)).Select(p => p.IsKeyFrame), Is.All.True);
    Assert.That(_Packets(_Repeat(AviReaderTests._Jpeg(0), 3)).Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 1, 2 }));
  }

  [Test]
  [Category("Unit")]
  public void OpeningAStreamDoesNotSplitIt() {
    // A stream that starts with a start-of-image marker and then stops. The old reader split
    // everything in its constructor and threw here because it had found no complete frame; this one
    // has not looked, and does not look until a packet is asked for.
    var whole = AviReaderTests._Jpeg(0);
    var container = MjpegReader.FromBytes(whole[..(whole.Length / 2)]);

    Assert.That(MjpegContainer.ReadPackets(container), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PacketsAreWindowsOntoTheStreamRatherThanCopies() {
    var stream = _Concatenate(AviReaderTests._Jpeg(0), AviReaderTests._Jpeg(1));
    var container = MjpegReader.FromBytes(stream);

    // Every packet slices the caller's own array rather than holding a copy of its bytes. A copy of
    // a film is the one allocation this library cannot afford.
    foreach (var packet in MjpegContainer.ReadPackets(container)) {
      Assert.That(MemoryMarshal.TryGetArray(packet.Data, out var segment), Is.True);
      Assert.That(segment.Array, Is.SameAs(stream));
    }
  }

  [Test]
  [Category("Unit")]
  public void TheStreamIsDeclaredAsMotionJpeg() {
    var streams = MjpegContainer.Streams(MjpegReader.FromBytes(AviReaderTests._Jpeg(0)));

    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(streams[0].Codec.ToString(), Is.EqualTo("MJPG"));
    // No dimensions: a raw stream has no header to state them, and the JPEGs already do.
    Assert.That(streams[0].Width, Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void TheSameCodecDecodesBothContainers() {
    // The point of splitting demuxing from decoding: an MJPG AVI and a raw .mjpg reach one decoder,
    // and neither container knows what a JPEG is.
    var jpeg = AviReaderTests._Jpeg(4);
    var throughRawStream = _Frames(jpeg)[0];

    var direct = JpegFile.ToRawImage(JpegReader.FromBytes(jpeg));
    Assert.That(throughRawStream.ToRgb24(), Is.EqualTo(direct.ToRgb24()));
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
    _Packets(stream); // once to warm the code path up
    var started = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < 5; ++i)
      _Packets(stream);

    return started.Elapsed / 5;
  }

  private static byte[] _Repeat(byte[] frame, int count) {
    var result = new byte[frame.Length * count];
    for (var i = 0; i < count; ++i)
      frame.CopyTo(result, i * frame.Length);

    return result;
  }

  private static List<CodedPacket> _Packets(byte[] stream)
    => MjpegContainer.ReadPackets(MjpegReader.FromBytes(stream)).ToList();

  private static List<RawImage> _Frames(byte[] stream) {
    var container = MjpegReader.FromBytes(stream);
    var info = MjpegContainer.Streams(container)[0];
    return VideoIO.Decode(MjpegContainer.ReadPackets(container), info, VideoFormatRegistry.CreateDecoder)
      .Select(f => f.Image)
      .ToList();
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

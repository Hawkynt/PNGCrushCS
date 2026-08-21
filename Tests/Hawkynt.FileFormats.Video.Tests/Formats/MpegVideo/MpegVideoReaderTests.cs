using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Codecs.Mpeg.Tests;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.MpegVideo.Tests;

/// <summary>
/// The MPEG-1 elementary stream container: where it cuts the packets, and what it refuses.
/// </summary>
/// <remarks>
/// The interesting question for this container is not "does it find the pictures" but "where does it
/// put the headers". A sequence header describes the pictures after it, so it belongs to the packet
/// of the picture it introduces and not to the one before — a decoder handed the picture without it
/// has no size to decode at. That is the whole of what these tests are about.
/// </remarks>
[TestFixture]
public sealed class MpegVideoReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MpegVideoReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void DataThatDoesNotOpenWithASequenceHeaderIsRefused() {
    // A picture start code is a perfectly good MPEG-1 start code and still not a stream's beginning:
    // the picture's size and quantiser matrices were stated in a sequence header that is not here.
    var failure = Assert.Throws<InvalidDataException>(
      () => MpegVideoReader.FromBytes([0x00, 0x00, 0x01, 0x00, 0x00, 0x00]));

    Assert.That(failure!.Message, Does.Contain("00 00 01 B3"));
  }

  [Test]
  [Category("Unit")]
  public void EachPictureIsOnePacket()
    => Assert.That(_Packets(_Stream(pictures: 4)).Count, Is.EqualTo(4));

  [Test]
  [Category("Unit")]
  public void EveryPacketBelongsToTheOneStreamTheFileDeclares()
    => Assert.That(_Packets(_Stream(pictures: 3)).All(packet => packet.StreamIndex == 0), Is.True);

  [Test]
  [Category("Unit")]
  public void ThePacketsAreNumberedInCodedOrderAndCarryNoPresentationTime() {
    // No presentation timestamp on purpose. An elementary stream has no time base, and the order the
    // pictures are coded in is not the order they are shown in — so a demuxer that put the coded
    // position in the presentation field would be stating something false about every B picture.
    var packets = _Packets(_Stream(pictures: 3));

    Assert.That(packets.Select(packet => packet.DecodeTimestamp).ToArray(), Is.EqualTo(new long?[] { 0, 1, 2 }));
    Assert.That(packets.All(packet => packet.PresentationTimestamp == null), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void APacketBeginsAtTheSequenceHeaderThatIntroducesItsPicture() {
    var packets = _Packets(_Stream(pictures: 2));

    Assert.That(_At(packets[0], 3), Is.EqualTo(0xB3), "the first packet opens at the sequence header");
    Assert.That(_At(packets[1], 3), Is.EqualTo(0x00), "the second opens at its own picture, with no header of its own");
  }

  [Test]
  [Category("Unit")]
  public void AGroupHeaderTravelsWithThePictureAfterIt() {
    var stream = new MpegTestStream().SequenceHeader(16, 16).GroupOfPictures();
    _Picture(stream, 1);
    stream.GroupOfPictures();
    _Picture(stream, 1);

    var packets = _Packets(stream.End());

    Assert.That(packets.Count, Is.EqualTo(2));
    Assert.That(_At(packets[1], 3), Is.EqualTo(0xB8), "the second packet opens at the group header, not at the picture");
  }

  [Test]
  [Category("Unit")]
  public void OnlyAPacketCarryingASequenceHeaderIsAPointDecodingMayBeginAt() {
    var stream = new MpegTestStream().SequenceHeader(16, 16).GroupOfPictures();
    _Picture(stream, 1);
    _Picture(stream, 2);
    stream.SequenceHeader(16, 16).GroupOfPictures();
    _Picture(stream, 1);

    Assert.That(_Packets(stream.End()).Select(packet => packet.IsKeyFrame).ToArray(),
      Is.EqualTo(new[] { true, false, true }));
  }

  [Test]
  [Category("Unit")]
  public void ThePacketsCoverTheStreamWithoutOverlapOrGap() {
    var stream = _Stream(pictures: 3);
    var packets = _Packets(stream);
    var covered = packets.Sum(packet => packet.Data.Length);

    // Everything but the leading nothing and the trailing sequence end code, which belong to no
    // picture. Reassembling the packets in order must give back exactly that span of the file.
    var joined = packets.SelectMany(packet => packet.Data.ToArray()).ToArray();
    Assert.That(joined, Is.EqualTo(stream.Take(covered).ToArray()));
    Assert.That(stream.Length - covered, Is.EqualTo(4), "the sequence end code is in no packet");
  }

  [Test]
  [Category("Unit")]
  public void ZeroBytePaddingBeforeAStartCodeStaysWithThePictureBeforeIt() {
    // Encoders pad with zero bytes to reach a byte boundary or a target rate. A start code found at
    // 00 00 00 01 begins at the last two zeroes, so the padding stays where it was written.
    var stream = _Stream(pictures: 2).ToList();
    var second = _Packets(stream.ToArray())[1];
    var offset = stream.Count - second.Data.Length - 4;
    stream.InsertRange(offset, new byte[] { 0x00, 0x00, 0x00 });

    var packets = _Packets(stream.ToArray());

    Assert.That(packets.Count, Is.EqualTo(2));
    Assert.That(packets[1].Data.Length, Is.EqualTo(second.Data.Length));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatStopsWithoutASequenceEndCodeStillYieldsItsLastPicture() {
    var stream = new MpegTestStream().SequenceHeader(16, 16).GroupOfPictures();
    _Picture(stream, 1);
    _Picture(stream, 1);

    Assert.That(_Packets(stream.ToArray()).Count, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoPictureAtAllYieldsNoPackets()
    => Assert.That(_Packets(new MpegTestStream().SequenceHeader(16, 16).GroupOfPictures().End()), Is.Empty);

  [Test]
  [Category("Unit")]
  public void TheOneStreamIsVideoAndNamesTheCodecWithoutReadingAnyHeader() {
    var streams = MpegVideoContainer.Streams(MpegVideoReader.FromBytes(_Stream(pictures: 1)));

    Assert.That(streams.Count, Is.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(streams[0].Codec, Is.EqualTo(CodecTag.FromCharacters("MPG1")));

    // Deliberately nothing else. The picture size is in the sequence header, which is the decoder's
    // to read; a demuxer's copy of it would be a second place for the same field to be read.
    Assert.That(streams[0].Width, Is.Zero);
    Assert.That(streams[0].Height, Is.Zero);
    Assert.That(streams[0].DeclaredFrameCount, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void AskingForAStreamTheFileDoesNotHaveWalksNothing()
    => Assert.That(MpegVideoContainer.ReadPackets(MpegVideoReader.FromBytes(_Stream(pictures: 2)), 1), Is.Empty);

  [Test]
  [Category("Unit")]
  public void TheContainerIsDetectedByItsSignature()
    => Assert.That(VideoFormatRegistry.Detect(_Stream(pictures: 1)), Is.EqualTo(VideoFormat.MpegVideo));

  [Test]
  [Category("Unit")]
  public void TheContainerAndTheCodecAreBothRegistered() {
    Assert.That(VideoFormatRegistry.AllFormats.Select(entry => entry.Format), Does.Contain(VideoFormat.MpegVideo));
    Assert.That(VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName),
      Does.Contain("MPEG-1 video (ISO/IEC 11172-2)"));
    Assert.That(VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName),
      Does.Contain("MPEG-2 video (ISO/IEC 13818-2)"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithASequenceExtensionIsNamedMpeg2AndOneWithoutIsNamedMpeg1() {
    // The two standards' files open with the same four bytes, so the code the demuxer states can only
    // come from the start code after the sequence header. Naming both MPEG-1 would still decode —
    // one engine reads both — and would report the wrong codec for every .m2v in existence.
    var mpeg1 = MpegVideoContainer.Streams(MpegVideoReader.FromBytes(_Stream(pictures: 1)))[0];
    var mpeg2 = MpegVideoContainer.Streams(MpegVideoReader.FromBytes(_Mpeg2Stream()))[0];

    Assert.That(mpeg1.Codec.ToString(), Is.EqualTo("MPG1"));
    Assert.That(mpeg2.Codec.ToString(), Is.EqualTo("MPG2"));
  }

  [Test]
  [Category("Unit")]
  public void ASequenceDisplayExtensionDoesNotMakeAStreamMpeg2() {
    // Extension start codes are not all sequence extensions. Counting them rather than reading the
    // four-bit identifier would call an MPEG-1 stream carrying any extension at all MPEG-2.
    var stream = new MpegTestStream().SequenceHeader(16, 16).Extension(2).Bits(0, 32).GroupOfPictures();
    _Picture(stream, 1);

    Assert.That(MpegVideoContainer.Streams(MpegVideoReader.FromBytes(stream.End()))[0].Codec.ToString(),
      Is.EqualTo("MPG1"));
  }

  [Test]
  [Category("Unit")]
  public void AnMpeg2ElementaryStreamDecodesThroughTheRegistry() {
    var frames = VideoFormatRegistry.DecodeFrames(_Mpeg2Stream()).ToList();

    Assert.That(frames.Count, Is.EqualTo(1));
    Assert.That(frames[0].Image, Is.Not.Null);
    Assert.That(frames[0].Image.Width, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryDecodesTheStreamEndToEnd() {
    var frames = VideoFormatRegistry.DecodeFrames(_Stream(pictures: 3)).ToList();

    Assert.That(frames.Count, Is.EqualTo(3));
    Assert.That(frames.All(frame => frame.Image is { Width: 16, Height: 16 }), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void TheExtensionsTheContainerClaimsReachIt() {
    foreach (var extension in new[] { ".m1v", ".m2v", ".mpv", ".mpeg1video", ".mpeg2video" })
      Assert.That(VideoFormatRegistry.ByExtension(extension), Does.Contain(VideoFormat.MpegVideo), extension);
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  /// <summary>One flat sixteen-by-sixteen MPEG-2 intra picture, sequence extension and all.</summary>
  private static byte[] _Mpeg2Stream() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).SequenceExtension()
      .PictureHeader(1).PictureCodingExtension().SliceHeader(0, 1)
      .Code("1").Code("1");

    stream.IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0);
    stream.IntraBlock(false, 0).IntraBlock(false, 0);
    return stream.End();
  }

  /// <summary>A stream of flat sixteen-by-sixteen pictures: one intra and then predicted ones.</summary>
  private static byte[] _Stream(int pictures) {
    var stream = new MpegTestStream().SequenceHeader(16, 16).GroupOfPictures();
    for (var index = 0; index < pictures; ++index)
      _Picture(stream, index == 0 ? 1 : 2);

    return stream.End();
  }

  private static void _Picture(MpegTestStream stream, int codingType) {
    stream.PictureHeader(codingType).SliceHeader(0, 1).Code("1");

    if (codingType == 1)
      stream
        .Code("1")
        .IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0)
        .IntraBlock(false, 0).IntraBlock(false, 0);
    else
      stream.Code("001").Code("1").Code("1"); // motion forward, vector (0, 0), nothing coded
  }

  private static List<CodedPacket> _Packets(byte[] stream)
    => MpegVideoContainer.ReadPackets(MpegVideoReader.FromBytes(stream)).ToList();

  private static byte _At(CodedPacket packet, int offset) => packet.Data.Span[offset];
}

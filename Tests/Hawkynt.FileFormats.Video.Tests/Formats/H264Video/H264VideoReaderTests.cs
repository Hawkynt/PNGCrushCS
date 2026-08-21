using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs.H264.Tests;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.H264Video.Tests;

/// <summary>
/// The raw H.264 byte stream container: where it says access units begin and end.
/// </summary>
[TestFixture]
public sealed class H264VideoReaderTests {

  /// <summary>An intra picture and <paramref name="predicted"/> pictures of one skipped macroblock each.</summary>
  private static byte[] _Stream(int predicted, int slicesPerPicture = 1) {
    var builder = new H264TestStream()
      .SequenceParameterSet(widthInMbs: slicesPerPicture)
      .PictureParameterSet();

    builder.BeginIdrSliceHeader();
    for (var macroblock = 0; macroblock < slicesPerPicture; ++macroblock)
      builder.FlatIntra16x16Macroblock();

    builder.EndNal(5, 3);

    for (var picture = 1; picture <= predicted; ++picture) {
      builder.BeginSliceHeader(frameNum: picture);
      builder.Unsigned(slicesPerPicture); // mb_skip_run over the whole picture
      builder.EndNal(1, 2);
    }

    return builder.ToArray();
  }

  private static CodedPacket[] _Packets(byte[] stream)
    => [.. H264VideoContainer.ReadPackets(H264VideoReader.FromBytes(stream))];

  // ----------------------------------------------------------------------------------------------
  // Signature
  // ----------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void AByteStreamOpeningWithAParameterSetIsClaimed()
    => Assert.That(H264VideoContainer.MatchesSignature(_Stream(0)), Is.True);

  [Test]
  [Category("Unit")]
  public void AThreeByteStartCodeIsClaimedAsWellAsAFourByteOne() {
    // Both lengths are start codes (Annex B); the four-byte one is the three-byte one with a leading
    // zero, which encoders use for the first unit of an access unit.
    Assert.That(H264VideoContainer.MatchesSignature([0x00, 0x00, 0x01, 0x67, 0x42]), Is.True);
    Assert.That(H264VideoContainer.MatchesSignature([0x00, 0x00, 0x00, 0x01, 0x67, 0x42]), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AnMpegElementaryStreamIsNotClaimedDespiteSharingTheStartCodePrefix() {
    // 00 00 01 opens every MPEG elementary stream. What tells them apart is the byte after it: an
    // MPEG-1 sequence header is B3 and an MPEG-4 part 2 visual object sequence is B0, and both have
    // the top bit that an H.264 NAL unit header is forbidden to set.
    Assert.That(H264VideoContainer.MatchesSignature([0x00, 0x00, 0x01, 0xB3, 0x00]), Is.Null);
    Assert.That(H264VideoContainer.MatchesSignature([0x00, 0x00, 0x01, 0xB0, 0x00]), Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void AUnitAStreamCannotBeEnteredAtIsNotClaimed() {
    // A non-IDR slice with no parameter set before it is not the start of a decodable stream, so a
    // file that opens with one is a fragment rather than a byte stream.
    Assert.That(H264VideoContainer.MatchesSignature([0x00, 0x00, 0x01, 0x41, 0x9A]), Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void DataWithNoStartCodeIsRefused()
    => Assert.That(() => H264VideoReader.FromBytes([1, 2, 3, 4, 5, 6, 7, 8]),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("start code"));

  // ----------------------------------------------------------------------------------------------
  // Packets
  // ----------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void EachAccessUnitIsOnePacket() {
    var packets = _Packets(_Stream(predicted: 4));

    Assert.That(packets, Has.Length.EqualTo(5));
    Assert.That(packets.Select(p => p.StreamIndex), Is.All.EqualTo(0));
    Assert.That(packets.Select(p => p.DecodeTimestamp), Is.EqualTo(new long?[] { 0, 1, 2, 3, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void TheParameterSetsBelongToThePictureTheyIntroduce() {
    // A decoder handed a picture without the sequence parameter set that introduced it has no picture
    // size to decode it at, so the leading units go into the packet after them and not before.
    var packets = _Packets(_Stream(predicted: 1));

    Assert.That(packets[0].IsKeyFrame, Is.True);
    Assert.That(packets[1].IsKeyFrame, Is.False);

    // Every byte of the stream is in exactly one packet, and the first packet opens at the very start.
    var stream = _Stream(predicted: 1);
    Assert.That(packets.Sum(p => p.Data.Length), Is.EqualTo(stream.Length));
  }

  [Test]
  [Category("Unit")]
  public void SeveralSlicesOfOnePictureStayInOnePacket() {
    // A picture may be coded as several slices, and only the one starting at macroblock zero opens a
    // packet. Cutting at every slice would hand a decoder a third of a picture and call it a frame.
    var builder = new H264TestStream()
      .SequenceParameterSet(widthInMbs: 2, heightInMbs: 1)
      .PictureParameterSet();

    builder.BeginIdrSliceHeader().FlatIntra16x16Macroblock().EndNal(5, 3);

    // A second slice of the same picture, beginning at macroblock one.
    builder.Unsigned(1) // first_mb_in_slice
      .Unsigned(7) // slice_type: I
      .Unsigned(0) // pic_parameter_set_id
      .Bits(0, 4) // frame_num
      .Unsigned(0) // idr_pic_id
      .Bits(0, 1)
      .Bits(0, 1)
      .Signed(0) // slice_qp_delta
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3);

    Assert.That(_Packets(builder.ToArray()), Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void APacketIsAWindowOntoTheStreamRatherThanACopy() {
    var stream = _Stream(predicted: 2);
    var packets = _Packets(stream);

    // Every packet's bytes are the stream's own, in order and without gaps.
    var at = 0;
    foreach (var packet in packets) {
      Assert.That(packet.Data.ToArray(), Is.EqualTo(stream[at..(at + packet.Data.Length)]));
      at += packet.Data.Length;
    }

    Assert.That(at, Is.EqualTo(stream.Length));
  }

  // ----------------------------------------------------------------------------------------------
  // The stream it declares
  // ----------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void ItDeclaresOneVideoStreamAndSaysNothingElseAboutIt() {
    var streams = H264VideoContainer.Streams(H264VideoReader.FromBytes(_Stream(0)));

    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(streams[0].Codec.ToString(), Is.EqualTo("avc1"));

    // The picture size, the frame rate and the codec configuration are all inside the stream, which
    // is the decoder's to read. A demuxer stating them here would be the same parse done twice.
    Assert.That(streams[0].Width, Is.Zero);
    Assert.That(streams[0].Height, Is.Zero);
    Assert.That(streams[0].CodecPrivateData.IsEmpty, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AnythingButTheOneStreamWalksNothing()
    => Assert.That(H264VideoContainer.ReadPackets(H264VideoReader.FromBytes(_Stream(1)), 1), Is.Empty);

  [Test]
  [Category("Unit")]
  public void ItClaimsTheExtensionsARawStreamIsWrittenUnder() {
    Assert.That(H264VideoContainer.PrimaryExtension, Is.EqualTo(".264"));
    Assert.That(H264VideoContainer.FileExtensions, Does.Contain(".h264"));
    Assert.That(H264VideoContainer.FileExtensions, Does.Contain(".avc"));
  }

  // ----------------------------------------------------------------------------------------------
  // Through the registry, which is how a caller reaches it
  // ----------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheRegistryDetectsDemuxesAndDecodesAByteStream() {
    var frames = VideoFormatRegistry.DecodeFrames(_Stream(predicted: 2)).ToList();

    Assert.That(frames, Has.Count.EqualTo(3));
    Assert.That(frames[0].Image.Width, Is.EqualTo(16));
    foreach (var frame in frames)
      Assert.That(frame.Image.PixelData, Is.EqualTo(frames[0].Image.PixelData));
  }
}

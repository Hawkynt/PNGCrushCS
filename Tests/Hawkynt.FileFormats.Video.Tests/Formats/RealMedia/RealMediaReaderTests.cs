using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.RealMedia.Tests;

/// <summary>
/// The RealMedia reader's behaviour.
/// </summary>
/// <remarks>
/// Everything asserted here was first measured against ffprobe on real recordings. Ten files were
/// compared picture for picture — RealVideo 1, 2, 3 and 4, from 91 kilobytes to 18 megabytes, three
/// hundred and sixty thousand coded pictures between them — and every file produced the same number
/// of pictures with the same byte lengths, the same timestamps and the same key-frame flags that
/// ffmpeg's own demuxer reports.
/// <para/>
/// The tests that build a file rather than describing one are the shapes no encoder still made will
/// produce: a picture whose pieces arrive with one sent twice, a picture completing without ever
/// being marked complete, a data chunk whose length was never filled in, a stream number that is not
/// its index. Each of those is a branch the ordinary files never reach, and each of them is where a
/// reader that looks right goes wrong.
/// <para/>
/// The reader is a demuxer and nothing else, so what is tested is packets: how many, in what order,
/// how long, and when each is due.
/// </remarks>
[TestFixture]
public sealed class RealMediaReaderTests {

  // ------------------------------------------------------------------------------------------
  // Opening
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => RealMediaReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => RealMediaReader.FromBytes(new byte[8]));

  [Test]
  [Category("Unit")]
  public void WithoutTheSignature_IsRefused() {
    var file = _Simple();
    file[1] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => RealMediaReader.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void WithoutADataChunk_IsRefusedByName() {
    // A file of headers alone declares streams and no packets at all. There is nothing to walk, and
    // reporting an empty walk would make a file that lost its packets look like a file that had none.
    var file = RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      []);

    var truncated = file[..^18];
    var failure = Assert.Throws<InvalidDataException>(() => RealMediaReader.FromBytes(truncated));
    Assert.That(failure!.Message, Does.Contain("DATA"));
  }

  [Test]
  [Category("Unit")]
  public void TheSignatureIsClaimedOnlyByThisFormat() {
    Assert.That(RealMediaContainer.MatchesSignature("...RMF"u8), Is.Null);
    Assert.That(RealMediaContainer.MatchesSignature(".RMF"u8), Is.True);
    Assert.That(RealMediaContainer.MatchesSignature("RIFF"u8), Is.Null);
  }

  // ------------------------------------------------------------------------------------------
  // Streams
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void AVideoStream_IsDescribedFromItsMediaProperties() {
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 320, 240, codecPrivateData: [1, 2, 3, 4])],
      [new(1, 0, RealMediaTestContainer.WholeFrame([9, 9, 9]), IsKeyFrame: true)]));

    var stream = RealMediaContainer.Streams(container)[0];
    Assert.Multiple(() => {
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.Codec.ToString(), Is.EqualTo("RV20"));
      Assert.That(stream.Width, Is.EqualTo(320));
      Assert.That(stream.Height, Is.EqualTo(240));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 1000)));

      // The bytes past the fixed fields are the codec's own and are handed across untouched. For
      // RealVideo they carry the bitstream version, which no field of the container states.
      Assert.That(stream.CodecPrivateData.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
    });
  }

  [TestCase(4, "dnet")]
  [TestCase(4, "sipr")]
  [TestCase(5, "cook")]
  [TestCase(5, "raac")]
  [Category("Unit")]
  public void ASoundStream_IsNamedByTheCodeInItsRealAudioHeader(int version, string code) {
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.AudioStream(0, code, version)],
      [new(0, 0, [1, 2, 3, 4])]));

    var stream = RealMediaContainer.Streams(container)[0];
    Assert.Multiple(() => {
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Audio));
      Assert.That(stream.Codec.ToString(), Is.EqualTo(code));

      // The whole header, because the rest of it is the sound codec's and there is nowhere in a model
      // that describes pictures to put a sample rate.
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(version == 4 ? 73 : 86));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheChunkThatDescribesTheFile_IsNotAStream() {
    // It carries no packets and never has. Reporting it as a stream would put an entry in the list
    // that no packet ever belongs to, and would number the real streams differently from every other
    // tool — ffprobe reports two streams for every sample file that has one of these.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [
        RealMediaTestContainer.AudioStream(0, "cook"),
        RealMediaTestContainer.VideoStream(1, "RV30", 64, 48),
        RealMediaTestContainer.FileInfoStream(2, ("Generated By", "A Producer"), ("Keywords", "one two")),
      ],
      [new(1, 0, RealMediaTestContainer.WholeFrame([1, 2, 3]))]));

    var streams = RealMediaContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Audio));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Video));

    var metadata = RealMediaContainer.Metadata(container);
    Assert.That(metadata.EncodedBy, Is.EqualTo("A Producer"));
    Assert.That(metadata.TextEntries.Select(e => e.Keyword), Does.Contain("Keywords"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamNumberIsNotAStreamIndex() {
    // RealMedia numbers its streams itself and a file may leave gaps. A packet states the number
    // where the model states the index, and publishing one for the other would report packets under
    // a stream that is not theirs.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [
        RealMediaTestContainer.AudioStream(4, "cook"),
        RealMediaTestContainer.VideoStream(7, "RV20", 64, 48),
      ],
      [
        new(7, 0, RealMediaTestContainer.WholeFrame([1, 2, 3])),
        new(4, 0, [4, 5, 6]),
      ]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 1, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void APacketForAStreamNothingDeclared_IsSkipped() {
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [
        new(1, 0, RealMediaTestContainer.WholeFrame([1, 2, 3])),
        new(9, 10, [7, 7, 7]),
      ]));

    Assert.That(RealMediaContainer.ReadPackets(container).Count(), Is.EqualTo(1));
  }

  // ------------------------------------------------------------------------------------------
  // Reassembly
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void APictureSplitAcrossPackets_ComesBackWhole() {
    var first = new byte[] { 1, 2, 3, 4, 5 };
    var second = new byte[] { 6, 7, 8 };

    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [
        new(1, 100, RealMediaTestContainer.Piece(first, 8, 0, 1), IsKeyFrame: true),
        new(1, 100, RealMediaTestContainer.LastPiece(second, 8, 2), IsKeyFrame: true),
      ]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(100));
    Assert.That(packets[0].IsKeyFrame, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void APictureCompletingWithoutALastPieceMarker_IsStillHandedOut() {
    // The marker is a hint and not the definition. Plenty of real pictures arrive complete without
    // one, and a reader that waited for it loses every such picture and then every picture after it:
    // this is the bug that cost twenty frames of a fifteen-hundred-frame file before it was found.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [
        new(1, 0, RealMediaTestContainer.Piece([1, 2, 3], 6, 0, 1)),
        new(1, 0, RealMediaTestContainer.Piece([4, 5, 6], 6, 3, 2)),
      ]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void APieceSentTwice_DoesNotCostThePicture() {
    // A format built for streaming re-sends. Treating the repeat as a break in the sequence throws
    // away a picture whose bytes are all present — and every piece still to come, which then has
    // nothing to attach to.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [
        new(1, 0, RealMediaTestContainer.Piece([1, 2, 3], 9, 0, 1)),
        new(1, 0, RealMediaTestContainer.Piece([4, 5, 6], 9, 3, 2)),
        new(1, 0, RealMediaTestContainer.Piece([4, 5, 6], 9, 3, 2)),
        new(1, 0, RealMediaTestContainer.LastPiece([7, 8, 9], 9, 3)),
      ]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
  }

  [Test]
  [Category("Unit")]
  public void TheOffsetsOfThePiecesAPictureArrivedIn_AreReported() {
    // RealMedia cuts a picture at its slices, one slice to a piece. Those offsets are not in the
    // picture's bytes and are unrecoverable once the pieces are joined, so they go out on the packet:
    // a RealVideo slice carries no start code and the padding between slices is not fixed, which
    // makes this the only record of where they are.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [
        new(1, 0, RealMediaTestContainer.Piece([1, 2, 3, 4], 9, 0, 1)),
        new(1, 0, RealMediaTestContainer.Piece([5, 6], 9, 4, 2)),
        new(1, 0, RealMediaTestContainer.LastPiece([7, 8, 9], 9, 3)),
      ]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
    Assert.That(packets[0].Fragments, Is.EqualTo(new[] { 0, 4, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void APictureThatArrivedWhole_IsOnePieceAtNought() {
    var container = RealMediaReader.FromBytes(_Simple());

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets[0].FragmentOffsets, Is.Null);
    Assert.That(packets[0].Fragments, Is.EqualTo(new[] { 0 }));
  }

  [Test]
  [Category("Unit")]
  public void APieceSentTwice_IsNotReportedAsAPieceOfItsOwn() {
    // The repeat carries no slice the picture did not already have. Reporting it would tell a decoder
    // there is a slice boundary in the middle of a slice.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [
        new(1, 0, RealMediaTestContainer.Piece([1, 2, 3], 9, 0, 1)),
        new(1, 0, RealMediaTestContainer.Piece([4, 5, 6], 9, 3, 2)),
        new(1, 0, RealMediaTestContainer.Piece([4, 5, 6], 9, 3, 2)),
        new(1, 0, RealMediaTestContainer.LastPiece([7, 8, 9], 9, 3)),
      ]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets[0].Fragments, Is.EqualTo(new[] { 0, 3, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void APictureWithAPieceMissing_IsNotHandedOut() {
    // Half a picture is not a picture, and a picture made of whichever pieces arrived is worse than
    // none: it looks like a picture, so nobody checks it.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [
        new(1, 0, RealMediaTestContainer.Piece([1, 2, 3], 9, 0, 1)),
        new(1, 0, RealMediaTestContainer.LastPiece([7, 8, 9], 9, 3)),
      ]));

    Assert.That(RealMediaContainer.ReadPackets(container), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void APieceOfAPictureNothingSawTheStartOf_IsSkipped() {
    // Which is what every stream looks like when reading begins in the middle of a file.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [
        new(1, 0, RealMediaTestContainer.LastPiece([7, 8, 9], 9, 3)),
        new(1, 20, RealMediaTestContainer.WholeFrame([1, 2, 3])),
      ]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
    Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void APictureLongerThanTheWholeDataChunk_IsRefusedRatherThanAllocated() {
    // The length is four bytes of the file like any other. Believing one that claims two gigabytes
    // would allocate two gigabytes for a picture whose remaining pieces do not exist.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [new(1, 0, RealMediaTestContainer.Piece([1, 2, 3], 0x3FFFFF, 0, 1))]));

    Assert.That(RealMediaContainer.ReadPackets(container), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void APictureOverSixteenKilobytes_UsesTheLongerFormOfTheLengthFields() {
    // The two numbers a piece carries are written in a form that spends two bytes on values that fit
    // in fourteen bits and four on values that do not. Reading a long one as though it were short
    // leaves every field after it off by two bytes.
    var first = new byte[20000];
    var second = new byte[500];
    for (var i = 0; i < first.Length; ++i)
      first[i] = (byte)i;

    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 640, 480)],
      [
        new(1, 0, RealMediaTestContainer.Piece(first, 20500, 0, 1)),
        new(1, 0, RealMediaTestContainer.LastPiece(second, 20500, 2)),
      ]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
    Assert.That(packets[0].Data.Length, Is.EqualTo(20500));
    Assert.That(packets[0].Data.Span[19999], Is.EqualTo(first[19999]));
  }

  // ------------------------------------------------------------------------------------------
  // Timestamps
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void OnlyTheFirstPictureOfAPacket_TakesThePacketsTimestamp() {
    // There is one timestamp in a packet header. A picture that begins part way through a packet is
    // one the container stated no time for, and saying so is the honest answer — ffmpeg fills the gap
    // by adding a frame's worth of time to the picture before, which is a good guess and is not what
    // the file says.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV10", 64, 48)],
      [
        new(1, 500, RealMediaTestContainer.Elements(
          RealMediaTestContainer.PackedFrame([1, 2, 3]),
          RealMediaTestContainer.PackedFrame([4, 5, 6])), IsKeyFrame: true),
      ]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(2));
    Assert.Multiple(() => {
      Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(500));
      Assert.That(packets[0].IsKeyFrame, Is.True);
      Assert.That(packets[1].PresentationTimestamp, Is.Null);
      Assert.That(packets[1].IsKeyFrame, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void APacketFinishingOnePictureAndCarryingAnother_YieldsBoth() {
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV10", 64, 48)],
      [
        new(1, 0, RealMediaTestContainer.Piece([1, 2, 3], 6, 0, 1), IsKeyFrame: true),
        new(1, 0, RealMediaTestContainer.Elements(
          RealMediaTestContainer.LastPiece([4, 5, 6], 6, 2),
          RealMediaTestContainer.PackedFrame([7, 8])), IsKeyFrame: true),
      ]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(2));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
    Assert.That(packets[1].Data.ToArray(), Is.EqualTo(new byte[] { 7, 8 }));

    // The completed picture claims the timestamp because its piece opens the packet; the one after it
    // does not, exactly as it would not in a packet holding two whole pictures.
    Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(0));
    Assert.That(packets[1].PresentationTimestamp, Is.Null);
  }

  [TestCase(0)]
  [TestCase(1)]
  [Category("Unit")]
  public void BothPacketHeaderVersions_AreRead(int version) {
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [new(1, 1234, RealMediaTestContainer.WholeFrame([1, 2, 3]), IsKeyFrame: true, Version: version)]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
    Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(1234));
    Assert.That(packets[0].IsKeyFrame, Is.True);
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
  }

  // ------------------------------------------------------------------------------------------
  // Damaged files
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void ADataChunkWhoseLengthWasNeverFilledIn_IsReadToTheEndOfTheFile() {
    // A writer fills the length in when it closes the file, so a recording that was never closed
    // carries a zero there. One of the sample recordings does exactly this, and ffmpeg reads it.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [
        new(1, 0, RealMediaTestContainer.WholeFrame([1, 2, 3])),
        new(1, 40, RealMediaTestContainer.WholeFrame([4, 5, 6])),
      ],
      dataLength: 0));

    Assert.That(RealMediaContainer.ReadPackets(container).Count(), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AFileCutOffMidPacket_KeepsThePicturesThatWereWholeAndDropsTheOneThatWasNot() {
    var file = RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [
        new(1, 0, RealMediaTestContainer.WholeFrame([1, 2, 3])),
        new(1, 40, RealMediaTestContainer.WholeFrame([4, 5, 6, 7, 8, 9])),
      ]);

    // A whole picture's length is the packet's rather than its own, so what survives a cut cannot be
    // known to be all of it.
    var container = RealMediaReader.FromBytes(file[..^3]);

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheContentDescription_BecomesTheMetadata() {
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [new(1, 0, RealMediaTestContainer.WholeFrame([1, 2, 3]))],
      title: "Welcome", author: "RealNetworks", copyright: "1997", comment: "an encoder's notes",
      durationMilliseconds: 6320));

    var metadata = RealMediaContainer.Metadata(container);
    Assert.Multiple(() => {
      Assert.That(metadata.Title, Is.EqualTo("Welcome"));
      Assert.That(metadata.Artist, Is.EqualTo("RealNetworks"));
      Assert.That(metadata.Duration, Is.EqualTo(TimeSpan.FromMilliseconds(6320)));
      Assert.That(metadata.Streams, Has.Count.EqualTo(1));

      // The copyright and the comment have no field of their own in the model, so they keep the names
      // the format gives them rather than being folded into one that nearly fits.
      Assert.That(metadata.TextEntries.Select(e => e.Keyword), Is.EquivalentTo(new[] { "Comment", "Copyright" }));
      Assert.That(metadata.TextEntries.Single(e => e.Keyword == "Copyright").Text, Is.EqualTo("1997"));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyContentField_IsAbsentRatherThanEmpty() {
    var container = RealMediaReader.FromBytes(_Simple());

    var metadata = RealMediaContainer.Metadata(container);
    Assert.That(metadata.Title, Is.Null);
    Assert.That(metadata.Artist, Is.Null);
    Assert.That(metadata.TextEntries, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void TheFrameRate_IsReportedAsTheFileStatesIt() {
    // 0x001DF852 is 982057/32768, which is 29.97000 — and not 30000/1001, however much that is the
    // rate that was meant. Reporting the tidier one would report a rate the file never claimed.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 320, 240, frameRateFixedPoint: 0x001DF852)],
      [new(1, 0, RealMediaTestContainer.WholeFrame([1, 2, 3]))]));

    var rate = RealMediaContainer.Streams(container)[0].FrameRate;
    Assert.That(rate, Is.EqualTo(new Rational(982057, 32768)));
    Assert.That(rate.ToDouble(), Is.EqualTo(29.97).Within(0.001));
  }

  // ------------------------------------------------------------------------------------------
  // Demux without decode
  // ------------------------------------------------------------------------------------------

  [TestCase("RV30")]
  [TestCase("RV40")]
  [Category("Unit")]
  public void ACodecNothingHereDecodes_StillDemuxes(string code) {
    // The refusal is the codec's and not the container's. A file nothing here decodes still comes
    // apart into the packets a remux would move, which is the whole point of keeping the two apart.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, code, 352, 240)],
      [
        new(1, 0, RealMediaTestContainer.WholeFrame([1, 2, 3]), IsKeyFrame: true),
        new(1, 33, RealMediaTestContainer.WholeFrame([4, 5, 6])),
      ]));

    var streams = RealMediaContainer.Streams(container);
    Assert.That(streams[0].Codec.ToString(), Is.EqualTo(code));
    Assert.That(RealMediaContainer.ReadPackets(container).Count(), Is.EqualTo(2));
    Assert.That(VideoFormatRegistry.CanDecode(streams[0]), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheWalkIsLazyAndRepeatable() {
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [
        RealMediaTestContainer.AudioStream(0, "cook"),
        RealMediaTestContainer.VideoStream(1, "RV20", 64, 48),
      ],
      [
        new(1, 0, RealMediaTestContainer.WholeFrame([1, 2, 3])),
        new(0, 0, [4, 5]),
        new(1, 40, RealMediaTestContainer.WholeFrame([6, 7, 8])),
      ]));

    var first = RealMediaContainer.ReadPackets(container).ToArray();
    var second = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(second.Select(p => p.StreamIndex), Is.EqualTo(first.Select(p => p.StreamIndex)));

    // The per-stream walks together account for every packet of the whole-file walk, and no more.
    var video = RealMediaContainer.ReadPackets(container, 1).ToArray();
    var audio = RealMediaContainer.ReadPackets(container, 0).ToArray();
    Assert.That(video, Has.Length.EqualTo(2));
    Assert.That(audio, Has.Length.EqualTo(1));
    Assert.That(video.Length + audio.Length, Is.EqualTo(first.Length));
  }

  [Test]
  [Category("Unit")]
  public void SoundPacketsAreHandedOutAsStored() {
    // A sound payload has no sub-header in front of it; reading one as though it did would take four
    // bytes of sound for a length. Deinterleaving is not done here either — the geometry that undoes
    // it is in the RealAudio header, which makes it the codec's business.
    var container = RealMediaReader.FromBytes(RealMediaTestContainer.Build(
      [RealMediaTestContainer.AudioStream(0, "cook")],
      [new(0, 60, [0xC0, 0x40, 0x01, 0x02, 0x03])]));

    var packets = RealMediaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(new byte[] { 0xC0, 0x40, 0x01, 0x02, 0x03 }));
    Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(60));
  }

  [Test]
  [Category("Unit")]
  public void TheFormatIsRegisteredAndDetectedBySignature() {
    var file = _Simple();

    var format = VideoFormatRegistry.Detect(file);
    Assert.That(format, Is.EqualTo(VideoFormat.RealMedia));

    var entry = VideoFormatRegistry.GetEntry(format);
    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.PrimaryExtension, Is.EqualTo(".rm"));
    Assert.That(entry.AllExtensions, Does.Contain(".rmvb"));
    Assert.That(VideoFormatRegistry.ReadStreams(file), Has.Count.EqualTo(1));
    Assert.That(VideoFormatRegistry.ByExtension(".rmvb"), Does.Contain(VideoFormat.RealMedia));
  }

  private static byte[] _Simple()
    => RealMediaTestContainer.Build(
      [RealMediaTestContainer.VideoStream(1, "RV20", 64, 48)],
      [new(1, 0, RealMediaTestContainer.WholeFrame([1, 2, 3]), IsKeyFrame: true)]);
}

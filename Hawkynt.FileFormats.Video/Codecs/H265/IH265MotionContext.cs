using System.Collections.Generic;

namespace FileFormat.Codecs.H265;

/// <summary>
/// What deriving a motion vector needs to know about the picture around the block.
/// </summary>
/// <remarks>
/// A motion vector is never sent whole. What the bitstream carries is a merge index, or a difference
/// against a predictor, and both are resolved from the neighbouring blocks and the collocated block
/// of another picture — so the vector a decoder ends up with depends on state that has nothing to do
/// with the bins it just read.
/// <para/>
/// That is why an encoder cannot compute a motion vector difference without running the same
/// derivation: it has to know what the decoder will predict before it can say what to add to it. The
/// two could each have their own copy of clause 8.5.3.2, and then a disagreement anywhere in it would
/// produce a picture that decodes cleanly to the wrong samples — a class of defect that leaves no
/// trace in the stream at all. One derivation over this interface makes that impossible: the decoder
/// answers from the picture it is building, the encoder from the one it is coding, and
/// <see cref="H265MotionPrediction"/> cannot tell them apart.
/// </remarks>
internal interface IH265MotionContext {

  /// <summary>The slice being coded, for its type, its reference counts and its merge settings.</summary>
  H265SliceHeader Header { get; }

  H265SequenceParameterSet Sps { get; }

  H265PictureParameterSet Pps { get; }

  /// <summary>The picture being built, which holds the motion field the neighbours are read from.</summary>
  H265Picture Picture { get; }

  /// <summary>The reference pictures of one list, in the order the slice header put them.</summary>
  IReadOnlyList<H265Picture> ReferenceList(int list);

  /// <summary>The picture the temporal candidate comes from, or <c>null</c> where there is none.</summary>
  H265Picture? CollocatedPicture { get; }

  /// <summary>The index of the smallest block covering a luma position.</summary>
  int BlockIndexAt(int x, int y);

  /// <summary>The motion of one block of the picture being coded.</summary>
  H265MotionInfo MotionAt(int index);

  /// <summary>Whether a block was coded without prediction from another picture.</summary>
  bool IsIntraAt(int index);

  /// <summary>
  /// Whether a position may be read from while coding the block at another — clause 6.4.1.
  /// </summary>
  /// <remarks>
  /// A position that has not been coded yet, or belongs to another slice or tile, is not available
  /// however far inside the picture it is.
  /// </remarks>
  bool IsAvailableAt(int x0, int y0, int x, int y);

  /// <summary>The coding block the prediction unit being derived belongs to.</summary>
  int CodingBlockX { get; }

  int CodingBlockY { get; }

  int CodingBlockSize { get; }

  /// <summary>How that coding block is divided into prediction units.</summary>
  H265PartitionMode CodingBlockPartitionMode { get; }
}

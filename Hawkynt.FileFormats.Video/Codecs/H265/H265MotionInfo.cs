using System;

namespace FileFormat.Codecs.H265;

/// <summary>How a coding unit's samples are produced — ITU-T H.265, clause 7.4.9.5.</summary>
internal enum H265PredictionMode {

  /// <summary>Predicted from another picture.</summary>
  Inter = 0,

  /// <summary>Predicted from this picture's own already-decoded neighbours.</summary>
  Intra = 1,
}

/// <summary>How a coding block is divided into prediction blocks — Table 7-10.</summary>
/// <remarks>
/// The four asymmetric partitions are HEVC's, and they exist because a moving object's edge rarely
/// falls halfway across a block. A quarter-and-three-quarters split lets one prediction block sit on
/// the object and the other on the background without the quadtree having to descend a level, which
/// would cost four sets of motion data instead of two.
/// </remarks>
internal enum H265PartitionMode {

  /// <summary>The whole coding block, undivided.</summary>
  Square = 0,

  /// <summary>Two halves, one above the other.</summary>
  HorizontalHalves = 1,

  /// <summary>Two halves, side by side.</summary>
  VerticalHalves = 2,

  /// <summary>Four quarters. Inter only below the smallest coding block size; intra only at it.</summary>
  Quarters = 3,

  /// <summary>A quarter above three quarters.</summary>
  HorizontalQuarterTop = 4,

  /// <summary>Three quarters above a quarter.</summary>
  HorizontalQuarterBottom = 5,

  /// <summary>A quarter left of three quarters.</summary>
  VerticalQuarterLeft = 6,

  /// <summary>Three quarters left of a quarter.</summary>
  VerticalQuarterRight = 7,
}

/// <summary>
/// The motion of one prediction block: up to two references and a vector for each.
/// </summary>
/// <remarks>
/// A mutable struct passed by reference through the derivation, because the merge and the advanced
/// motion vector prediction processes both build candidate lists of these and a list of a class
/// would allocate several of them per prediction block.
/// </remarks>
internal struct H265MotionInfo {

  internal bool PredictL0;

  internal bool PredictL1;

  internal sbyte RefIdxL0;

  internal sbyte RefIdxL1;

  internal short MvL0X;

  internal short MvL0Y;

  internal short MvL1X;

  internal short MvL1Y;

  internal static H265MotionInfo None => new() { RefIdxL0 = -1, RefIdxL1 = -1 };

  internal readonly bool Predicts(int list) => list == 0 ? this.PredictL0 : this.PredictL1;

  internal readonly sbyte RefIdx(int list) => list == 0 ? this.RefIdxL0 : this.RefIdxL1;

  internal readonly short MvX(int list) => list == 0 ? this.MvL0X : this.MvL1X;

  internal readonly short MvY(int list) => list == 0 ? this.MvL0Y : this.MvL1Y;

  internal void Set(int list, bool predict, int refIdx, int mvX, int mvY) {
    if (list == 0) {
      this.PredictL0 = predict;
      this.RefIdxL0 = (sbyte)refIdx;
      this.MvL0X = (short)mvX;
      this.MvL0Y = (short)mvY;
      return;
    }

    this.PredictL1 = predict;
    this.RefIdxL1 = (sbyte)refIdx;
    this.MvL1X = (short)mvX;
    this.MvL1Y = (short)mvY;
  }

  /// <summary>Whether two blocks were predicted identically, which is what makes a candidate redundant.</summary>
  internal readonly bool SameAs(in H265MotionInfo other)
    => this.PredictL0 == other.PredictL0
       && this.PredictL1 == other.PredictL1
       && (!this.PredictL0 || (this.RefIdxL0 == other.RefIdxL0 && this.MvL0X == other.MvL0X && this.MvL0Y == other.MvL0Y))
       && (!this.PredictL1 || (this.RefIdxL1 == other.RefIdxL1 && this.MvL1X == other.MvL1X && this.MvL1Y == other.MvL1Y));
}

using System;

namespace FileFormat.LuraWave;

/// <summary>
/// Rebuilds the wavelet coefficients of one stripe from its embedded bit-plane code — or, run the other way round,
/// produces that code for known coefficients.
/// </summary>
/// <remarks>
/// <para>
/// The coder is a set-partitioning scheme in the spirit of SPIHT. Coefficients sit in the "in-place" layout a lifting
/// transform leaves behind: at level <c>l</c> the low-pass samples are those whose coordinates are multiples of
/// <c>2^l</c>, and the details of that level sit half a step away. Each block of <c>2^L x 2^L</c> pixels and each channel
/// owns one tree whose root holds the final low-pass sample and the three level-<c>L</c> details; below it hang one
/// quadtree per orientation, every node standing for a 2x2 group of same-orientation coefficients.
/// </para>
/// <para>
/// Trees live in a fixed, pre-shaped node pool. A root block is the root followed by its three orientation quadtrees,
/// each stored depth-first, so every child's index follows arithmetically from its parent's. Nodes that matter are
/// threaded on one list, starting with all roots in stripe order; nodes join its tail the moment one of their bits
/// says they have something to code, and later passes visit them in that order.
/// </para>
/// <para>
/// Magnitudes are kept sign-and-magnitude while coding (bit 31 is the sign). A coefficient that becomes significant
/// against threshold mask <c>t</c> is set to the whole mask, which carries the reconstruction offset in its lower bits.
/// </para>
/// <para>
/// Encoding walks exactly the same traversal; wherever the decoder would read a bit, the encoder answers the question
/// that bit stands for from the true coefficients and writes the answer. The decoder's state therefore evolves
/// identically on both sides, which is what makes the written stream decode to the intended coefficients.
/// </para>
/// </remarks>
internal sealed class LuraWaveStripeCoder {

  // Node flag layout.
  private const int KindMask = 0x3;
  private const int KindLeaf = 0;      // finest level: four coefficients, no children
  private const int KindInner = 1;     // four coefficients and four child nodes
  private const int KindRoot = 2;      // low-pass sample, three details, three orientation children
  private const int KindFinished = 3;  // inner node already handled by the verbatim tail
  private const int Expanded = 0x4;
  private const int Exhausting = 0x8;
  private const int Complete = 0xC;
  private const int StateMask = 0xC;
  private const int ChildMarks = 0xF0;
  private const int ShiftOffset = 8;
  private const int ShiftMask = 31;
  private const int OrientationMask = 0x6000;
  private const int Horizontal = 0x2000;   // HL: high-pass across, low-pass down
  private const int Vertical = 0x4000;     // LH
  private const int Diagonal = 0x6000;     // HH
  private const int LevelOffset = 16;
  private const int LevelMask = 15;
  private const int Sign = int.MinValue;
  private const int End = -1;

  private readonly LuraWaveHeader _header;
  private readonly LuraWaveBitReader? _reader;
  private readonly LuraWaveBitWriter? _writer;
  private readonly int _channels;
  private readonly int _width;
  private readonly int _height;
  private readonly int _levels;
  private readonly int _rootCount;
  private readonly int[] _subtreeSize;   // nodes in a quadtree whose top sits at a given level
  private readonly int _rootBlock;
  private readonly int[] _channelWeight;
  private readonly uint _passwordCheck;
  private readonly bool _unlocked;
  private readonly int[] _minShift;

  private readonly int[] _position;
  private readonly int[] _next;
  private readonly int[] _flags;

  private int[] _coefficients = [];
  private int _tail;
  private int _passes;
  private int _publicPassLimit;
  private int _totalPassLimit;

  // Encoder only: the true coefficients (sign and magnitude), the current plane's top bit, and per node the highest
  // plane at which anything it is responsible for becomes significant.
  private int[] _source = [];
  private int _planeBit;
  private int[] _reach = [];

  /// <summary>Questions a coded bit answers; the encoder needs to know which one is being asked.</summary>
  private enum Question {
    Significant,
    Negative,
    Refinement,
    NodeOpens,
    AnyChildOpens,
    Verbatim,
    VerbatimNegative,
    PasswordCheck,
  }

  /// <summary>Prepares a decoder pulling bits from <paramref name="reader"/>.</summary>
  /// <param name="header">The stream's header.</param>
  /// <param name="reader">Source of the coded bits.</param>
  /// <param name="passwordCheck">
  /// The 32-bit check value of an accepted password, which lets decoding run past the public quality of a protected
  /// stream; <see langword="null"/> to stop at the public quality.
  /// </param>
  public LuraWaveStripeCoder(LuraWaveHeader header, LuraWaveBitReader reader, uint? passwordCheck = null) : this(header, passwordCheck) => this._reader = reader;

  /// <summary>Prepares an encoder pushing bits into <paramref name="writer"/>.</summary>
  public LuraWaveStripeCoder(LuraWaveHeader header, LuraWaveBitWriter writer) : this(header, (uint?)null) {
    if (header.PublicQuality != LuraWaveHeader.FullQuality)
      throw new ArgumentException("The encoder writes no password-limited streams.", nameof(header));
    if (!IsLosslessLowestPlane(header.LowestBitPlane, header.Levels))
      throw new ArgumentException($"Lowest bit plane {header.LowestBitPlane} cannot code {header.Levels} levels losslessly.", nameof(header));
    this._writer = writer;
  }

  private LuraWaveStripeCoder(LuraWaveHeader header, uint? passwordCheck) {
    this._header = header;
    this._channels = header.Channels;
    this._width = header.CodedWidth;
    this._height = header.StripeHeight;
    this._levels = header.Levels;
    this._rootCount = (this._width >> this._levels) * (this._height >> this._levels) * this._channels;

    this._subtreeSize = new int[this._levels + 1];
    for (var level = 0; level <= this._levels; ++level)
      this._subtreeSize[level] = level <= 1 ? 1 : 1 + 4 * this._subtreeSize[level - 1];
    this._rootBlock = 1 + 3 * this._subtreeSize[this._levels - 1];

    var nodes = (long)this._rootBlock * this._rootCount;
    if (nodes > LuraWaveHeader.MaxStripeSamples * 2)
      throw new System.IO.InvalidDataException("LuraWave stripe needs more tree nodes than this decoder accepts.");
    this._position = new int[nodes];
    this._next = new int[nodes];
    this._flags = new int[nodes];

    // The luminance-like first channel of some modes is coded one or two bit planes finer than the rest.
    this._channelWeight = new int[this._channels];
    this._channelWeight[0] = header.Mode switch {
      LuraWaveColourMode.ReversibleColour or LuraWaveColourMode.Polynomial or LuraWaveColourMode.WideLab => 1,
      LuraWaveColourMode.FourChannel or LuraWaveColourMode.WideReversibleColour or LuraWaveColourMode.WideFourChannel => 2,
      _ => 0,
    };

    // A protected stream decodes to its public quality unless the password is known; with it, the 32-bit check
    // embedded at the public budget must match before decoding continues.
    this._unlocked = passwordCheck.HasValue && header.PublicQuality < LuraWaveHeader.FullQuality;
    this._passwordCheck = passwordCheck ?? 0;
    this._minShift = header.Regions.Length > 0 ? new int[nodes] : [];
  }

  /// <summary>Decodes one stripe into <paramref name="target"/> (two's complement, interleaved channels).</summary>
  /// <param name="target">Receives <c>CodedWidth * StripeHeight * Channels</c> coefficients; overwritten entirely.</param>
  /// <param name="stripeTop">First coded row of this stripe, used to place interest regions.</param>
  public void Decode(int[] target, int stripeTop) {
    this._coefficients = target;
    Array.Clear(target);

    var reader = this._reader!;
    reader.ResetBits();
    if (reader.State == LuraWaveReadState.StripeEnd)
      reader.State = LuraWaveReadState.Reading;

    this.Run(reader.ReadBitsLsbFirst(5), stripeTop);

    for (var i = 0; i < target.Length; ++i)
      if (target[i] < 0)
        target[i] = -(target[i] & int.MaxValue);
  }

  /// <summary>Codes one stripe of two's-complement coefficients (interleaved channels) losslessly.</summary>
  /// <param name="source">The stripe's coefficients.</param>
  /// <param name="stripeTop">First coded row of this stripe, used to place interest regions.</param>
  /// <exception cref="ArgumentException">A coefficient is too large for the 5-bit top-plane field.</exception>
  public void Encode(int[] source, int stripeTop) {
    this._source = new int[source.Length];
    for (var i = 0; i < source.Length; ++i)
      this._source[i] = source[i] < 0 ? Sign | -source[i] : source[i];
    this._coefficients = new int[source.Length];

    var topPlane = this.MeasureReach(stripeTop) + 1;
    if (topPlane > 30)
      throw new ArgumentException("Coefficients are too large to be coded in a LuraWave stream.", nameof(source));

    var writer = this._writer!;
    for (var i = 0; i < 5; ++i)
      writer.Put((topPlane >> i & 1) != 0);
    this.Run(topPlane, stripeTop);
  }

  /// <summary>
  /// Encoder preparation: grows every tree completely and records, per node, the highest plane at which a coefficient
  /// it is responsible for turns significant. Returns that plane over the whole stripe (−1 when everything is zero).
  /// </summary>
  private int MeasureReach(int stripeTop) {
    this._reach = new int[this._position.Length];
    this.PlantRoots(stripeTop);
    var highest = -1;
    for (int root = 0, i = 0; i < this._rootCount; ++i, root += this._rootBlock) {
      this.GrowRoot(root);
      var level = this.Level(root);
      var shift = this.Shift(root);
      var position = this._position[root];
      var dx = this.StepX(level);
      var dy = this.StepY(level);
      var reach = Math.Max(this.SignificancePlane(position + dx, level + shift), this.SignificancePlane(position + dy, level + shift));
      reach = Math.Max(reach, this.SignificancePlane(position + dx + dy, level + shift - 1));
      for (var orientation = Horizontal; orientation <= Diagonal; orientation += Horizontal)
        reach = Math.Max(reach, this.MeasureSubtree(this.ChildOfRoot(root, orientation)));
      this._reach[root] = reach;
      highest = Math.Max(highest, Math.Max(reach, this.SignificancePlane(position, level + shift + 1)));
    }

    return highest;
  }

  private int MeasureSubtree(int node) {
    var level = this.Level(node);
    var dx = this.StepX(level);
    var dy = this.StepY(level);
    var position = this._position[node];
    var exponent = this.Shift(node) + level - 1;
    var reach = Math.Max(
      Math.Max(this.SignificancePlane(position, exponent), this.SignificancePlane(position + dx, exponent)),
      Math.Max(this.SignificancePlane(position + dy, exponent), this.SignificancePlane(position + dx + dy, exponent)));
    if (this.Kind(node) == KindInner) {
      this.GrowInner(node);
      for (var k = 0; k < 4; ++k)
        reach = Math.Max(reach, this.MeasureSubtree(this.ChildOfInner(node, k)));
    }

    this._reach[node] = reach;
    return reach;
  }

  /// <summary>The plane at which a coefficient whose thresholds are scaled down by <c>2^exponent</c> turns significant.</summary>
  private int SignificancePlane(int index, int exponent) {
    var magnitude = this._source[index] & int.MaxValue;
    return magnitude == 0 ? -1 : System.Numerics.BitOperations.Log2((uint)magnitude) + exponent;
  }

  private bool AnyUnopenedChildReaches(int node) {
    if (this.Kind(node) == KindRoot) {
      for (var orientation = Horizontal; orientation <= Diagonal; orientation += Horizontal)
        if (this._reach[this.ChildOfRoot(node, orientation)] >= this._planeBit)
          return true;
      return false;
    }

    for (var k = 0; k < 4; ++k)
      if (this._reach[this.ChildOfInner(node, k)] >= this._planeBit)
        return true;
    return false;
  }

  /// <summary>The traversal shared by both directions, after the top plane is known.</summary>
  private void Run(int topPlane, int stripeTop) {
    var locked = this._header.PublicQuality < LuraWaveHeader.FullQuality && !this._unlocked;
    this._totalPassLimit = PassBudget(locked ? this._header.PublicQuality : LuraWaveHeader.FullQuality, topPlane);
    this._publicPassLimit = this._unlocked ? PassBudget(this._header.PublicQuality, topPlane) : int.MaxValue;
    this._passes = 0;
    if (topPlane == 0)
      return; // an all-zero stripe carries nothing beyond its top-plane field

    var mask = 1 << topPlane;
    mask |= (mask >> 2) | (mask >> 3);
    this.PlantRoots(stripeTop);

    for (var plane = topPlane; plane > this._header.LowestBitPlane && this.Reading; --plane) {
      mask >>= 1;
      this._planeBit = plane - 1;
      this.ForEachListed(node => this.CodeKnownCoefficients(node, mask));
      this.CountPass();
      this.ForEachListed(node => this.SortNode(node, mask));
      this.CountPass();
      this.ForEachListed(node => this.RefineKnownCoefficients(node, mask));
      this.CountPass();
    }

    // A spent pass budget, or coded data that ran out, ends the stripe: the verbatim stage only follows complete planes.
    if (!this.Reading)
      return;

    mask >>= 1;
    this.SendTails(Math.Min(topPlane, this._header.LowestBitPlane), mask);
  }

  /// <summary>
  /// Below the lowest coded plane every node gets its remaining bits verbatim, one tree level per round and one
  /// orientation after the other. Only the deeper steps count against the pass budget: round <c>r</c> and orientation
  /// <c>o</c> count when <c>3L - 3r - o</c> does not exceed <c>3 * plane - 2</c>, the count starting at one.
  /// </summary>
  private void SendTails(int plane, int mask) {
    var totalLeft = this._totalPassLimit - this._passes;
    if (totalLeft == 1)
      return;

    var publicLeft = this._publicPassLimit == int.MaxValue ? int.MaxValue : this._publicPassLimit - this._passes;
    var count = 1;
    if (publicLeft < 2) {
      this.CheckPassword();
      publicLeft = totalLeft;
    }

    var line = 3 * plane - 2;
    ReadOnlySpan<int> orientations = [Horizontal, Vertical, Diagonal];
    for (var round = 1; round < this._levels; ++round)
    for (var o = 0; o < 3; ++o) {
      for (int node = 0, visited = 0; visited < this._rootCount && this.Reading; node = this._next[node], ++visited)
        this.SendTailOfRoot(node, orientations[o], mask);
      if (!this.Reading)
        return;

      if (3 * this._levels - 3 * round - o > line)
        continue;
      if (++count == totalLeft)
        return;
      if (publicLeft <= count) {
        this.CheckPassword();
        publicLeft = totalLeft;
      }
    }
  }


  /// <summary>
  /// Whether stopping bit-plane coding at <paramref name="lowest"/> still delivers every bit: the roots, which the
  /// verbatim rounds never reach, keep all their bits only while the plane stays below the level count. The second
  /// clause is a conservative bound kept from before the verbatim budget was known to always suffice.
  /// </summary>
  internal static bool IsLosslessLowestPlane(int lowest, int levels)
    => lowest == 0 || (lowest < levels && levels - 1 < 3 * lowest);

  /// <summary>How many coding passes a quality value buys for a stream whose top bit plane is <paramref name="topPlane"/>.</summary>
  internal static int PassBudget(int quality, int topPlane) {
    if (quality <= 512) {
      var start = topPlane / 2 - 1;
      return start + (topPlane - start) * quality / 512;
    }

    return quality <= 900
      ? topPlane + topPlane * (quality - 512) / 388
      : 2 * topPlane + topPlane * (quality - 900) / 124;
  }

  private LuraWaveReadState State {
    get => this._reader?.State ?? this._writer!.State;
    set {
      if (this._reader != null)
        this._reader.State = value;
      else
        this._writer!.State = value;
    }
  }

  private bool Reading => this.State == LuraWaveReadState.Reading;

  /// <summary>One coded bit: read it when decoding, decide and write it when encoding.</summary>
  private bool Bit(Question question, int subject = 0, int threshold = 0) {
    if (this._reader != null)
      return this._reader.ReadBit();

    var writer = this._writer!;
    if (writer.State != LuraWaveReadState.Reading)
      return false;

    var answer = question switch {
      Question.Significant => (this._source[subject] & int.MaxValue) >= TopBit(threshold),
      Question.Negative => this._source[subject] < 0,
      Question.Refinement => (this._source[subject] & TopBit(threshold)) != 0,
      Question.NodeOpens => this._reach[subject] >= this._planeBit,
      Question.AnyChildOpens => this.AnyUnopenedChildReaches(subject),
      Question.Verbatim => (this._source[subject] & TopBit(threshold)) != 0,
      Question.VerbatimNegative => this._source[subject] < 0,
      _ => false,
    };
    writer.Put(answer);
    return answer;
  }

  private static int TopBit(int threshold) => 1 << (31 - System.Numerics.BitOperations.LeadingZeroCount((uint)threshold));

  private void ForEachListed(Action<int> visit) {
    for (var node = 0; node != End && this.Reading; node = this._next[node])
      visit(node);
  }

  /// <summary>Counts a finished bit-plane pass: the total budget ends coding, the public budget asks for the password check.</summary>
  private void CountPass() {
    ++this._passes;
    if (this._passes >= this._totalPassLimit) {
      this.State = LuraWaveReadState.Exhausted;
      return;
    }

    if (this._passes < this._publicPassLimit)
      return;

    this.CheckPassword();
    this._publicPassLimit = int.MaxValue;
  }

  /// <summary>Reads the 32-bit password check (least significant bit first) and refuses to go on when it differs.</summary>
  private void CheckPassword() {
    var check = this._passwordCheck;
    for (var i = 0; i < 32; ++i, check >>= 1)
      if (this.Bit(Question.PasswordCheck) != ((check & 1) != 0))
        throw new System.IO.InvalidDataException("The LuraWave stream's embedded password check does not match, although the header's check value did.");
  }

  #region tree construction

  private int Kind(int node) => this._flags[node] & KindMask;
  private int Level(int node) => (this._flags[node] >> LevelOffset) & LevelMask;
  private int Shift(int node) => (this._flags[node] >> ShiftOffset) & ShiftMask;
  private int Orientation(int node) => this._flags[node] & OrientationMask;
  private int StepX(int level) => this._channels << level;
  private int StepY(int level) => this._channels * this._width << level;

  /// <summary>Lays out every root of the stripe and threads them, in channel/row/column order, onto the list.</summary>
  private void PlantRoots(int stripeTop) {
    var block = 1 << this._levels;
    var withRegions = this._header.Regions.Length > 0;
    var node = 0;
    var remaining = this._rootCount;
    for (var channel = 0; channel < this._channels; ++channel) {
      var baseShift = 1 + this._channelWeight[channel];
      for (var y = 0; y < this._height; y += block)
      for (var x = 0; x < this._width; x += block) {
        var regionShift = withRegions ? this.SeedRegionShifts(node, x, y + stripeTop, baseShift) : 0;
        this._position[node] = (y * this._width + x) * this._channels + channel;
        this._flags[node] = KindRoot | (baseShift + regionShift) << ShiftOffset | (this._levels - 1) << LevelOffset;
        this._next[node] = --remaining > 0 ? node + this._rootBlock : End;
        this._tail = node;
        node += this._rootBlock;
      }
    }
  }

  /// <summary>
  /// With interest regions, every node's shift is fixed up front from the regions its pixels fall into; returns the
  /// largest extra shift found below the root, which the root itself takes on. The smallest shift below each node is
  /// kept as well, for <see cref="ChildrenSpent"/>.
  /// </summary>
  private int SeedRegionShifts(int root, int x, int y, int baseShift) {
    var top = this._levels - 1;
    var size = this._subtreeSize[top];
    var (largest, smallest) = this.SeedRegion(root + 1, x, y, top, Horizontal, baseShift);
    var (vertical, verticalMin) = this.SeedRegion(root + 1 + size, x, y, top, Vertical, baseShift);
    var (diagonal, diagonalMin) = this.SeedRegion(root + 1 + 2 * size, x, y, top, Diagonal, baseShift - 1);
    this._minShift[root] = Math.Min(Math.Min(smallest, verticalMin) + baseShift, diagonalMin + baseShift - 1);
    return Math.Max(largest, Math.Max(vertical, diagonal));
  }

  /// <summary>Seeds one subtree; returns the largest and smallest extra shift of its finest-level nodes.</summary>
  private (int Largest, int Smallest) SeedRegion(int node, int x, int y, int level, int orientation, int baseShift) {
    var span = 1 << level;
    int largest, smallest;
    if (level > 1) {
      var size = this._subtreeSize[level - 1];
      (largest, smallest) = this.SeedRegion(node + 1, x, y, level - 1, orientation, baseShift);
      foreach (var (dx, dy, k) in (ReadOnlySpan<(int, int, int)>)[(span, 0, 1), (0, span, 2), (span, span, 3)]) {
        var (high, low) = this.SeedRegion(node + 1 + k * size, x + dx, y + dy, level - 1, orientation, baseShift);
        largest = Math.Max(largest, high);
        smallest = Math.Min(smallest, low);
      }
    } else {
      // A finest-level node is judged by one representative pixel of its 4x4 footprint; any region containing it
      // gives the (shared) region shift.
      var half = span >> 1;
      var (px, py) = orientation switch {
        Horizontal => (x + span + half, y + span),
        Vertical => (x + span, y + span + half),
        _ => (x + span + half, y + span + half),
      };
      largest = 0;
      foreach (var region in this._header.Regions)
        if (px >= region.X && px <= region.X + region.Width && py >= region.Y && py <= region.Y + region.Height) {
          largest = this._header.RegionShift;
          break;
        }

      smallest = largest;
    }

    this._flags[node] = (largest + baseShift) << ShiftOffset;
    this._minShift[node] = smallest + baseShift;
    return (largest, smallest);
  }

  /// <summary>Gives a node a fresh flags word, either from scratch or on top of a region-seeded shift.</summary>
  private void Describe(int node, int description, int shift) {
    if (this._header.Regions.Length == 0)
      this._flags[node] = description | shift << ShiftOffset;
    else
      this._flags[node] += description;
  }

  private static int KindForLevel(int level) => level > 1 ? KindInner : KindLeaf;

  /// <summary>Creates the three orientation quadtrees under a root.</summary>
  private void GrowRoot(int root) {
    var level = this.Level(root);
    var shift = this.Shift(root);
    var position = this._position[root];
    var dx = this.StepX(level - 1);
    var dy = this.StepY(level - 1);
    var size = this._subtreeSize[level];
    var description = level << LevelOffset | KindForLevel(level);

    this._position[root + 1] = position + dx;
    this.Describe(root + 1, description | Horizontal, shift);
    this._position[root + 1 + size] = position + dy;
    this.Describe(root + 1 + size, description | Vertical, shift);
    this._position[root + 1 + 2 * size] = position + dx + dy;
    this.Describe(root + 1 + 2 * size, description | Diagonal, shift - 1);
  }

  /// <summary>Creates the four children of an inner node; they cover the 2x2 groups one level finer.</summary>
  private void GrowInner(int node) {
    var level = this.Level(node);
    var orientation = this.Orientation(node);
    var shift = this.Shift(node);
    var halfX = this.StepX(level - 2);
    var halfY = this.StepY(level - 2);
    var origin = this._position[node] - orientation switch {
      Horizontal => halfX,
      Vertical => halfY,
      Diagonal => halfX + halfY,
      _ => 0,
    };
    var dx = halfX << 2;
    var dy = halfY << 2;
    var size = this._subtreeSize[level - 1];
    var description = (level - 1) << LevelOffset | orientation | KindForLevel(level - 1);

    ReadOnlySpan<int> offsets = [0, dx, dy, dx + dy];
    for (var k = 0; k < 4; ++k) {
      var child = node + 1 + k * size;
      this._position[child] = origin + offsets[k];
      this.Describe(child, description, shift);
    }
  }

  private int ChildOfRoot(int root, int orientation) {
    var size = this._subtreeSize[this.Level(root)];
    return root + 1 + (orientation >> 13) * size - size;
  }

  private int ChildOfInner(int node, int k) => node + 1 + k * this._subtreeSize[this.Level(node) - 1];

  private void Append(int node) {
    this._next[this._tail] = node;
    this._next[node] = End;
    this._tail = node;
  }

  #endregion

  #region coefficient primitives

  /// <summary>Significance test of one coefficient that is still zero: a bit says whether it reaches <paramref name="threshold"/>, a second gives its sign.</summary>
  private void TestCoefficient(int index, int threshold) {
    if (this._coefficients[index] == 0 && this.Bit(Question.Significant, index, threshold))
      this._coefficients[index] |= this.Bit(Question.Negative, index) ? threshold | Sign : threshold;
  }

  /// <summary>
  /// Refinement of a coefficient already above <paramref name="threshold"/>: the bit at the threshold's top <c>t</c> and
  /// the guess below it are cleared, <c>t/8</c> becomes the new reconstruction offset, and the coded bit sets <c>t</c>.
  /// </summary>
  private void RefineCoefficient(int index, int threshold) {
    ref var value = ref this._coefficients[index];
    if ((value & int.MaxValue) <= threshold)
      return;

    // A bit read past the end of the coded data leaves the coefficient as it was.
    var bit = this.Bit(Question.Refinement, index, threshold);
    if (!this.Reading)
      return;

    var top = TopBit(threshold);
    value = (value & ~(top | top >> 1)) | top >> 3;
    if (bit)
      value |= top;
  }

  /// <summary>The four coefficients a leaf or inner node stands for.</summary>
  private void ForEachOwnCoefficient(int node, int mask, Action<int, int> act) {
    var level = this.Level(node);
    var threshold = (int)((uint)mask >> (this.Shift(node) + level - 1));
    if (threshold <= 0)
      return;

    var position = this._position[node];
    var dx = this.StepX(level);
    var dy = this.StepY(level);
    act(position, threshold);
    act(position + dx, threshold);
    act(position + dy, threshold);
    act(position + dx + dy, threshold);
  }

  /// <summary>The low-pass sample of a root, then — once its orientation trees exist — its three own details.</summary>
  private void ForEachRootCoefficient(int root, int mask, Action<int, int> act) {
    var level = this.Level(root);
    var shift = this.Shift(root);
    var position = this._position[root];
    var lowPass = (int)((uint)mask >> (level + shift + 1));
    if (lowPass > 0)
      act(position, lowPass);
    if ((this._flags[root] & StateMask) == 0)
      return;

    var dx = this.StepX(level);
    var dy = this.StepY(level);
    var straight = (int)((uint)mask >> (level + shift));
    if (straight > 0) {
      act(position + dx, straight);
      act(position + dy, straight);
    }

    var diagonal = (int)((uint)mask >> (level + shift - 1));
    if (diagonal > 0)
      act(position + dx + dy, diagonal);
  }

  #endregion

  #region the three passes

  /// <summary>First pass of a bit plane: significance of the coefficients of every node already on the list.</summary>
  private void CodeKnownCoefficients(int node, int mask) => this.VisitCoefficients(node, mask, this.TestCoefficient);

  /// <summary>Third pass of a bit plane: one more magnitude bit for every coefficient that was already significant.</summary>
  private void RefineKnownCoefficients(int node, int mask) => this.VisitCoefficients(node, mask, this.RefineCoefficient);

  private void VisitCoefficients(int node, int mask, Action<int, int> act) {
    switch (this.Kind(node)) {
      case KindLeaf or KindInner:
        this.ForEachOwnCoefficient(node, mask, act);
        break;
      case KindRoot:
        this.ForEachRootCoefficient(node, mask, act);
        break;
    }
  }

  /// <summary>Second pass of a bit plane: split sets whose bits say they hold something new.</summary>
  private void SortNode(int node, int mask) {
    switch (this.Kind(node)) {
      case KindLeaf:
        if ((this._flags[node] & StateMask) == 0)
          this.TryOpenLeaf(node, mask);
        break;
      case KindInner:
        if ((this._flags[node] & StateMask) == 0)
          this.TryOpenInner(node, mask);
        else
          this.SortChildren(node, mask, 4, ChildMarks);
        break;
      case KindRoot:
        this.SortRoot(node, mask);
        break;
    }
  }

  /// <summary>A leaf opens when its bit says one of its four coefficients is significant; it then joins the list.</summary>
  private bool TryOpenLeaf(int node, int mask) {
    var threshold = (int)((uint)mask >> (this.Shift(node) + this.Level(node) - 1));
    if (threshold <= 0 || !this.Bit(Question.NodeOpens, node))
      return false;

    this.ForEachOwnCoefficient(node, mask, this.TestCoefficient);
    this._flags[node] |= Expanded;
    this.Append(node);
    return true;
  }

  /// <summary>An inner node opens when its bit is set: its children are created, its own four coefficients tested, and it joins the list.</summary>
  private bool TryOpenInner(int node, int mask) {
    if (!this.Bit(Question.NodeOpens, node))
      return false;

    this.GrowInner(node);
    this.ForEachOwnCoefficient(node, mask, this.TestCoefficient);
    this._flags[node] |= Expanded;
    this.Append(node);
    return true;
  }

  private bool TryOpen(int node, int mask)
    => this.Kind(node) == KindLeaf ? this.TryOpenLeaf(node, mask) : this.TryOpenInner(node, mask);

  /// <summary>
  /// An opened node with unopened children first spends one bit on "any child opens now"; after a yes, each child
  /// not yet open is tried in turn, now and in every later sorting pass, until all are open.
  /// </summary>
  private void SortChildren(int node, int mask, int count, int allOpen) {
    var flags = this._flags[node];
    switch (flags & StateMask) {
      case Expanded:
        if (!this.Bit(Question.AnyChildOpens, node))
          return;
        flags ^= StateMask;
        if (this.ChildrenSpent(node, mask)) {
          this._flags[node] = flags;
          return;
        }
        break;
      case Exhausting:
        if (count == 3 && (flags & ChildMarks) == allOpen)
          return;
        if (this.ChildrenSpent(node, mask))
          return;
        break;
      default:
        return;
    }

    for (var k = 0; k < count; ++k) {
      var mark = 0x80 >> k;
      if ((flags & mark) != 0)
        continue;

      var child = count == 3 ? this.ChildOfRoot(node, (k + 1) << 13) : this.ChildOfInner(node, k);
      if (this.TryOpen(child, mask))
        flags |= mark;
    }

    if ((flags & ChildMarks) == allOpen)
      flags |= Complete;
    this._flags[node] = flags;
  }

  /// <summary>
  /// Whether every threshold below <paramref name="node"/> has run out at this plane: its unopened children are then
  /// given up without spending a bit, now and in every later pass.
  /// </summary>
  private bool ChildrenSpent(int node, int mask) => ((uint)mask >> this.MinShift(node)) == 0;

  /// <summary>The smallest shift of any finest-level node below <paramref name="node"/>.</summary>
  private int MinShift(int node) => this._minShift.Length > 0
    ? this._minShift[node]
    : this.Kind(node) == KindRoot ? this.Shift(node) - 1 : this.Shift(node);

  /// <summary>A root first spends a bit on growing its orientation trees (testing its own details), then behaves like an inner node with three children.</summary>
  private void SortRoot(int root, int mask) {
    if ((this._flags[root] & StateMask) == 0) {
      if (!this.Bit(Question.NodeOpens, root))
        return;

      this.GrowRoot(root);
      var level = this.Level(root);
      var shift = this.Shift(root);
      var position = this._position[root];
      var dx = this.StepX(level);
      var dy = this.StepY(level);
      var straight = (int)((uint)mask >> (level + shift));
      if (straight > 0) {
        this.TestCoefficient(position + dx, straight);
        this.TestCoefficient(position + dy, straight);
      }

      var diagonal = (int)((uint)mask >> (level + shift - 1));
      if (diagonal > 0)
        this.TestCoefficient(position + dx + dy, diagonal);

      this._flags[root] |= Expanded;
    }

    this.SortChildren(root, mask, 3, 0xE0);
  }

  #endregion

  #region verbatim tail

  /// <summary>Below the lowest coded bit plane, one orientation tree of a root at a time gets its remaining bits verbatim, one level per round.</summary>
  private void SendTailOfRoot(int root, int orientation, int mask) {
    if ((this._flags[root] & StateMask) == 0) {
      this.GrowRoot(root);
      this._flags[root] |= Expanded;
    }

    this.SendTail(this.ChildOfRoot(root, orientation), mask);
  }

  private void SendTail(int node, int mask) {
    var flags = this._flags[node];
    switch (flags & KindMask) {
      case KindInner:
        this._flags[node] |= KindFinished;
        if ((flags & StateMask) == 0)
          this.GrowInner(node);
        this.SendVerbatim(node, mask);
        break;
      case KindLeaf:
        this.SendVerbatim(node, mask);
        break;
      case KindFinished:
        for (var k = 0; k < 4; ++k)
          this.SendTail(this.ChildOfInner(node, k), mask);
        break;
    }
  }

  /// <summary>
  /// Each bit pattern from the threshold mask downwards is set or cleared as a whole, then coefficients that only
  /// now became non-zero get their sign.
  /// </summary>
  private void SendVerbatim(int node, int mask) {
    var level = this.Level(node);
    var threshold = (int)((uint)mask >> (this.Shift(node) + level - 1));
    if (threshold <= 0)
      return;

    var position = this._position[node];
    var dx = this.StepX(level);
    var dy = this.StepY(level);
    ReadOnlySpan<int> group = [position, position + dx, position + dy, position + dx + dy];
    for (var bits = threshold; bits > 0; bits >>= 1)
      foreach (var index in group)
        if (this.Bit(Question.Verbatim, index, bits))
          this._coefficients[index] |= bits;
        else
          this._coefficients[index] &= ~bits;

    var limit = threshold << 1;
    foreach (var index in group) {
      var magnitude = this._coefficients[index] & int.MaxValue;
      if (magnitude < limit && magnitude > 0 && this.Bit(Question.VerbatimNegative, index))
        this._coefficients[index] |= Sign;
    }
  }

  #endregion
}

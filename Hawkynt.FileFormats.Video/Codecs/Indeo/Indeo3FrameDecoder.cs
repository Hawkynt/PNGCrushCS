using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Decodes one Indeo 3 frame: three planes, each cut into cells by a binary tree, each cell either
/// copied from the other frame buffer or built out of a fixed table of delta pairs.
/// </summary>
/// <remarks>
/// <b>Two streams in one buffer.</b> A plane opens with its motion vectors and then carries a bit
/// stream and a byte stream at the same time, interleaved: the bits are a binary tree that cuts the
/// plane into cells, and every leaf of that tree names bytes that follow the tree's current position.
/// Reading a leaf consumes whole bytes, which the tree then has to be pushed past — but only once the
/// tree reaches the next byte boundary, which is why the skip is remembered rather than taken (see
/// <see cref="_Resynchronise"/>). Getting that one rule wrong desynchronises the tree against the
/// cells, and the picture that comes out still looks like a picture.
/// <para/>
/// <b>Seven bits a sample.</b> Every write masks the eighth away, and every addition is done on two,
/// four or eight samples at once as one wide integer — so a delta that overflows one sample carries
/// into the next before the mask removes it. That is not an accident of an implementation but what the
/// format's own encoders coded against, so the arithmetic is reproduced as it stands rather than
/// clamped sample by sample.
/// <para/>
/// <b>Cells are addressed in 4x4 blocks</b> and a coding mode says how many samples one coded block
/// covers: modes 0 and 1 code 4x4, modes 3 and 4 code 4x8 with the odd rows interpolated, mode 10 codes
/// 8x8 the same way in both directions, and mode 11 is 4x8 for motion-compensated cells only.
/// </remarks>
internal sealed class Indeo3FrameDecoder {

  private const int _OS_HEADER_LENGTH = 16;

  /// <summary>The four bytes the operating-system header's checksum is taken against, big-endian.</summary>
  private const uint _OS_HEADER_ID = 0x46524D48;

  /// <summary>The one bitstream version the codec defines.</summary>
  private const int _CODEC_VERSION = 32;

  /// <summary>Where a frame's sixteen secondary table indices for modes 1 and 4 sit.</summary>
  private const int _ALT_QUANT_OFFSET = 48;

  /// <summary>A frame stating this many bytes of data carries none and shows nothing.</summary>
  private const int _SYNC_FRAME_SIZE = 16;

  /// <summary>How deep a plane's binary tree may go before it is called corrupt.</summary>
  private const int _CELL_STACK_MAX = 20;

  private const int _LUMA_STRIP_WIDTH = 40;
  private const int _CHROMA_STRIP_WIDTH = 10;

  private const int _MIN_DIMENSION = 16;
  private const int _MAX_WIDTH = 640;
  private const int _MAX_HEIGHT = 480;

  /// <summary>The most motion vectors a plane may state.</summary>
  private const int _MAX_VECTORS = 256;

  // Frame flags.
  private const int _BS_8BIT_PEL = 1 << 1;
  private const int _BS_MV_Y_HALF = 1 << 4;
  private const int _BS_MV_X_HALF = 1 << 5;
  private const int _BS_BUFFER_SHIFT = 9;

  // Binary tree codes.
  private const int _H_SPLIT = 0;
  private const int _V_SPLIT = 1;
  private const int _INTRA_NULL = 2;
  private const int _INTER_DATA = 3;

  // The byte values a cell's data uses as escapes rather than as table indices.
  private const int _RLE_FIRST = 248;
  private const int _RLE_SKIP_AND_NEXT = 249;
  private const int _RLE_SKIP_BLOCK = 250;
  private const int _RLE_BLOCK_RUN = 251;
  private const int _RLE_REST_AND_NEXT = 252;
  private const int _RLE_REST_OF_BLOCK = 253;
  private const int _RLE_TO_THIRD_LINE = 254;
  private const int _RLE_TO_SECOND_LINE = 255;

  private Indeo3Plane[] _planes = [];
  private int _alignedWidth;
  private int _alignedHeight;
  private int _outputWidth;
  private int _outputHeight;

  private int _bufferSelect;
  private int _codebookOffset;
  private int _lumaData;
  private int _blueData;
  private int _redData;
  private int _lumaSize;
  private int _blueSize;
  private int _redSize;

  private int _vectorCount;
  private int _vectors;
  private int _nextCellData;
  private int _lastByte;
  private bool _needsResynchronising;
  private int _pendingSkip;

  internal Indeo3FrameDecoder(int width, int height) => this._Allocate(width, height);

  /// <summary>The picture's width in samples, as the stream last stated it.</summary>
  internal int Width => this._outputWidth;

  /// <summary>The picture's height in samples.</summary>
  internal int Height => this._outputHeight;

  /// <summary>The chrominance planes' width, a quarter of the picture's rounded up.</summary>
  internal int ChromaWidth => (this._outputWidth + 3) >> 2;

  /// <summary>The chrominance planes' height.</summary>
  internal int ChromaHeight => (this._outputHeight + 3) >> 2;

  /// <summary>The luminance plane of the frame decoded last.</summary>
  internal byte[] Luma { get; private set; } = [];

  /// <summary>The blue-difference plane of the frame decoded last.</summary>
  internal byte[] Cb { get; private set; } = [];

  /// <summary>The red-difference plane of the frame decoded last.</summary>
  internal byte[] Cr { get; private set; } = [];

  /// <summary>
  /// Decodes one packet and says whether it produced a picture.
  /// </summary>
  /// <remarks>
  /// A stream carries sync frames that state no data at all. They are not pictures and not errors — the
  /// frame before stays on screen — which is why nothing is returned rather than the previous picture
  /// being handed out a second time under a new timestamp.
  /// </remarks>
  internal bool Decode(ReadOnlySpan<byte> frame) {
    if (!this._ReadHeaders(frame))
      return false;

    this._DecodePlane(frame, this._planes[0], this._lumaData, this._lumaSize, _LUMA_STRIP_WIDTH);
    this._DecodePlane(frame, this._planes[1], this._blueData, this._blueSize, _CHROMA_STRIP_WIDTH);
    this._DecodePlane(frame, this._planes[2], this._redData, this._redSize, _CHROMA_STRIP_WIDTH);

    this.Luma = this._planes[0].ToSamples(this._bufferSelect, this._outputWidth, this._outputHeight);
    this.Cb = this._planes[1].ToSamples(this._bufferSelect, this.ChromaWidth, this.ChromaHeight);
    this.Cr = this._planes[2].ToSamples(this._bufferSelect, this.ChromaWidth, this.ChromaHeight);
    return true;
  }

  // ============================================================================================
  // The frame's two headers
  // ============================================================================================

  /// <summary>
  /// Reads the operating-system header and the bitstream header, and says whether a picture follows.
  /// </summary>
  /// <remarks>
  /// The three planes are stored in no fixed order and each states only where it starts, so how long
  /// one is has to be worked out from where the next one begins — the smallest of the other two starts
  /// that is still after this one, or the end of the frame's data when there is none.
  /// </remarks>
  private bool _ReadHeaders(ReadOnlySpan<byte> frame) {
    if (frame.Length < _ALT_QUANT_OFFSET + 16)
      throw new InvalidDataException(
        $"An Indeo 3 frame is {frame.Length} bytes, short of the {_ALT_QUANT_OFFSET + 16} its two headers take.");

    var frameNumber = BinaryPrimitives.ReadUInt32LittleEndian(frame);
    var second = BinaryPrimitives.ReadUInt32LittleEndian(frame[4..]);
    var checksum = BinaryPrimitives.ReadUInt32LittleEndian(frame[8..]);
    var stated = BinaryPrimitives.ReadUInt32LittleEndian(frame[12..]);

    if ((frameNumber ^ second ^ stated ^ _OS_HEADER_ID) != checksum)
      throw new InvalidDataException(
        "An Indeo 3 frame's operating-system header does not check out against its own checksum.");

    var header = frame[_OS_HEADER_LENGTH..];
    var version = BinaryPrimitives.ReadUInt16LittleEndian(header);
    if (version != _CODEC_VERSION)
      throw new NotSupportedException(
        $"An Indeo 3 frame states bitstream version {version}, where the codec defines {_CODEC_VERSION}.");

    var flags = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
    var dataSize = (int)((BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) + 7) >> 3);
    this._codebookOffset = header[8];

    if (dataSize == _SYNC_FRAME_SIZE)
      return false;

    dataSize = Math.Min(dataSize, frame.Length - _OS_HEADER_LENGTH);

    var height = BinaryPrimitives.ReadUInt16LittleEndian(header[12..]);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
    if (width != this._alignedWidth || height != this._alignedHeight) {
      if (width < _MIN_DIMENSION || width > _MAX_WIDTH || height < _MIN_DIMENSION || height > _MAX_HEIGHT
          || (width & 3) != 0 || (height & 3) != 0)
        throw new InvalidDataException(
          $"An Indeo 3 frame states a picture of {width}x{height}, which the codec cannot hold.");

      this._Allocate(width, height);
    }

    // Luminance first, then the two chrominance planes — but the frame states them in the order Y, V,
    // U, and the plane indices here run Y, U, V. Which slot a start lands in does not matter to the
    // sizing below, which only ever compares a start against the other two.
    var starts = new int[3];
    starts[0] = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
    starts[2] = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
    starts[1] = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);

    var sizes = new int[3];
    for (var j = 0; j < 3; ++j) {
      var end = dataSize;
      for (var i = 2; i >= 0; --i)
        if (starts[i] < end && starts[i] > starts[j])
          end = starts[i];

      sizes[j] = end - starts[j];
    }

    var lowest = Math.Min(starts[0], Math.Min(starts[1], starts[2]));
    var highest = Math.Max(starts[0], Math.Max(starts[1], starts[2]));
    var smallest = Math.Min(sizes[0], Math.Min(sizes[1], sizes[2]));

    // A plane may not start inside the headers, may not start past the data, and may not be empty.
    if (lowest < _ALT_QUANT_OFFSET || highest >= dataSize - _OS_HEADER_LENGTH || smallest <= 0)
      throw new InvalidDataException("An Indeo 3 frame states plane offsets that do not lie inside its own data.");

    if (dataSize == _SYNC_FRAME_SIZE)
      return false;

    if ((flags & _BS_8BIT_PEL) != 0)
      throw new NotSupportedException(
        "An Indeo 3 frame is coded at eight bits a sample, a form no encoder is known to have written "
        + "and whose tables are not those of the seven-bit one.");

    if ((flags & (_BS_MV_X_HALF | _BS_MV_Y_HALF)) != 0)
      throw new NotSupportedException(
        "An Indeo 3 frame uses half-sample motion vectors, a form no encoder is known to have written "
        + "and whose interpolation is stated nowhere.");

    this._bufferSelect = (flags >> _BS_BUFFER_SHIFT) & 1;
    this._lumaData = _OS_HEADER_LENGTH + starts[0];
    this._blueData = _OS_HEADER_LENGTH + starts[1];
    this._redData = _OS_HEADER_LENGTH + starts[2];
    this._lumaSize = sizes[0];
    this._blueSize = sizes[1];
    this._redSize = sizes[2];

    if (this._lumaData + this._lumaSize > frame.Length
        || this._blueData + this._blueSize > frame.Length
        || this._redData + this._redSize > frame.Length)
      throw new InvalidDataException("An Indeo 3 frame states a plane that reaches past the packet carrying it.");

    return true;
  }

  private void _Allocate(int width, int height) {
    var lumaWidth = Indeo3Plane.Align(width, 2);
    var lumaHeight = Indeo3Plane.Align(height, 2);

    if (lumaWidth < _MIN_DIMENSION || lumaWidth > _MAX_WIDTH || lumaHeight < _MIN_DIMENSION || lumaHeight > _MAX_HEIGHT)
      throw new NotSupportedException(
        $"An Indeo 3 picture of {lumaWidth}x{lumaHeight} is outside the {_MIN_DIMENSION}x{_MIN_DIMENSION} to "
        + $"{_MAX_WIDTH}x{_MAX_HEIGHT} the codec defines.");

    var chromaWidth = Indeo3Plane.Align(lumaWidth >> 2, 4);
    var chromaHeight = Indeo3Plane.Align(lumaHeight >> 2, 4);

    this._planes = [new(lumaWidth, lumaHeight), new(chromaWidth, chromaHeight), new(chromaWidth, chromaHeight)];
    this._alignedWidth = lumaWidth;
    this._alignedHeight = lumaHeight;
    this._outputWidth = width;
    this._outputHeight = height;
  }

  // ============================================================================================
  // A plane and the tree that cuts it up
  // ============================================================================================

  private void _DecodePlane(ReadOnlySpan<byte> frame, Indeo3Plane plane, int at, int size, int stripWidth) {
    if (size < 4)
      throw new InvalidDataException($"An Indeo 3 plane is {size} bytes, short of the four its motion vector count alone takes.");

    var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[at..]);
    at += 4;
    size -= 4;

    if (count > _MAX_VECTORS)
      throw new InvalidDataException($"An Indeo 3 plane states {count} motion vectors, where the codec allows {_MAX_VECTORS}.");

    if (count * 2 > size)
      throw new InvalidDataException($"An Indeo 3 plane states {count} motion vectors, which do not fit in the {size} bytes it holds.");

    this._vectorCount = count;
    this._vectors = at;
    this._lastByte = at + size;
    this._pendingSkip = 0;
    this._needsResynchronising = false;

    var bits = new Indeo3BitReader(frame, at + count * 2, (size - count * 2) * 8);
    var cell = new Cell {
      Width = plane.Width >> 2,
      Height = plane.Height >> 2,
      MotionVector = -1,
    };

    this._ParseTree(frame, ref bits, plane, _INTRA_NULL, ref cell, _CELL_STACK_MAX, stripWidth);
  }

  /// <summary>One region of a plane, in 4x4 blocks, as the binary tree has cut it so far.</summary>
  private struct Cell {

    internal int XPos;
    internal int YPos;
    internal int Width;
    internal int Height;

    /// <summary>False while the tree still says whether the cell moves; true once it says how it is coded.</summary>
    internal bool InCodingTree;

    /// <summary>Where the cell's motion vector sits in the frame, or -1 when the cell is intra-coded.</summary>
    internal int MotionVector;
  }

  /// <summary>
  /// Walks one node of a plane's binary tree, splitting the parent cell and reading what its parts are.
  /// </summary>
  /// <remarks>
  /// Two trees in one, and the same two codes mean different things in each. While a cell is in the
  /// first tree the codes say whether it moves — <c>INTRA_NULL</c> marks it still, <c>INTER_DATA</c>
  /// takes a motion vector — and either answer moves it into the second tree, where those same two
  /// codes instead mean "copy this cell whole" and "here is its coded data". The two split codes cut
  /// cells in both.
  /// <para/>
  /// The parent is passed by reference because a split takes a piece <i>off</i> it: the child keeps the
  /// piece and the parent keeps what is left, so a run of splits walks across the plane rather than
  /// subdividing one region over and over.
  /// </remarks>
  private void _ParseTree(
    ReadOnlySpan<byte> frame, ref Indeo3BitReader bits, Indeo3Plane plane,
    int code, ref Cell parent, int depth, int stripWidth) {
    if (depth <= 0)
      throw new InvalidDataException($"An Indeo 3 plane's binary tree is deeper than the {_CELL_STACK_MAX} levels the codec allows.");

    var cell = parent;
    switch (code) {
      case _H_SPLIT:
        cell.Height = _Split(parent.Height);
        parent.YPos += cell.Height;
        parent.Height -= cell.Height;
        if (parent.Height <= 0 || cell.Height <= 0)
          throw new InvalidDataException("An Indeo 3 horizontal split leaves a cell with no height.");

        break;
      case _V_SPLIT:
        // A cell wider than a strip is cut to whole strips rather than in half, which is what keeps the
        // strips of a plane aligned to each other however the tree above them was built.
        cell.Width = cell.Width > stripWidth
          ? (cell.Width <= stripWidth << 1 ? 1 : 2) * stripWidth
          : _Split(parent.Width);
        parent.XPos += cell.Width;
        parent.Width -= cell.Width;
        if (parent.Width <= 0 || cell.Width <= 0)
          throw new InvalidDataException("An Indeo 3 vertical split leaves a cell with no width.");

        break;
    }

    while (bits.BitsLeft >= 2) {
      this._Resynchronise(ref bits);
      switch (bits.ReadBits(2)) {
        case _H_SPLIT:
          this._ParseTree(frame, ref bits, plane, _H_SPLIT, ref cell, depth - 1, stripWidth);
          break;
        case _V_SPLIT:
          this._ParseTree(frame, ref bits, plane, _V_SPLIT, ref cell, depth - 1, stripWidth);
          break;
        case _INTRA_NULL:
          if (!cell.InCodingTree) {
            cell.MotionVector = -1;
            cell.InCodingTree = true;
            break;
          }

          this._Resynchronise(ref bits);
          var nullCode = bits.ReadBits(2);
          if (nullCode >= 2)
            throw new InvalidDataException($"An Indeo 3 cell states null code {nullCode}, where the codec defines two.");

          if (nullCode == 1)
            throw new NotSupportedException(
              "An Indeo 3 cell asks to be skipped rather than copied — a variant no sample is known to carry, "
              + "and whose effect on the two frame buffers is stated nowhere.");

          _CheckCell(cell, plane);
          if (cell.MotionVector < 0)
            throw new InvalidDataException("An Indeo 3 cell asks to be copied from a frame it states no motion vector for.");

          this._CopyCell(frame, plane, cell);
          return;
        case _INTER_DATA:
          if (!cell.InCodingTree) {
            if (!this._needsResynchronising)
              this._nextCellData = bits.NextByteIndex;

            if (this._nextCellData >= this._lastByte)
              throw new InvalidDataException("An Indeo 3 cell's motion vector index lies past the end of its plane.");

            var index = frame[this._nextCellData++];
            if (index >= this._vectorCount)
              throw new InvalidDataException(
                $"An Indeo 3 cell names motion vector {index} of the {this._vectorCount} its plane states.");

            cell.MotionVector = this._vectors + index * 2;
            cell.InCodingTree = true;
            this._Defer(8);
            break;
          }

          if (!this._needsResynchronising)
            this._nextCellData = bits.NextByteIndex;

          _CheckCell(cell, plane);
          var used = this._DecodeCell(frame, plane, cell);
          this._Defer(used * 8);
          this._nextCellData += used;
          return;
      }
    }

    throw new InvalidDataException("An Indeo 3 plane's binary tree runs out of bits before every cell is accounted for.");
  }

  /// <summary>How much of a cell a split takes off it, in 4x4 blocks.</summary>
  private static int _Split(int size) => size > 2 ? ((size + 2) >> 2) << 1 : 1;

  private static void _CheckCell(Cell cell, Indeo3Plane plane) {
    if (cell.XPos + cell.Width > plane.Width >> 2 || cell.YPos + cell.Height > plane.Height >> 2)
      throw new InvalidDataException(
        $"An Indeo 3 cell at {cell.XPos},{cell.YPos} of {cell.Width}x{cell.Height} blocks reaches outside its own plane.");
  }

  /// <summary>Remembers that a leaf consumed bytes the tree has to be pushed past.</summary>
  private void _Defer(int bitCount) {
    this._pendingSkip += bitCount;
    this._needsResynchronising = true;
  }

  /// <summary>
  /// Takes a deferred skip, but only where the tree has reached a byte boundary.
  /// </summary>
  /// <remarks>
  /// The whole of the interleaving is in this condition. A leaf's bytes start at the next byte boundary
  /// at or after the tree's position, so while the tree is still part way through a byte it is inside
  /// the byte the leaf began in and must not be moved; once it reaches a boundary the leaf's bytes are
  /// behind it and the skip is taken all at once.
  /// </remarks>
  private void _Resynchronise(ref Indeo3BitReader bits) {
    if (!this._needsResynchronising || !bits.IsByteAligned)
      return;

    bits.Skip(this._pendingSkip);
    this._pendingSkip = 0;
    this._needsResynchronising = false;
  }

  // ============================================================================================
  // Cells
  // ============================================================================================

  /// <summary>Copies a cell out of the other frame buffer, displaced by its motion vector.</summary>
  private void _CopyCell(ReadOnlySpan<byte> frame, Indeo3Plane plane, Cell cell) {
    var target = (cell.YPos << 2) * plane.Pitch + (cell.XPos << 2);
    var (motionY, motionX) = _MotionOf(frame, cell);
    _CheckMotion(plane, cell, motionX, motionY);

    var source = target + motionY * plane.Pitch + motionX;
    var destination = plane.Buffer(this._bufferSelect);
    var reference = plane.Buffer(this._bufferSelect ^ 1);
    var columns = cell.Width << 2;
    var rows = cell.Height << 2;

    for (var y = 0; y < rows; ++y)
      Array.Copy(
        reference, plane.Origin + source + y * plane.Pitch,
        destination, plane.Origin + target + y * plane.Pitch,
        columns);
  }

  private static (int Y, int X) _MotionOf(ReadOnlySpan<byte> frame, Cell cell)
    => cell.MotionVector < 0 ? (0, 0) : ((sbyte)frame[cell.MotionVector], (sbyte)frame[cell.MotionVector + 1]);

  /// <summary>
  /// Refuses a motion vector that reaches outside the plane.
  /// </summary>
  /// <remarks>
  /// One row above the picture is allowed because there is one: the prediction line the buffers carry
  /// in front of the first row is a real row, and a vector of -1 lands on it.
  /// </remarks>
  private static void _CheckMotion(Indeo3Plane plane, Cell cell, int motionX, int motionY) {
    if ((cell.YPos << 2) + motionY < -1 || (cell.XPos << 2) + motionX < 0
        || ((cell.YPos + cell.Height) << 2) + motionY > plane.Height
        || ((cell.XPos + cell.Width) << 2) + motionX > plane.Width)
      throw new InvalidDataException(
        $"An Indeo 3 cell states a motion vector of {motionX},{motionY} that points outside the frame it predicts from.");
  }

  /// <summary>
  /// Decodes one coded cell and reports how many bytes of the plane it took.
  /// </summary>
  /// <remarks>
  /// The cell's first byte is its whole description: a coding mode in the high nibble and a
  /// quantisation table index in the low one. Modes 1 and 4 do not use that index directly but as an
  /// index into the frame's own sixteen-entry table of index <i>pairs</i>, one for even lines and one
  /// for odd, which is how those two modes alternate between two quantisers down a block.
  /// </remarks>
  private int _DecodeCell(ReadOnlySpan<byte> frame, Indeo3Plane plane, Cell cell) {
    var at = this._nextCellData;
    if (at >= frame.Length)
      throw new InvalidDataException("An Indeo 3 cell begins past the end of the packet carrying it.");

    var start = at;
    var descriptor = frame[at++];
    var mode = descriptor >> 4;
    var tableIndex = descriptor & 0xF;

    var offset = (cell.YPos << 2) * plane.Pitch + (cell.XPos << 2);
    var destination = plane.Buffer(this._bufferSelect);
    var target = plane.Origin + offset;

    byte[]? reference = null;
    var referenceAt = target;

    if (cell.MotionVector < 0) {
      // An intra cell predicts from the row above itself, in the buffer it is being written into.
      reference = destination;
      referenceAt = target - plane.Pitch;
    } else if (mode >= 10) {
      // Modes 10 and 11 add their deltas to the cell in place, so the prediction is copied in first and
      // there is no separate reference at all.
      this._CopyCell(frame, plane, cell);
    } else {
      var (motionY, motionX) = _MotionOf(frame, cell);
      _CheckMotion(plane, cell, motionX, motionY);
      reference = plane.Buffer(this._bufferSelect ^ 1);
      referenceAt = plane.Origin + offset + motionY * plane.Pitch + motionX;
    }

    int primary;
    int secondary;
    if (mode is 1 or 4) {
      var pair = frame[_ALT_QUANT_OFFSET + tableIndex];
      primary = (pair >> 4) + this._codebookOffset;
      secondary = (pair & 0xF) + this._codebookOffset;
    } else {
      tableIndex += this._codebookOffset;
      primary = secondary = tableIndex;
    }

    if (primary >= Indeo3Tables.TABLE_COUNT || secondary >= Indeo3Tables.TABLE_COUNT)
      throw new InvalidDataException(
        $"An Indeo 3 cell names quantisation tables {primary} and {secondary}, where the codec defines {Indeo3Tables.TABLE_COUNT}.");

    var tables = new CellTables(
      Indeo3Tables.Tables[secondary], Indeo3Tables.Tables[primary],
      secondary >= Indeo3Tables.FIRST_SWAPPED_TABLE, primary >= Indeo3Tables.FIRST_SWAPPED_TABLE);

    // A prediction made at a finer quantiser than the one this cell is coded at can leave the seven
    // bits a sample has once a delta is added to it, so it is pulled onto this cell's own grid first.
    if (tableIndex >= 8 && reference != null) {
      var requantise = Indeo3Tables.Requantise[tableIndex & 7];
      var columns = cell.Width << 2;
      for (var x = 0; x < columns; ++x)
        reference[referenceAt + x] = requantise[reference[referenceAt + x] & 127];
    }

    switch (mode) {
      case 0:
      case 1:
      case 3:
      case 4:
        if (mode >= 3 && cell.MotionVector >= 0)
          throw new InvalidDataException($"An Indeo 3 motion-compensated cell is coded in mode {mode}, which is intra-only.");

        this._DecodeCellData(
          frame, cell, destination, target, reference ?? destination, referenceAt,
          plane.Pitch, 0, mode >= 3 ? 1 : 0, mode, tables, ref at);
        break;
      case 10:
      case 11:
        if (mode == 11 && cell.MotionVector < 0)
          throw new InvalidDataException("An Indeo 3 intra cell is coded in mode 11, which is for motion-compensated cells only.");

        this._DecodeCellData(
          frame, cell, destination, target, reference ?? destination, referenceAt,
          plane.Pitch, cell.MotionVector < 0 || mode == 10 ? 1 : 0, 1, mode, tables, ref at);
        break;
      default:
        throw new NotSupportedException($"An Indeo 3 cell is coded in mode {mode}, which the codec does not define.");
    }

    return at - start;
  }

  /// <summary>
  /// The one or two quantisation tables a cell is coded against, and how each reads its quad codes.
  /// </summary>
  /// <remarks>
  /// Two tables because modes 1 and 4 alternate between them down a block; every other mode names one
  /// table twice over. Which of the two a line uses is the line's own parity, and so is which swap
  /// applies — the swap follows the line and not the table, which only shows where the two differ.
  /// </remarks>
  private readonly record struct CellTables(
    Indeo3Tables.VqTable Even, Indeo3Tables.VqTable Odd, bool SwapEven, bool SwapOdd) {

    internal Indeo3Tables.VqTable For(int line, int mode)
      => mode <= 4 && (line & 1) == 0 ? this.Even : this.Odd;

    internal bool Swaps(int line) => (line & 1) == 0 ? this.SwapEven : this.SwapOdd;
  }

  /// <summary>
  /// Reads a cell's bytes and writes its samples.
  /// </summary>
  /// <remarks>
  /// One pass over the cell's blocks; every block either takes bytes of its own or is one of the blocks
  /// a previous run-length code already accounted for. A block's bytes are read four lines at a time,
  /// and a line is either a delta code — one table entry named directly, or two of them packed into one
  /// byte as a quad — or one of the escapes saying some number of lines, or of whole blocks, carry no
  /// change at all.
  /// </remarks>
  /// <param name="frame">The whole frame, which the cell's bytes are read out of.</param>
  /// <param name="cell">The cell being coded, in 4x4 blocks.</param>
  /// <param name="destination">The frame buffer the cell is written into.</param>
  /// <param name="target">Where in that buffer the cell's first sample sits.</param>
  /// <param name="reference">The buffer the cell predicts from, which for an intra cell is its own.</param>
  /// <param name="referenceAt">Where in that buffer the prediction's first sample sits.</param>
  /// <param name="rowOffset">The distance between two rows of either buffer.</param>
  /// <param name="horizontalZoom">One when a coded block covers eight columns rather than four.</param>
  /// <param name="verticalZoom">One when a coded line covers two rows, the second interpolated.</param>
  /// <param name="mode">The cell's coding mode, as its descriptor byte stated it.</param>
  /// <param name="tables">The quantisation tables the cell's lines alternate between.</param>
  /// <param name="at">Where in the frame reading is up to; left one past the cell's last byte.</param>
  private void _DecodeCellData(
    ReadOnlySpan<byte> frame, Cell cell, byte[] destination, int target, byte[] reference, int referenceAt,
    int rowOffset, int horizontalZoom, int verticalZoom, int mode, CellTables tables, ref int at) {
    if ((cell.Height & verticalZoom) != 0 || (cell.Width & horizontalZoom) != 0)
      throw new InvalidDataException(
        $"An Indeo 3 cell of {cell.Width}x{cell.Height} blocks is coded in mode {mode}, which codes them in pairs.");

    var blockRowOffset = (rowOffset << (2 + verticalZoom)) - (cell.Width << 2);
    var lineOffset = verticalZoom != 0 ? rowOffset : 0;
    var isIntra = cell.MotionVector < 0;

    // Only an intra cell in mode 10 both copies and averages eight samples at a time; a motion
    // compensated cell in mode 10 or 11 holds its prediction already and does nothing on a run.
    var wide = mode == 10 && isIntra;

    var runBlocks = 0;
    var skipping = false;
    var isFirstRow = true;

    for (var y = 0; y < cell.Height; isFirstRow = false, y += 1 + verticalZoom) {
      for (var x = 0; x < cell.Width; x += 1 + horizontalZoom) {
        var source = referenceAt;
        var into = target;

        if (runBlocks > 0) {
          if (mode <= 4) {
            if (!isIntra || !skipping)
              _CopyLines(destination, into, reference, source, rowOffset, 4 << verticalZoom);
          } else if (wide) {
            _RepeatBlockWide(destination, into, reference, source, rowOffset, isFirstRow);
          }

          --runBlocks;
          target += 4 << horizontalZoom;
          referenceAt += 4 << horizontalZoom;
          continue;
        }

        for (var line = 0; line < 4;) {
          var lines = 1;
          var isTopOfCell = isFirstRow && line == 0;
          var table = tables.For(line, mode);

          if (at >= this._lastByte)
            throw new InvalidDataException("An Indeo 3 cell reads past the end of its own plane.");

          int code = frame[at++];
          if (code < _RLE_FIRST) {
            int first;
            int second;
            if (code < table.DyadCount) {
              if (at >= this._lastByte)
                throw new InvalidDataException("An Indeo 3 cell reads past the end of its own plane.");

              first = frame[at++];
              second = code;
              if (first >= table.DyadCount || first >= _RLE_FIRST)
                throw new InvalidDataException($"An Indeo 3 cell names delta {first} of the {table.DyadCount} its table holds.");
            } else {
              var quad = code - table.DyadCount;
              first = quad / table.QuadDivisor;
              second = quad % table.QuadDivisor;
              if (tables.Swaps(line))
                (first, second) = (second, first);
            }

            if (mode <= 4)
              _ApplyNarrow(destination, into, reference, source, rowOffset, lineOffset, table, first, second, mode, isTopOfCell, cell.YPos);
            else if (wide)
              _ApplyWide(destination, into, reference, source, rowOffset, table, first, second, isTopOfCell, cell.YPos);
            else
              _ApplyInPlace(destination, into, rowOffset, table, first, second, mode);
          } else {
            switch (code) {
              case _RLE_REST_AND_NEXT:
                skipping = false;
                runBlocks = 1;
                code = _RLE_REST_OF_BLOCK;
                goto case _RLE_REST_OF_BLOCK;
              case _RLE_TO_SECOND_LINE:
              case _RLE_TO_THIRD_LINE:
              case _RLE_REST_OF_BLOCK:
                lines = 257 - code - line;
                if (lines <= 0)
                  throw new InvalidDataException($"An Indeo 3 cell states escape {code:X2} on line {line}, where it may not stand.");

                if (mode <= 4)
                  _CopyLines(destination, into, reference, source, rowOffset, lines << verticalZoom);
                else if (wide)
                  _RepeatLinesWide(destination, into, reference, source, rowOffset, lines, isTopOfCell);

                break;
              case _RLE_BLOCK_RUN:
                if (at >= this._lastByte)
                  throw new InvalidDataException("An Indeo 3 cell reads past the end of its own plane.");

                var counter = frame[at++];
                runBlocks = (counter & 0x1F) - 1;
                if (counter >= 64 || runBlocks < 0)
                  throw new InvalidDataException($"An Indeo 3 block run states a counter of {counter}, which names no run.");

                skipping = (counter & 0x20) != 0;
                lines = 4 - line;
                if (mode >= 10 || !isIntra || !skipping) {
                  if (mode <= 4)
                    _CopyLines(destination, into, reference, source, rowOffset, lines << verticalZoom);
                  else if (wide)
                    _RepeatLinesWide(destination, into, reference, source, rowOffset, lines, isTopOfCell);
                }

                break;
              case _RLE_SKIP_AND_NEXT:
                skipping = true;
                runBlocks = 1;
                goto case _RLE_SKIP_BLOCK;
              case _RLE_SKIP_BLOCK:
                if (line != 0)
                  throw new InvalidDataException($"An Indeo 3 cell states escape {code:X2} on line {line}, where it may only open a block.");

                lines = 4;
                if (!isIntra && mode <= 4)
                  _CopyLines(destination, into, reference, source, rowOffset, lines << verticalZoom);

                break;
              default:
                throw new NotSupportedException($"An Indeo 3 cell states escape {code:X2}, which the codec does not define.");
            }
          }

          line += lines;
          source += rowOffset * (lines << verticalZoom);
          into += rowOffset * (lines << verticalZoom);
        }

        target += 4 << horizontalZoom;
        referenceAt += 4 << horizontalZoom;
      }

      target += blockRowOffset;
      referenceAt += blockRowOffset;
    }
  }

  // ============================================================================================
  // Writing samples
  // ============================================================================================

  /// <summary>Copies four columns of a block straight across, for the given number of rows.</summary>
  private static void _CopyLines(byte[] destination, int into, byte[] reference, int source, int rowOffset, int rows) {
    for (var y = 0; y < rows; ++y)
      Array.Copy(reference, source + y * rowOffset, destination, into + y * rowOffset, 4);
  }

  /// <summary>
  /// Applies two delta pairs to a 4-wide block's line, and where the mode codes two rows at a time
  /// fills in the row it skipped.
  /// </summary>
  /// <remarks>
  /// Two writes, one addition each on a pair of samples held as one 16-bit word — which is what the
  /// packing in <see cref="Indeo3Tables"/> is for. The skipped row is the mean of the reference and the
  /// row just written, except on the very top row of the picture, where there is nothing above to
  /// average with and the row below is repeated instead.
  /// </remarks>
  private static void _ApplyNarrow(
    byte[] destination, int into, byte[] reference, int source, int rowOffset, int lineOffset,
    Indeo3Tables.VqTable table, int first, int second, int mode, bool isTopOfCell, int cellY) {
    _Write16(destination, into + lineOffset, (_Read16(reference, source) + table.Deltas[first]) & 0x7F7F);
    _Write16(destination, into + lineOffset + 2, (_Read16(reference, source + 2) + table.Deltas[second]) & 0x7F7F);

    if (mode < 3)
      return;

    if (isTopOfCell && cellY == 0)
      Array.Copy(destination, into + rowOffset, destination, into, 4);
    else
      _Average32(destination, into, reference, source, destination, into + rowOffset);
  }

  /// <summary>
  /// Applies two delta pairs to an 8-wide block's line and interpolates the row it skipped.
  /// </summary>
  /// <remarks>
  /// The deltas are the same numbers with each one doubled up, so one 32-bit addition covers four
  /// samples. On the top line of a cell the reference row was itself coded at half this resolution, so
  /// it is stretched back out before the delta lands on it.
  /// </remarks>
  private static void _ApplyWide(
    byte[] destination, int into, byte[] reference, int source, int rowOffset,
    Indeo3Tables.VqTable table, int first, int second, bool isTopOfCell, int cellY) {
    var left = _Read32(reference, source);
    var right = _Read32(reference, source + 4);
    if (isTopOfCell) {
      left = _Replicate32(left);
      right = _Replicate32(right);
    }

    _Write32(destination, into + rowOffset, (left + (uint)table.WideDeltas[first]) & 0x7F7F7F7Fu);
    _Write32(destination, into + rowOffset + 4, (right + (uint)table.WideDeltas[second]) & 0x7F7F7F7Fu);

    if (isTopOfCell && cellY == 0)
      Array.Copy(destination, into + rowOffset, destination, into, 8);
    else
      _Average64(destination, into, reference, source, destination, into + rowOffset);
  }

  /// <summary>Applies two delta pairs to a motion-compensated cell, which already holds its prediction.</summary>
  private static void _ApplyInPlace(
    byte[] destination, int into, int rowOffset, Indeo3Tables.VqTable table, int first, int second, int mode) {
    if (mode == 10) {
      _Write32(destination, into, (_Read32(destination, into) + (uint)table.WideDeltas[first]) & 0x7F7F7F7Fu);
      _Write32(destination, into + 4, (_Read32(destination, into + 4) + (uint)table.WideDeltas[second]) & 0x7F7F7F7Fu);
      _Write32(destination, into + rowOffset, (_Read32(destination, into + rowOffset) + (uint)table.WideDeltas[first]) & 0x7F7F7F7Fu);
      _Write32(destination, into + rowOffset + 4, (_Read32(destination, into + rowOffset + 4) + (uint)table.WideDeltas[second]) & 0x7F7F7F7Fu);
      return;
    }

    _Write16(destination, into, (_Read16(destination, into) + table.Deltas[first]) & 0x7F7F);
    _Write16(destination, into + 2, (_Read16(destination, into + 2) + table.Deltas[second]) & 0x7F7F);
    _Write16(destination, into + rowOffset, (_Read16(destination, into + rowOffset) + table.Deltas[first]) & 0x7F7F);
    _Write16(destination, into + rowOffset + 2, (_Read16(destination, into + rowOffset + 2) + table.Deltas[second]) & 0x7F7F);
  }

  /// <summary>Repeats a whole 8x8 block down from its reference row.</summary>
  private static void _RepeatBlockWide(
    byte[] destination, int into, byte[] reference, int source, int rowOffset, bool isFirstRow) {
    var row = _Read64(reference, source);
    if (!isFirstRow) {
      _Fill64(destination, into, row, 8, rowOffset);
      return;
    }

    _Fill64(destination, into + rowOffset, _Replicate64(row), 7, rowOffset);
    _Average64(destination, into, reference, source, destination, into + rowOffset);
  }

  /// <summary>Repeats a reference row down some number of an 8-wide block's coded lines.</summary>
  private static void _RepeatLinesWide(
    byte[] destination, int into, byte[] reference, int source, int rowOffset, int lines, bool isTopOfCell) {
    var row = _Read64(reference, source);
    if (!isTopOfCell) {
      _Fill64(destination, into, row, lines << 1, rowOffset);
      return;
    }

    _Fill64(destination, into + rowOffset, _Replicate64(row), (lines << 1) - 1, rowOffset);
    _Average64(destination, into, reference, source, destination, into + rowOffset);
  }

  /// <summary>
  /// Writes the mean of two rows of four samples, without rounding.
  /// </summary>
  /// <remarks>
  /// Four samples at once as one 32-bit word: no sum of two seven-bit samples can leave a byte, so the
  /// lanes stay apart, and the one bit the shift moves between them is what the mask takes off again.
  /// </remarks>
  private static void _Average32(byte[] destination, int into, byte[] a, int atA, byte[] b, int atB)
    => _Write32(destination, into, ((_Read32(a, atA) + _Read32(b, atB)) >> 1) & 0x7F7F7F7Fu);

  /// <summary>Writes the mean of two rows of eight samples, without rounding.</summary>
  private static void _Average64(byte[] destination, int into, byte[] a, int atA, byte[] b, int atB)
    => _Write64(destination, into, ((_Read64(a, atA) + _Read64(b, atB)) >> 1) & 0x7F7F7F7F7F7F7F7FUL);

  /// <summary>Repeats each even sample of four over the odd one after it.</summary>
  private static uint _Replicate32(uint row) {
    row &= 0x00FF00FFu;
    return row | (row << 8);
  }

  /// <summary>Repeats each even sample of eight over the odd one after it.</summary>
  private static ulong _Replicate64(ulong row) {
    row &= 0x00FF00FF00FF00FFUL;
    return row | (row << 8);
  }

  private static void _Fill64(byte[] destination, int into, ulong row, int rows, int rowOffset) {
    for (var y = 0; y < rows; ++y)
      _Write64(destination, into + y * rowOffset, row);
  }

  private static int _Read16(byte[] data, int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));

  private static void _Write16(byte[] data, int at, int value)
    => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at), (ushort)value);

  private static uint _Read32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));

  private static void _Write32(byte[] data, int at, uint value)
    => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);

  private static ulong _Read64(byte[] data, int at) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(at));

  private static void _Write64(byte[] data, int at, ulong value)
    => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(at), value);
}

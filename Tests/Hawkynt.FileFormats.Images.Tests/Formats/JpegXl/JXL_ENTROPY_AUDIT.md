# JPEG XL Entropy Decoder Audit

Audit target: `FileFormat.JpegXl/Codec/JxlEntropyDecoder.cs`,
`FileFormat.JpegXl/Codec/JxlAnsDecoder.cs`,
`FileFormat.JpegXl/Codec/AnsDistribution.cs` (~57+318+258 LOC).

Reference: ISO/IEC 18181-1 §C and libjxl `lib/jxl/dec_ans.{h,cc}`,
`lib/jxl/dec_huffman.cc`, `lib/jxl/dec_context_map.cc`, `lib/jxl/ans_params.h`.

---

## 1. Summary

The entropy decoder is **fundamentally broken** in its current form and will
not decode any real JPEG XL bitstream. The high-level skeleton (read LZ77
flag → read use_prefix_code → read cluster map → read hybrid-int config →
read distributions → init rANS) is in roughly the correct order and the
top-level field names/types match libjxl, but virtually every leaf-level
algorithm — hybrid integer config reading, hybrid integer decoding,
cluster-map decoding, prefix-code reading, distribution decoding, the rANS
state-update formula and renormalization width — disagrees with the spec.
The bit reader (`JxlBitReader`) is the only component that looks correct.

---

## 2. Confirmed correct

- **`JxlBitReader`** (whole file): LSB-first bit ordering, 56-bit refill
  pattern, `ReadU32(c0,u0,c1,u1,c2,u2,c3,u3)` semantics, `ReadU64`,
  `ZeroPadToByte` are all spec-faithful.
- **rANS state init** (`JxlAnsDecoder.cs:25`): reads 32 bits LSB-first
  immediately after distributions are parsed. Matches libjxl
  `state_ = ReadFixedBits<32>()` in `dec_ans.h`.
- **rANS final-state signature** (`JxlAnsDecoder.cs:13,56`):
  `_InitialState = 0x130000` matches `ANS_SIGNATURE (0x13) << 16` from
  `lib/jxl/ans_params.h`.
- **rANS renormalization condition and width** (`JxlAnsDecoder.cs:46-47`):
  threshold `1 << 16` and 16-bit refill match
  `state_ < (1u << 16u)` / `PeekFixedBits<16>()` in libjxl.
- **`use_prefix_code` flag** (`JxlEntropyDecoder.cs:74`): single bit,
  matches `code->use_prefix_code = ReadFixedBits<1>()`.
- **LZ77 enable flag and U32 distributions for `min_symbol`/`min_length`**
  (`JxlEntropyDecoder.cs:66,69-70`): `(224,0,512,0,4096,0,8,15)` and
  `(3,0,4,0,5,2,9,8)` exactly match libjxl `LZ77Params::VisitFields`
  (`Val(224), Val(512), Val(4096), BitsOffset(15,8)` and
  `Val(3), Val(4), BitsOffset(2,5), BitsOffset(8,9)`).
- **`log_alpha_size` reading when use_prefix_code is false**
  (`JxlEntropyDecoder.cs:119`): `5 + ReadBits(2)` matches libjxl
  `code->log_alpha_size = ReadFixedBits<2>() + 5`.
- **`num_clusters` U32 specifier** (`JxlEntropyDecoder.cs:79`):
  `(1,0,2,0,3,0,1,6)` matches the spec — needs verification against the
  formal spec §C.4 wording but agrees with `DecodeContextMap` behaviour.
- **`numClusters > numContexts` clamp** (`JxlEntropyDecoder.cs:81-82`):
  matches the spec invariant.

---

## 3. Bugs found

### 3.1 BLOCKING — Hybrid-int config (`split_exponent`/`msb`/`lsb`) read with the wrong U32 specifier
- **Location**: `JxlEntropyDecoder.cs:94-97`
- **Current code**:
  - `splitExponent[c] = ReadU32(0, 0, 4, 0, 8, 0, 0, 4)`
  - `msb[c] = ReadU32(0, 0, 1, 0, 2, 0, 0, 3)`
  - `lsb[c] = ReadU32(0, 0, 1, 0, 2, 0, 0, 3)`
- **Spec / libjxl** (`dec_ans.cc::DecodeUintConfig`):
  - `split_exponent = ReadBits(CeilLog2Nonzero(log_alpha_size + 1))`
  - `msb_in_token  = ReadBits(CeilLog2Nonzero(split_exponent + 1))`
  - `lsb_in_token  = ReadBits(CeilLog2Nonzero(split_exponent - msb + 1))`
  - Plus: `if (split_exponent == log_alpha_size)` skip msb/lsb (they are
    forced to 0).
- **Severity**: Blocking. Every cluster's hybrid-int config will be wrong,
  and the bit position will desync immediately after the first cluster.

### 3.2 BLOCKING — Hybrid-integer decode formula is wrong
- **Location**: `JxlEntropyDecoder.cs:206-222` (`_ReadHybridInt`)
- **Current code**:
  ```
  nExtra = split + ((token - (1 << split)) >> (split > 0 ? split - 1 : 0));
  return (token << nExtra) | extra;
  ```
  Note that `_msb`/`_lsb` are read at lines 216-217 but **never used** in the
  reconstruction.
- **Spec / libjxl** (`dec_ans.h::ReadHybridUintConfig`):
  ```
  nbits = split_exponent - (msb + lsb) + ((token - split_token) >> (msb + lsb));
  low   = token & ((1 << lsb) - 1);
  token >>= lsb;
  bits  = ReadBits(nbits);
  ret   = ((((1 << msb) | (token & ((1 << msb)-1))) << nbits) | bits) << lsb | low;
  ```
- **Severity**: Blocking. Even with the correct token, the reconstructed
  integer will be wrong for any value ≥ split_token.

### 3.3 BLOCKING — rANS decode formula uses wrong shift and per-distribution `cumFreq`
- **Location**: `JxlAnsDecoder.cs:36-43`
- **Current code**:
  ```
  index   = state & (tableSize - 1);          // tableSize = 1 << logBucketSize
  symbol  = dist.Symbols[index];
  freq    = dist.Frequencies[symbol];
  cumFreq = dist.CumulativeFreqs[symbol];
  state   = freq * (state >> logBucket) + (state & mask) - cumFreq;
  ```
- **Spec / libjxl** (`dec_ans.h::ReadSymbolANSWithoutRefill`):
  ```
  res     = state & (ANS_TAB_SIZE - 1u);      // ANS_TAB_SIZE = 4096 always
  symbol  = AliasTable::Lookup(...);          // returns {value, freq, offset}
  state   = symbol.freq * (state >> ANS_LOG_TAB_SIZE) + symbol.offset;
  ```
  `ANS_LOG_TAB_SIZE` is always **12** (`ANS_TAB_SIZE = 4096`), independent
  of `log_alpha_size`. The `offset` is computed during alias-table
  construction and already includes the cumulative-frequency subtraction —
  it is not the same as `(state & mask) - cumFreq`.
- **Severity**: Blocking. The total table size is always 4096 in JXL, but
  `JxlEntropyDecoder.cs:120` sets `logBucketSize = min(logAlphaSize, 8)`,
  giving 256 max — completely wrong. State update uses the wrong shift and
  the wrong offset semantics.

### 3.4 BLOCKING — `log_bucket_size` is conflated with `log_alpha_size`
- **Location**: `JxlEntropyDecoder.cs:120`,
  `AnsDistribution.cs:13-16` (`LogBucketSize`/`TableSize`)
- **Current code**: `logBucketSize = Math.Min(logAlphaSize, 8)`, used as
  the *table* size for rANS decoding.
- **Spec**: For rANS, the alias-table total size is fixed at
  `ANS_TAB_SIZE = 1 << 12 = 4096`. The per-distribution
  `log_bucket_size` controls how many alias-table entries each *bucket*
  holds (`log_entry_size = ANS_LOG_TAB_SIZE - log_alpha_size`), not the
  whole table. Sum of all symbol frequencies must equal 4096 (the
  distribution-precision invariant).
- **Severity**: Blocking. Distributions allocate the wrong number of
  slots, frequencies sum to the wrong total, and rANS state cycles wrong.

### 3.5 BLOCKING — Distribution decoder (`AnsDistribution.Read`) does not implement spec §C.3
- **Location**: `AnsDistribution.cs:43-69` (`Read`),
  `_ReadExplicitFrequencies` (lines 191-227)
- **Current code**: Reads two flag bits and dispatches to single-symbol /
  two-symbol / flat / "explicit" frequency table where each frequency is
  `Log2Ceil(remaining+1)` raw bits with last-symbol-takes-remainder.
- **Spec §C.3 / libjxl `DecodeANSCodes`**: The mode flags are different
  (3 modes encoded as `(b1, b1?b2:_)` form: `0→explicit`,
  `10→flat`, `11→simple` where simple itself splits into 1-4 symbols).
  The "explicit" branch reads `shift = ReadBits(...)` then for each
  symbol reads a *hybrid-uint-coded* frequency (using a small fixed
  prefix code over symbol counts), with separate sentinel for run-length
  patterns and a Pareto-style shift-adjustment for highly skewed
  symbols. None of this is implemented.
- **Severity**: Blocking. Will not parse any real distribution.

### 3.6 BLOCKING — Alias table builder is a degenerate fill, not the JXL alias algorithm
- **Location**: `AnsDistribution.cs:233-244` (`_BuildAliasTable`)
- **Current code**: Walks symbols in order, assigning each the next
  `frequencies[s]` consecutive slots, storing `j` as the "offset" and
  the symbol's frequency as the "cutoff". This is a plain
  cumulative-frequency table, not an alias table.
- **Spec / libjxl** (`ans_common.cc::InitAliasTable`): Two-stack
  redistribution algorithm where each bucket holds at most two symbols
  (the "primary" up to `cutoff`, "secondary" above), with `freq0`,
  `freq1`, and `offset1` resolving the secondary lookup. The decoder
  reads `bucket = res >> log_entry_size`; `pos_in_bucket = res &
  (entry_size-1)`; `if (pos_in_bucket < cutoff) symbol = primary` else
  `symbol = secondary; offset = pos_in_bucket - cutoff + offset1`.
- **Severity**: Blocking. Even if the formula in §3.3 were corrected,
  it would lookup garbage offsets.

### 3.7 BLOCKING — Cluster map decoder is non-conformant
- **Location**: `JxlEntropyDecoder.cs:270-283` (`_ReadClusterMap`)
- **Current code**: Reads `ceil(log2(numClusters))` raw bits per context.
- **Spec §C.4 / libjxl `DecodeContextMap`**:
  1. `is_simple = ReadBits(1)`.
  2. If simple: `bits_per_entry = ReadBits(2)`; if 0 → all zero, else
     each entry = `ReadBits(bits_per_entry)`. (Note: 2 bits, not
     `log2_ceil`.)
  3. Else: `use_mtf = ReadBits(1)`; recursively decode an entropy code
     (with `disallow_lz77 = numContexts ≤ 2`); read each entry as a
     hybrid-uint via `ReadHybridUint`; if `use_mtf`, apply
     `InverseMoveToFrontTransform` to the result.
- **Severity**: Blocking. Both simple and complex cases are wrong.

### 3.8 BLOCKING — Prefix-code reader is non-conformant
- **Location**: `JxlEntropyDecoder.cs:285-305` (`_ReadPrefixCode`)
- **Current code**: Reads alphabet size via `ReadU32(2,0,4,0,8,0,1,7)+1`,
  then 4 raw bits per code length.
- **Spec §C.5 / libjxl `dec_huffman.cc`**:
  1. `simple_code_or_skip = ReadBits(2)`.
  2. If `==1` → simple code: `nsym = ReadBits(2) + 1` (1..4),
     each symbol `ReadBits(max_bits)`, optional 5th when `nsym==4`,
     fixed bit lengths assigned by `nsym`.
  3. Else (0/2/3) → the 2-bit value is the number of *skipped*
     code-length-code-lengths; remaining 18-skip code-length-code-lengths
     are read as 3-bit values; then a static-Huffman-coded sequence of
     symbol code lengths over alphabet {0..15, 16=repeat-prev,
     17=repeat-zero}, with extra bits for run lengths.
- **Severity**: Blocking. Will desynchronise instantly on any non-trivial
  alphabet.

### 3.9 BLOCKING — Symbol-index reader uses wrong width
- **Location**: `AnsDistribution.cs:99-105` (`_ReadSymbolIndex`)
- **Current code**: `ReadBits(Log2Ceil(alphabetSize))`.
- **Spec / libjxl**: The single-/two-symbol distribution branches read
  the symbol index as a fixed `log_alpha_size`-bit value (5..8 bits),
  not `ceil(log2 alphabetSize)`. Off-by-one when `alphabetSize` is not
  a power of two.
- **Severity**: Blocking (within the §C.3 path; depends on §3.5 fix).

### 3.10 IMPORTANT — Two-symbol distribution mode reads wrong field
- **Location**: `AnsDistribution.cs:51-56`
- **Current code**: `if (ReadBool()) { sym0,sym1; freqBits = ReadBits(4); freq0 = freqBits+1; }`
- **Spec**: Two-symbol mode has `freq0 = ReadBits(log_precision)` (i.e.
  12 bits for ANS_LOG_TAB_SIZE=12), not 4 bits. Resulting frequencies
  must sum to 4096.
- **Severity**: Important / Blocking — the `_ReadExplicitFrequencies`
  path is used so often that this branch may not always trigger, but
  when it does it is wrong.

### 3.11 IMPORTANT — `_ReadPrefixSymbol` is O(L·N) per symbol and re-derives canonical codes from scratch
- **Location**: `JxlEntropyDecoder.cs:224-268`
- **Issue**: `_GetCanonicalCode` is recomputed on every probe for every
  candidate symbol on every bit length 1..15 — quadratic in the alphabet
  size with a quadratic factor inside. Even if it produced the right
  codes, the canonical-code derivation in `_GetCanonicalCode` is itself
  buggy: it walks `i = 0..symbolIndex`, alternately incrementing and
  shifting, in a way that does not produce DEFLATE/RFC1951-style
  canonical codes for arbitrary length tables (it conflates `prevLen`
  updates and the `if (i < symbolIndex)` look-ahead). It only
  coincidentally works for already-sorted-by-length tables.
- **Severity**: Important. Standard fix is a single
  `BuildCanonicalCodes(lengths) → codes[]` pass plus a lookup table.

### 3.12 IMPORTANT — LZ77 is parsed but not honored
- **Location**: `JxlEntropyDecoder.cs:23-27, 184-188`
- **Issue**: `_lz77RepeatCount` and `_lz77RepeatValue` are declared (with
  `#pragma warning disable CS0649` because they are *never assigned*).
  `lz77Enabled` triggers reading of `min_symbol`/`min_length` but the
  values are discarded into `_minSymbol`/`_minLength` locals on lines
  69-70 and never stored.
  Also: when `use_prefix_code` is true, the alphabet size for prefix
  codes does not include the LZ77 marker symbols, so `min_symbol` /
  `min_length` are essential parameters that must persist.
- **Severity**: Important. As-is, any stream with `lz77_enabled=true`
  and any LZ77 token in the data will produce silent corruption.

### 3.13 MINOR — Single-symbol distribution path leaves alias table empty
- **Location**: `AnsDistribution.cs:107-129` (`_BuildSingleSymbol`)
- **Current code**: `Array.Fill(symbols, symbol)` but `offsets` and
  `cutoffs` left at zero, then `_state = freq * (state >> logBucket) + 0
  - cumFreq` in the decoder. `cumFreq` for the single hot symbol is 0,
  freq is `tableSize`, so the formula degenerates to
  `state = tableSize * (state >> logBucket) + (state & mask)`, which by
  coincidence works only because the alias table is degenerate. Fragile
  but not currently incorrect.
- **Severity**: Minor / latent. Becomes incorrect after fixing §3.4.

### 3.14 MINOR — `numContexts > 1 && numClusters > 1` guard order
- **Location**: `JxlEntropyDecoder.cs:86`
- **Issue**: Spec only requires the cluster-map block when
  `num_contexts > 1`; cluster count is then signalled. The check is
  fine, but `numContexts > 1` already implies the only relevant case;
  the redundant `&&` is a minor style issue.
- **Severity**: Minor.

### 3.15 MINOR — `CreateSimple` is dead test scaffolding
- **Location**: `JxlEntropyDecoder.cs:149-177`
- **Issue**: Constructs a flat-bit-width "prefix" code that is not a
  prefix code at all. Harmless for production but fragile if ever used
  in tests as ground truth.
- **Severity**: Minor.

---

## 4. Missing features

| # | Feature | Severity | Notes |
|---|---------|----------|-------|
| M1 | Conditional skip of msb/lsb when `split_exponent == log_alpha_size` | Blocking | Required by libjxl `DecodeUintConfig` |
| M2 | Move-to-front inverse transform for cluster maps | Blocking | §C.4; needs ~15 LOC |
| M3 | Recursive entropy decode for cluster map (`disallow_lz77`) | Blocking | §C.4; reuses the entropy decoder |
| M4 | Distribution-mode dispatch (`0→explicit`, `10→flat`, `11→simple`) | Blocking | §C.3 |
| M5 | Pareto / shift-adjustment in explicit frequency mode | Blocking | §C.3 |
| M6 | Run-length sentinel (`kANSRleSymbol`) inside explicit frequencies | Blocking | §C.3 |
| M7 | Two-stack alias-table build with cutoff/offset1 | Blocking | `ans_common.cc::InitAliasTable` |
| M8 | Static Huffman code-length-code-lengths table for prefix codes | Blocking | `dec_huffman.cc` |
| M9 | Simple-prefix-code shortcut for ≤4 (or 5) symbols | Blocking | §C.5 |
| M10 | Repeat codes 16/17 + extra bits in prefix-code length stream | Blocking | §C.5 |
| M11 | LZ77 marker handling (`min_symbol`, `min_length`, distance decode) | Important | §C.6; rest of decoder must store these |
| M12 | `disallow_lz77` plumbing for the recursive cluster-map case | Important | §C.4 + §C.6 |
| M13 | Final-state validation called from the format reader | Minor | `CheckFinalState` exists at `JxlAnsDecoder.cs:56` but no caller invokes it |
| M14 | Alphabet size for prefix codes from log_alpha_size+something rather than U32(2,0,4,0,8,0,1,7) | Needs verification | The U32 specifier in `_ReadPrefixCode` is suspicious — flag for double-check against §C.5 |

---

## 5. Recommendation (ordered)

Given the depth of the divergence, the right sequencing is:

1. **Replace the alias-table builder and rANS state update**
   (~60 LOC in `AnsDistribution.cs`, ~10 LOC in `JxlAnsDecoder.cs`).
   Pin `ANS_LOG_TAB_SIZE = 12` and remove `LogBucketSize` from
   `AnsDistribution`. This is the foundation; everything else depends
   on it.

2. **Rewrite `_ReadHybridInt`** to match
   `ReadHybridUintConfig` (~15 LOC in `JxlEntropyDecoder.cs:206-222`).
   Remove the `_msb` / `_lsb` "unused variable" smell.

3. **Rewrite hybrid-int config reading** in
   `JxlEntropyDecoder.cs:90-99` to use
   `ReadBits(CeilLog2Nonzero(...))` chain plus the
   `split_exponent == log_alpha_size` short-circuit (~12 LOC).

4. **Rewrite `AnsDistribution.Read`** to implement the §C.3
   3-mode dispatch + shift-adjusted explicit frequencies + RLE sentinel
   (~120 LOC; this is the biggest single chunk).

5. **Rewrite `_ReadClusterMap`** to support both simple and
   full (MTF-coded, recursive-entropy) variants (~50 LOC + reuse of
   the entropy decoder for the recursive case).

6. **Rewrite `_ReadPrefixCode`** to implement the
   simple-prefix shortcut and the static-Huffman-coded length stream
   with repeat codes (~80 LOC).

7. **Wire up LZ77** properly:
   persist `min_symbol` and `min_length`, implement the LZ77 token
   handling in `ReadInt`, and decode distances when a LZ77 marker is
   produced (~50 LOC + a small ring buffer).

8. **Speed up `_ReadPrefixSymbol`** with a precomputed
   canonical-code table (lookup-by-prefix-bits) and remove
   `_GetCanonicalCode` (~30 LOC).

9. Call `CheckFinalState` at the end of every entropy block at the
   reader level (~3 LOC + a couple of test cases).

Total estimated rework: ~400-450 LOC of decoder code plus tests. The bit
reader and the high-level Read() control flow can be retained
unchanged.

---

## Notes on uncertainty

- The exact ordering of "read num_clusters" vs "read use_prefix_code"
  was not re-verified against the formal §C.4/§C.5 wording — the libjxl
  source orders them as `use_prefix_code → num_histograms` which
  matches `JxlEntropyDecoder.cs:74,79`. **Confirmed.**
- The `_ReadPrefixCode` alphabet-size U32 specifier `(2,0,4,0,8,0,1,7)`
  was not located in the libjxl excerpts I fetched (the simple/complex
  prefix path reads `simple_code_or_skip` first instead of an alphabet
  size). This is flagged in M14 as **needs verification** — but given
  the rest of `_ReadPrefixCode` is wrong anyway, the precise specifier
  is moot.
- The two-symbol distribution `freq0` width was inferred as
  `log_precision = ANS_LOG_TAB_SIZE = 12` from the invariant
  "frequencies sum to 4096"; **needs verification** against the formal
  §C.3 wording.

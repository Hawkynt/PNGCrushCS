# H.265 range extension fixtures

Small H.265 elementary streams (Annex B) that exercise the format range extension tools, each
listed in `expected.md5` with the MD5 of the output the HM reference decoder (HM-18.0,
`TAppDecoder -b <stream> -o out.yuv`) writes for it: cropped planes, Y then Cb then Cr, one byte per
sample when every plane is eight bits deep and two little-endian bytes otherwise. Every stream except
`CCP_8bit_RExt_QCOM.bit` and `IPCM_B_RExt_NEC.bit` also carries a decoded picture hash SEI message for
each picture.

## JCT-VC conformance bitstreams

From the RExt draft conformance suite the JCT-VC publishes for testing decoders against ITU-T H.265.1,
<https://www.itu.int/wftp3/av-arch/jctvc-site/bitstream_exchange/draft_conformance/RExt/>.

| File | Archive | Taken |
|---|---|---|
| `Bitdepth_A_RExt_Sony_1.bit` | `Bitdepth_A_RExt_Sony_1.zip` (`.bin`) | whole stream |
| `CCP_8bit_RExt_QCOM.bit` | `CCP_8bit_RExt_QCOM_1.zip` (`.bin`) | whole stream |
| `GENERAL_10b_422_RExt_Sony_1.bit` | `GENERAL_10b_422_RExt_Sony_1.zip` | whole stream |
| `GENERAL_16b_400_RExt_Sony_1.cut1.bit` | `GENERAL_16b_400_RExt_Sony_1.zip` | first coded picture: every NAL unit before the second picture's first slice |
| `IPCM_B_RExt_NEC.bit` | `IPCM_B_RExt_NEC.zip` | whole stream |
| `WAVETILES_RExt_Sony_2.seq4.bit` | `WAVETILES_RExt_Sony_2.zip` | fourth coded video sequence (dependent slice segments with tiles and wavefronts): from its video parameter set to the end |
| `WPP_AND_TILE_AND_CABAC_BYPASS_ALIGN_1_HIGH_TP_444_14BIT_RExt_Apple_2.bit` | same name `.zip` | whole stream |
| `WPP_AND_TILE_AND_CABAC_EXT_PREC_1_HIGH_TP_444_14BIT_RExt_Apple_2.bit` | same name `.zip` | whole stream |

## Streams written with the HM reference encoder

The suite has no stream that uses weighted prediction with `high_precision_offsets_enabled_flag`,
none that codes `cu_chroma_qp_offset_idx`, none with `cu_transquant_bypass_flag`, and no inter
pictures deeper than twelve bits. These four were written with HM-18.0's `TAppEncoder` from
`ffmpeg -f lavfi testsrc2=size=128x96:rate=25` pictures. For the two weighted prediction streams the
source was `testsrc2=size=128x96:rate=25,fade=in:0:40,select='gte(n\,20)'`, nine pictures, so that
the encoder has a brightness change to weight. The encoder's chroma QP offset list was extended
from its one hard-wired entry `(6, 6)` to four, adding `(-3, 4)`, `(2, -5)` and `(-12, 12)` in
`TEncTop::xInitPPS`, so that its coding units cycle through list indices.

| File | Configuration | Options |
|---|---|---|
| `wp_hp_12b_420.bit` | `encoder_lowdelay_main_rext.cfg`, 9 pictures | `--InputBitDepth=12 --InternalBitDepth=12 --InputChromaFormat=420 --WeightedPredP=1 --WeightedPredB=1 --HighPrecisionPredictionWeighting=1 --MaxCUChromaQpAdjustmentDepth=1 --SEIDecodedPictureHash=1 --TemporalFilter=0 --QP=30` |
| `wp_ep_16b_444.bit` | `encoder_lowdelay_main_rext.cfg`, 9 pictures | `--InputBitDepth=16 --InternalBitDepth=16 --InputChromaFormat=444 --ExtendedPrecision=1 --CrossComponentPrediction=1 --WeightedPredP=1 --WeightedPredB=1 --HighPrecisionPredictionWeighting=1 --MaxCUChromaQpAdjustmentDepth=1 --ImplicitResidualDPCM=1 --ExplicitResidualDPCM=1 --TransformSkipLog2MaxSize=3 --GolombRiceParameterAdaptation=1 --SEIDecodedPictureHash=1 --TemporalFilter=0 --QP=12` |
| `sao_align_16b_444.bit` | `encoder_intra_high_throughput_rext.cfg`, 2 pictures | `--InputBitDepth=16 --InternalBitDepth=16 --InputChromaFormat=444 --ExtendedPrecision=1 --AlignCABACBeforeBypass=1 --SaoLumaOffsetBitShift=3 --SaoChromaOffsetBitShift=2 --IntraReferenceSmoothing=0 --SEIDecodedPictureHash=1 --QP=4` |
| `lossless_12b_444.bit` | `encoder_lowdelay_main_rext.cfg`, 5 pictures | `--InputBitDepth=12 --InternalBitDepth=12 --InputChromaFormat=444 --TransquantBypassEnable=1 --CUTransquantBypassFlagForce=1 --ImplicitResidualDPCM=1 --ExplicitResidualDPCM=1 --ResidualRotation=1 --SingleSignificanceMapContext=1 --GolombRiceParameterAdaptation=1 --CrossComponentPrediction=1 --SEIDecodedPictureHash=1 --TemporalFilter=0 --QP=22` |

# H.265 screen content coding fixtures

Elementary streams that exercise the screen content coding tools — intra block copy through current
picture referencing, palette mode, the adaptive colour transform, integer motion vectors and
disabled intra boundary filtering — each listed in `expected-scc.md5` with the MD5 of the output the
HM-SCM reference decoder (HM-16.21+SCM-8.8, `TAppDecoder -b <stream> -o out.yuv`) writes for it:
cropped planes in coded order, one byte per sample when every plane is eight bits deep and two
little-endian bytes otherwise. Every stream except `DPB_Loop_Filters_A_MediaTek_2.bit` and
`IBF_Disabled_B_MediaTek_2.bit` also carries a decoded picture hash SEI message for each picture.

## JCT-VC conformance bitstreams

From the SCC draft conformance suite,
<https://www.itu.int/wftp3/av-arch/jctvc-site/bitstream_exchange/draft_conformance/SCC/>, each taken
whole from the archive of the same name. For every one of them the MD5 above is also the one the
archive states, except `Slice_ACT_QP_Offsets_A_Qualcomm_2.bit`, whose archive states the checksum of
the same pictures written with the planes in R, G, B order.

| File | What it covers |
|---|---|
| `Bipred_8by8_A_Qualcomm_2.bit` | 4:2:0 intra block copy and the 8x8 bi-prediction restriction |
| `DPB_Loop_Filters_A_MediaTek_2.bit` | a current picture kept in two versions while the loop filters switch on and off |
| `HT_B_SCC_Apple_3.bit` | 4:4:4 high-throughput coding: colour transform, palette escapes, tiles, CABAC bypass alignment |
| `IBF_Disabled_B_MediaTek_2.bit` | 4:2:0 with intra boundary filtering disabled and integer motion vectors per slice |
| `MVRESIDC_C_MS_3.bit` | `motion_vector_resolution_control_idc` 2, alternating integer and quarter-sample slices |
| `PPI_A_InterDigital_2.bit` | palette predictor initialisers in the sequence and picture parameter sets |
| `Slice_ACT_QP_Offsets_A_Qualcomm_2.bit` | colour transform quantiser offsets from the picture parameter set and the slice |
| `Zero_and_One_Palette_Size_A_Canon_2.bit` | palettes of no and of one colour |

## Streams written with the HM-SCM encoder

The suite has no palette escape or colour-transformed unit in a lossless coding unit, no 4:2:2 or
monochrome palette, no weighted prediction table beside a current picture reference, no colour
transform between components of different depths, and no palette predictor carried across
wavefront rows and dependent slice segments. These seven were written with HM-16.21+SCM-8.8's
`TAppEncoder`, configuration `encoder_lowdelay_main_scc.cfg`, from `ffmpeg -f lavfi
testsrc=size=128x96:rate=25` pictures in the named chroma format, with
`--SEIDecodedPictureHash=1 --TemporalFilter=0` throughout. The weighted prediction stream's source
was `testsrc=size=128x96:rate=25,fade=in:0:12`, nine pictures; the RGB stream's was
`testsrc2=size=64x64:rate=25` written as planar G, B, R.

| File | Pictures | Options |
|---|---|---|
| `scc_lossless_444.bit` | 3 | `--InputChromaFormat=444 --TransquantBypassEnable=1 --CUTransquantBypassFlagForce=1 --QP=22` |
| `scc_lossless_rgb_444.bit` | 2 | `-wdt 64 -hgt 64 --InputChromaFormat=444 --InputColourSpaceConvert=UNCHANGED --TransquantBypassEnable=1 --CUTransquantBypassFlagForce=1 --QP=22` |
| `scc_mixed_depth_444.bit` | 3 | `--InputChromaFormat=444 --InternalBitDepth=10 --InternalBitDepthC=8 --OutputBitDepth=10 --OutputBitDepthC=8 --QP=25` |
| `scc_palette_400.bit` | 3 | `--InputChromaFormat=400 --QP=27` |
| `scc_palette_422.bit` | 3 | `--InputChromaFormat=422 --QP=27` |
| `scc_wp_ibc_420.bit` | 9 | `--InputChromaFormat=420 --WeightedPredP=1 --WeightedPredB=1 --QP=30` |
| `scc_wpp_slices_420.bit` | 3 | `--InputChromaFormat=420 --WaveFrontSynchro=1 --SliceMode=1 --SliceArgument=5 --SliceSegmentMode=1 --SliceSegmentArgument=2 --MaxCUWidth=32 --MaxCUHeight=32 --MaxPartitionDepth=3 --QP=27` |

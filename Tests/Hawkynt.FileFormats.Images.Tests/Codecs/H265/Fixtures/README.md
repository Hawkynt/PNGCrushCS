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

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

#pragma warning disable CA1707 // 識別子はアンダースコアを含むことはできません
#pragma warning disable CA1815 // equals および operator equals を値型でオーバーライドします
#pragma warning disable CA1051 // 参照可能なインスタンス フィールドを宣言しません
#pragma warning disable CA1712 // 列挙値の前に型名を付けないでください
#pragma warning disable CA1711 // 識別子は、不適切なサフィックスを含むことはできません
#pragma warning disable CA1008 // 列挙型は 0 値を含んでいなければなりません
#pragma warning disable CA1700 // 列挙型値に 'Reserved' という名前を指定しません
#pragma warning disable CA1069 // 列挙値を重複させることはできない

namespace Momiji.Interop.H264
{
    /**
    * @brief Option types introduced in SVC encoder application
    */
    public enum ENCODER_OPTION : int
    {
        ENCODER_OPTION_DATAFORMAT = 0,
        ENCODER_OPTION_IDR_INTERVAL,               ///< IDR period,0/-1 means no Intra period (only the first frame); lager than 0 means the desired IDR period, must be multiple of (2^temporal_layer)
        ENCODER_OPTION_SVC_ENCODE_PARAM_BASE,      ///< structure of Base Param
        ENCODER_OPTION_SVC_ENCODE_PARAM_EXT,       ///< structure of Extension Param
        ENCODER_OPTION_FRAME_RATE,                 ///< maximal input frame rate, current supported range: MAX_FRAME_RATE = 30,MIN_FRAME_RATE = 1
        ENCODER_OPTION_BITRATE,
        ENCODER_OPTION_MAX_BITRATE,
        ENCODER_OPTION_INTER_SPATIAL_PRED,
        ENCODER_OPTION_RC_MODE,
        ENCODER_OPTION_RC_FRAME_SKIP,
        ENCODER_PADDING_PADDING,                   ///< 0:disable padding;1:padding

        ENCODER_OPTION_PROFILE,                    ///< assgin the profile for each layer
        ENCODER_OPTION_LEVEL,                      ///< assgin the level for each layer
        ENCODER_OPTION_NUMBER_REF,                 ///< the number of refererence frame
        ENCODER_OPTION_DELIVERY_STATUS,            ///< the delivery info which is a feedback from app level

        ENCODER_LTR_RECOVERY_REQUEST,
        ENCODER_LTR_MARKING_FEEDBACK,
        ENCODER_LTR_MARKING_PERIOD,
        ENCODER_OPTION_LTR,                        ///< 0:disable LTR;larger than 0 enable LTR; LTR number is fixed to be 2 in current encoder
        ENCODER_OPTION_COMPLEXITY,

        ENCODER_OPTION_ENABLE_SSEI,                ///< enable SSEI: true--enable ssei; false--disable ssei
        ENCODER_OPTION_ENABLE_PREFIX_NAL_ADDING,   ///< enable prefix: true--enable prefix; false--disable prefix
        ENCODER_OPTION_SPS_PPS_ID_STRATEGY, ///< different stategy in adjust ID in SPS/PPS: 0- constant ID, 1-additional ID, 6-mapping and additional

        ENCODER_OPTION_CURRENT_PATH,
        ENCODER_OPTION_DUMP_FILE,                  ///< dump layer reconstruct frame to a specified file
        ENCODER_OPTION_TRACE_LEVEL,                ///< trace info based on the trace level
        ENCODER_OPTION_TRACE_CALLBACK,             ///< a void (*)(void* context, int level, const char* message) function which receives log messages
        ENCODER_OPTION_TRACE_CALLBACK_CONTEXT,     ///< context info of trace callback

        ENCODER_OPTION_GET_STATISTICS,             ///< read only
        ENCODER_OPTION_STATISTICS_LOG_INTERVAL,    ///< log interval in millisecond

        ENCODER_OPTION_IS_LOSSLESS_LINK,            ///< advanced algorithmetic settings

        ENCODER_OPTION_BITS_VARY_PERCENTAGE        ///< bit vary percentage
    }
    /**
    * @brief Encoder usage type
    */
    public enum EUsageType : int
    { 
        CAMERA_VIDEO_REAL_TIME,      ///< camera video for real-time communication
        SCREEN_CONTENT_REAL_TIME,    ///< screen content signal
        CAMERA_VIDEO_NON_REAL_TIME,
        SCREEN_CONTENT_NON_REAL_TIME,
        INPUT_CONTENT_TYPE_ALL,
    }
    /**
    * @brief Enumulate the complexity mode
    */
    public enum ECOMPLEXITY_MODE : int
    {
        LOW_COMPLEXITY = 0,              ///< the lowest compleixty,the fastest speed,
        MEDIUM_COMPLEXITY,          ///< medium complexity, medium speed,medium quality
        HIGH_COMPLEXITY             ///< high complexity, lowest speed, high quality
    }
    /**
        * @brief Enumulate for the stategy of SPS/PPS strategy
        */
    public enum EParameterSetStrategy : int
    {
        CONSTANT_ID = 0,           ///< constant id in SPS/PPS
        INCREASING_ID = 0x01,      ///< SPS/PPS id increases at each IDR
        SPS_LISTING = 0x02,       ///< using SPS in the existing list if possible
        SPS_LISTING_AND_PPS_INCREASING = 0x03,
        SPS_PPS_LISTING = 0x06,
    }
    /**
    * @brief Enumerate the type of rate control mode
    */
    public enum RC_MODES : int
    {
        RC_QUALITY_MODE = 0,     ///< quality mode
        RC_BITRATE_MODE = 1,     ///< bitrate mode
        RC_BUFFERBASED_MODE = 2, ///< no bitrate control,only using buffer status,adjust the video quality
        RC_TIMESTAMP_MODE = 3, //rate control based timestamp
        RC_BITRATE_MODE_POST_SKIP = 4, ///< this is in-building RC MODE, WILL BE DELETED after algorithm tuning!
        RC_OFF_MODE = -1,         ///< rate control off mode
    }
    /**
    * @brief Enumerate the type of profile id
    */
    public enum EProfileIdc : int
    {
        PRO_UNKNOWN = 0,
        PRO_BASELINE = 66,
        PRO_MAIN = 77,
        PRO_EXTENDED = 88,
        PRO_HIGH = 100,
        PRO_HIGH10 = 110,
        PRO_HIGH422 = 122,
        PRO_HIGH444 = 144,
        PRO_CAVLC444 = 244,

        PRO_SCALABLE_BASELINE = 83,
        PRO_SCALABLE_HIGH = 86
    }
    /**
    * @brief Enumerate the type of level id
    */
    public enum ELevelIdc : int
    {
        LEVEL_UNKNOWN = 0,
        LEVEL_1_0 = 10,
        LEVEL_1_B = 9,
        LEVEL_1_1 = 11,
        LEVEL_1_2 = 12,
        LEVEL_1_3 = 13,
        LEVEL_2_0 = 20,
        LEVEL_2_1 = 21,
        LEVEL_2_2 = 22,
        LEVEL_3_0 = 30,
        LEVEL_3_1 = 31,
        LEVEL_3_2 = 32,
        LEVEL_4_0 = 40,
        LEVEL_4_1 = 41,
        LEVEL_4_2 = 42,
        LEVEL_5_0 = 50,
        LEVEL_5_1 = 51,
        LEVEL_5_2 = 52
    }

    /**
    * @brief Enumerate the type of wels log
    */
    [Flags]
    public enum WELS_LOG : int
    {
        WELS_LOG_QUIET = 0x00,          ///< quiet mode
        WELS_LOG_ERROR = 1 << 0,        ///< error log iLevel
        WELS_LOG_WARNING = 1 << 1,        ///< Warning log iLevel
        WELS_LOG_INFO = 1 << 2,        ///< information log iLevel
        WELS_LOG_DEBUG = 1 << 3,        ///< debug log, critical algo log
        WELS_LOG_DETAIL = 1 << 4,        ///< per packet/frame log
        WELS_LOG_RESV = 1 << 5,        ///< resversed log iLevel
        WELS_LOG_LEVEL_COUNT = 6,
        WELS_LOG_DEFAULT = WELS_LOG_WARNING   ///< default log iLevel in Wels codec
    };

    /**
    * @brief Enumerate the type of slice mode
    */
    public enum SliceModeEnum : int
    {
        SM_SINGLE_SLICE = 0, ///< | SliceNum==1
        SM_FIXEDSLCNUM_SLICE = 1, ///< | according to SliceNum        | enabled dynamic slicing for multi-thread
        SM_RASTER_SLICE = 2, ///< | according to SlicesAssign    | need input of MB numbers each slice. In addition, if other constraint in SSliceArgument is presented, need to follow the constraints. Typically if MB num and slice size are both constrained, re-encoding may be involved.
        SM_SIZELIMITED_SLICE = 3, ///< | according to SliceSize       | slicing according to size, the slicing will be dynamic(have no idea about slice_nums until encoding current frame)
        SM_RESERVED = 4
    }
    /**
    * @brief Enumerate the type of sample aspect ratio
    */
    public enum ESampleAspectRatio : int
    {
        ASP_UNSPECIFIED = 0,
        ASP_1x1 = 1,
        ASP_12x11 = 2,
        ASP_10x11 = 3,
        ASP_16x11 = 4,
        ASP_40x33 = 5,
        ASP_24x11 = 6,
        ASP_20x11 = 7,
        ASP_32x11 = 8,
        ASP_80x33 = 9,
        ASP_18x11 = 10,
        ASP_15x11 = 11,
        ASP_64x33 = 12,
        ASP_160x99 = 13,

        ASP_EXT_SAR = 255
    }
    /**
    * @brief Enumerate the type of video format
    */
    public enum EVideoFormatType : uint
    {
        videoFormatRGB = 1,             ///< rgb color formats
        videoFormatRGBA = 2,
        videoFormatRGB555 = 3,
        videoFormatRGB565 = 4,
        videoFormatBGR = 5,
        videoFormatBGRA = 6,
        videoFormatABGR = 7,
        videoFormatARGB = 8,

        videoFormatYUY2 = 20,            ///< yuv color formats
        videoFormatYVYU = 21,
        videoFormatUYVY = 22,
        videoFormatI420 = 23,            ///< the same as IYUV
        videoFormatYV12 = 24,
        videoFormatInternal = 25,            ///< only used in SVC decoder testbed

        videoFormatNV12 = 26,            ///< new format for output by DXVA decoding

        videoFormatVFlip = 0x80000000
    }

    /**
    * @brief Enumerate  video frame type
    */
    public enum EVideoFrameType : int
    {
        videoFrameTypeInvalid,    ///< encoder not ready or parameters are invalidate
        videoFrameTypeIDR,        ///< IDR frame in H.264
        videoFrameTypeI,          ///< I frame type
        videoFrameTypeP,          ///< P frame type
        videoFrameTypeSkip,       ///< skip the frame based encoder kernel
        videoFrameTypeIPMixed     ///< a frame where I and P slices are mixing, not supported yet
    }
    /**
    * @brief SVC Encoding Parameters
    */
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class SEncParamBase
    {
        public EUsageType iUsageType;   ///< application type; please refer to the definition of EUsageType
        public int iPicWidth;           ///< width of picture in luminance samples (the maximum of all layers if multiple spatial layers presents)
        public int iPicHeight;          ///< height of picture in luminance samples((the maximum of all layers if multiple spatial layers presents)
        public int iTargetBitrate;      ///< target bitrate desired, in unit of bps
        public RC_MODES iRCMode;        ///< rate control mode
        public float fMaxFrameRate;     ///< maximal input frame rate
    }
    /**
    * @brief Structure for slice argument
    */
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct SSliceArgument
    {
        public SliceModeEnum uiSliceMode;    ///< by default, uiSliceMode will be SM_SINGLE_SLICE
        public uint uiSliceNum;     ///< only used when uiSliceMode=1, when uiSliceNum=0 means auto design it with cpu core number
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 35)]
        public uint[] uiSliceMbNum; ///< only used when uiSliceMode=2; when =0 means setting one MB row a slice
        public uint uiSliceSizeConstraint; ///< now only used when uiSliceMode=4
    }
    /**
    * @brief  Structure for spatial layer configuration
    */
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct SSpatialLayerConfig
    {
        public int iVideoWidth;           ///< width of picture in luminance samples of a layer
        public int iVideoHeight;          ///< height of picture in luminance samples of a layer
        public float fFrameRate;            ///< frame rate specified for a layer
        public int iSpatialBitrate;       ///< target bitrate for a spatial layer, in unit of bps
        public int iMaxSpatialBitrate;    ///< maximum  bitrate for a spatial layer, in unit of bps
        public EProfileIdc uiProfileIdc;   ///< value of profile IDC (PRO_UNKNOWN for auto-detection)
        public ELevelIdc uiLevelIdc;     ///< value of profile IDC (0 for auto-detection)
        public int iDLayerQp;      ///< value of level IDC (0 for auto-detection)

        public SSliceArgument sSliceArgument;

        // Note: members bVideoSignalTypePresent through uiColorMatrix below are also defined in SWelsSPS in parameter_sets.h.
        public bool bVideoSignalTypePresent;  // false => do not write any of the following information to the header
        public byte uiVideoFormat;        // EVideoFormatSPS; 3 bits in header; 0-5 => component, kpal, ntsc, secam, mac, undef
        public bool bFullRange;         // false => analog video data range [16, 235]; true => full data range [0,255]
        public bool bColorDescriptionPresent; // false => do not write any of the following three items to the header
        public byte uiColorPrimaries;     // EColorPrimaries; 8 bits in header; 0 - 9 => ???, bt709, undef, ???, bt470m, bt470bg,
                                            //    smpte170m, smpte240m, film, bt2020
        public byte uiTransferCharacteristics;  // ETransferCharacteristics; 8 bits in header; 0 - 15 => ???, bt709, undef, ???, bt470m, bt470bg, smpte170m,
                                                //   smpte240m, linear, log100, log316, iec61966-2-4, bt1361e, iec61966-2-1, bt2020-10, bt2020-12
        public byte uiColorMatrix;        // EColorMatrix; 8 bits in header (corresponds to FFmpeg "colorspace"); 0 - 10 => GBR, bt709,
                                            //   undef, ???, fcc, bt470bg, smpte170m, smpte240m, YCgCo, bt2020nc, bt2020c

        public bool bAspectRatioPresent; ///< aspect ratio present in VUI
        public ESampleAspectRatio eAspectRatio; ///< aspect ratio idc
        public ushort sAspectRatioExtWidth; ///< use if aspect ratio idc == 255
        public ushort sAspectRatioExtHeight; ///< use if aspect ratio idc == 255
    }
    /**
    * @brief SVC Encoding Parameters extention
    */
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct SEncParamExt
    {
        public EUsageType iUsageType;         ///< same as in TagEncParamBase
        public int iPicWidth;                 ///< same as in TagEncParamBase
        public int iPicHeight;                ///< same as in TagEncParamBase
        public int iTargetBitrate;            ///< same as in TagEncParamBase
        public RC_MODES iRCMode;                   ///< same as in TagEncParamBase
        public float fMaxFrameRate;             ///< same as in TagEncParamBase

        public int iTemporalLayerNum;         ///< temporal layer number, max temporal layer = 4
        public int iSpatialLayerNum;          ///< spatial layer number,1<= iSpatialLayerNum <= MAX_SPATIAL_LAYER_NUM, MAX_SPATIAL_LAYER_NUM = 4
        public SSpatialLayerConfig sSpatialLayers1;
        public SSpatialLayerConfig sSpatialLayers2;
        public SSpatialLayerConfig sSpatialLayers3;
        public SSpatialLayerConfig sSpatialLayers4;

        public ECOMPLEXITY_MODE iComplexityMode;
        public uint uiIntraPeriod;     ///< period of Intra frame
        public int iNumRefFrame;      ///< number of reference frame used
        public EParameterSetStrategy eSpsPpsIdStrategy;       ///< different stategy in adjust ID in SPS/PPS: 0- constant ID, 1-additional ID, 6-mapping and additional
        public bool bPrefixNalAddingCtrl;        ///< false:not use Prefix NAL; true: use Prefix NAL
        public bool bEnableSSEI;                 ///< false:not use SSEI; true: use SSEI -- TODO: planning to remove the interface of SSEI
        public bool bSimulcastAVC;               ///< (when encoding more than 1 spatial layer) false: use SVC syntax for higher layers; true: use Simulcast AVC
        public int iPaddingFlag;                ///< 0:disable padding;1:padding
        public int iEntropyCodingModeFlag;      ///< 0:CAVLC  1:CABAC.

        /* rc control */
        public bool bEnableFrameSkip;            ///< False: don't skip frame even if VBV buffer overflow.True: allow skipping frames to keep the bitrate within limits
        public int iMaxBitrate;                 ///< the maximum bitrate, in unit of bps, set it to UNSPECIFIED_BIT_RATE if not needed
        public int iMaxQp;                      ///< the maximum QP encoder supports
        public int iMinQp;                      ///< the minmum QP encoder supports
        public uint uiMaxNalSize;           ///< the maximum NAL size.  This value should be not 0 for dynamic slice mode

        /*LTR settings*/
        public bool bEnableLongTermReference;   ///< 1: on, 0: off
        public int iLTRRefNum;                 ///< the number of LTR(long term reference),TODO: not supported to set it arbitrary yet
        public uint iLtrMarkPeriod;    ///< the LTR marked period that is used in feedback.
        /* multi-thread settings*/
        public ushort iMultipleThreadIdc;                  ///< 1 # 0: auto(dynamic imp. internal encoder); 1: multiple threads imp. disabled; lager than 1: count number of threads;
        public bool bUseLoadBalancing; ///< only used when uiSliceMode=1 or 3, will change slicing of a picture during the run-time of multi-thread encoding, so the result of each run may be different

        /* Deblocking loop filter */
        public int iLoopFilterDisableIdc;     ///< 0: on, 1: off, 2: on except for slice boundaries
        public int iLoopFilterAlphaC0Offset;  ///< AlphaOffset: valid range [-6, 6], default 0
        public int iLoopFilterBetaOffset;     ///< BetaOffset: valid range [-6, 6], default 0
        /*pre-processing feature*/
        public bool bEnableDenoise;              ///< denoise control
        public bool bEnableBackgroundDetection;  ///< background detection control //VAA_BACKGROUND_DETECTION //BGD cmd
        public bool bEnableAdaptiveQuant;        ///< adaptive quantization control
        public bool bEnableFrameCroppingFlag;    ///< enable frame cropping flag: TRUE always in application
        public bool bEnableSceneChangeDetect;

        public bool bIsLosslessLink;            ///<  LTR advanced setting
    }
    /**
    * @brief Bitstream inforamtion of a layer being encoded
    */
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct SLayerBSInfo
    {
        public byte uiTemporalId;
        public byte uiSpatialId;
        public byte uiQualityId;
        public EVideoFrameType eFrameType;
        public byte uiLayerType;
        /**
            * The sub sequence layers are ordered hierarchically based on their dependency on each other so that any picture in a layer shall not be
            * predicted from any picture on any higher layer.
        */
        public int iSubSeqId;                ///< refer to D.2.11 Sub-sequence information SEI message semantics
        public int iNalCount;              ///< count number of NAL coded already
        public IntPtr pNalLengthInByte;       ///< length of NAL size in byte from 0 to iNalCount-1
        public IntPtr pBsBuf;       ///< buffer of bitstream contained
    }
    /**
    * @brief Frame bit stream info
    */
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class SFrameBSInfo
    {
        public int iLayerNum;
        public SLayerBSInfo sLayerInfo000;
        public SLayerBSInfo sLayerInfo001;
        public SLayerBSInfo sLayerInfo002;
        public SLayerBSInfo sLayerInfo003;
        public SLayerBSInfo sLayerInfo004;
        public SLayerBSInfo sLayerInfo005;
        public SLayerBSInfo sLayerInfo006;
        public SLayerBSInfo sLayerInfo007;
        public SLayerBSInfo sLayerInfo008;
        public SLayerBSInfo sLayerInfo009;
        public SLayerBSInfo sLayerInfo010;
        public SLayerBSInfo sLayerInfo011;
        public SLayerBSInfo sLayerInfo012;
        public SLayerBSInfo sLayerInfo013;
        public SLayerBSInfo sLayerInfo014;
        public SLayerBSInfo sLayerInfo015;
        public SLayerBSInfo sLayerInfo016;
        public SLayerBSInfo sLayerInfo017;
        public SLayerBSInfo sLayerInfo018;
        public SLayerBSInfo sLayerInfo019;
        public SLayerBSInfo sLayerInfo020;
        public SLayerBSInfo sLayerInfo021;
        public SLayerBSInfo sLayerInfo022;
        public SLayerBSInfo sLayerInfo023;
        public SLayerBSInfo sLayerInfo024;
        public SLayerBSInfo sLayerInfo025;
        public SLayerBSInfo sLayerInfo026;
        public SLayerBSInfo sLayerInfo027;
        public SLayerBSInfo sLayerInfo028;
        public SLayerBSInfo sLayerInfo029;
        public SLayerBSInfo sLayerInfo030;
        public SLayerBSInfo sLayerInfo031;
        public SLayerBSInfo sLayerInfo032;
        public SLayerBSInfo sLayerInfo033;
        public SLayerBSInfo sLayerInfo034;
        public SLayerBSInfo sLayerInfo035;
        public SLayerBSInfo sLayerInfo036;
        public SLayerBSInfo sLayerInfo037;
        public SLayerBSInfo sLayerInfo038;
        public SLayerBSInfo sLayerInfo039;
        public SLayerBSInfo sLayerInfo040;
        public SLayerBSInfo sLayerInfo041;
        public SLayerBSInfo sLayerInfo042;
        public SLayerBSInfo sLayerInfo043;
        public SLayerBSInfo sLayerInfo044;
        public SLayerBSInfo sLayerInfo045;
        public SLayerBSInfo sLayerInfo046;
        public SLayerBSInfo sLayerInfo047;
        public SLayerBSInfo sLayerInfo048;
        public SLayerBSInfo sLayerInfo049;
        public SLayerBSInfo sLayerInfo050;
        public SLayerBSInfo sLayerInfo051;
        public SLayerBSInfo sLayerInfo052;
        public SLayerBSInfo sLayerInfo053;
        public SLayerBSInfo sLayerInfo054;
        public SLayerBSInfo sLayerInfo055;
        public SLayerBSInfo sLayerInfo056;
        public SLayerBSInfo sLayerInfo057;
        public SLayerBSInfo sLayerInfo058;
        public SLayerBSInfo sLayerInfo059;
        public SLayerBSInfo sLayerInfo060;
        public SLayerBSInfo sLayerInfo061;
        public SLayerBSInfo sLayerInfo062;
        public SLayerBSInfo sLayerInfo063;
        public SLayerBSInfo sLayerInfo064;
        public SLayerBSInfo sLayerInfo065;
        public SLayerBSInfo sLayerInfo066;
        public SLayerBSInfo sLayerInfo067;
        public SLayerBSInfo sLayerInfo068;
        public SLayerBSInfo sLayerInfo069;
        public SLayerBSInfo sLayerInfo070;
        public SLayerBSInfo sLayerInfo071;
        public SLayerBSInfo sLayerInfo072;
        public SLayerBSInfo sLayerInfo073;
        public SLayerBSInfo sLayerInfo074;
        public SLayerBSInfo sLayerInfo075;
        public SLayerBSInfo sLayerInfo076;
        public SLayerBSInfo sLayerInfo077;
        public SLayerBSInfo sLayerInfo078;
        public SLayerBSInfo sLayerInfo079;
        public SLayerBSInfo sLayerInfo080;
        public SLayerBSInfo sLayerInfo081;
        public SLayerBSInfo sLayerInfo082;
        public SLayerBSInfo sLayerInfo083;
        public SLayerBSInfo sLayerInfo084;
        public SLayerBSInfo sLayerInfo085;
        public SLayerBSInfo sLayerInfo086;
        public SLayerBSInfo sLayerInfo087;
        public SLayerBSInfo sLayerInfo088;
        public SLayerBSInfo sLayerInfo089;
        public SLayerBSInfo sLayerInfo090;
        public SLayerBSInfo sLayerInfo091;
        public SLayerBSInfo sLayerInfo092;
        public SLayerBSInfo sLayerInfo093;
        public SLayerBSInfo sLayerInfo094;
        public SLayerBSInfo sLayerInfo095;
        public SLayerBSInfo sLayerInfo096;
        public SLayerBSInfo sLayerInfo097;
        public SLayerBSInfo sLayerInfo098;
        public SLayerBSInfo sLayerInfo099;
        public SLayerBSInfo sLayerInfo100;
        public SLayerBSInfo sLayerInfo101;
        public SLayerBSInfo sLayerInfo102;
        public SLayerBSInfo sLayerInfo103;
        public SLayerBSInfo sLayerInfo104;
        public SLayerBSInfo sLayerInfo105;
        public SLayerBSInfo sLayerInfo106;
        public SLayerBSInfo sLayerInfo107;
        public SLayerBSInfo sLayerInfo108;
        public SLayerBSInfo sLayerInfo109;
        public SLayerBSInfo sLayerInfo110;
        public SLayerBSInfo sLayerInfo111;
        public SLayerBSInfo sLayerInfo112;
        public SLayerBSInfo sLayerInfo113;
        public SLayerBSInfo sLayerInfo114;
        public SLayerBSInfo sLayerInfo115;
        public SLayerBSInfo sLayerInfo116;
        public SLayerBSInfo sLayerInfo117;
        public SLayerBSInfo sLayerInfo118;
        public SLayerBSInfo sLayerInfo119;
        public SLayerBSInfo sLayerInfo120;
        public SLayerBSInfo sLayerInfo121;
        public SLayerBSInfo sLayerInfo122;
        public SLayerBSInfo sLayerInfo123;
        public SLayerBSInfo sLayerInfo124;
        public SLayerBSInfo sLayerInfo125;
        public SLayerBSInfo sLayerInfo126;
        public SLayerBSInfo sLayerInfo127;
        public EVideoFrameType eFrameType;
        public int iFrameSizeInBytes;
        public long uiTimeStamp;
    }
        
    /**
    *  @brief Structure for source picture
    */
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class SSourcePicture
    {
        public EVideoFormatType iColorFormat;          ///< color space type
        public int iStride0;            ///< stride for each plane pData
        public int iStride1;
        public int iStride2;
        public int iStride3;
        public IntPtr pData0;        ///< plane pData
        public IntPtr pData1;
        public IntPtr pData2;
        public IntPtr pData3;
        public int iPicWidth;             ///< luma picture width in x coordinate
        public int iPicHeight;            ///< luma picture height in y coordinate
        public long uiTimeStamp;           ///< timestamp of the source picture, unit: millisecond
    }

    /**
     * @brief Struct of OpenH264 version
     */
    ///
    /// E.g. SDK version is 1.2.0.0, major version number is 1, minor version number is 2, and revision number is 0.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct OpenH264Version
    {
        public readonly uint uMajor;                  ///< The major version number
        public readonly uint uMinor;                  ///< The minor version number
        public readonly uint uRevision;               ///< The revision number
        public readonly uint uReserved;               ///< The reserved number, it should be 0.
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct ISVCEncoderVtbl
    {
        public IntPtr Initialize; //int (* Initialize) (ISVCEncoder*, const SEncParamBase* pParam);
        public IntPtr InitializeExt; //int (* InitializeExt) (ISVCEncoder*, const SEncParamExt* pParam);
        public IntPtr GetDefaultParams; //int (* GetDefaultParams) (ISVCEncoder*, SEncParamExt* pParam);
        public IntPtr Uninitialize; //int (* Uninitialize) (ISVCEncoder*);
        public IntPtr EncodeFrame; //int (* EncodeFrame) (ISVCEncoder*, const SSourcePicture* kpSrcPic, SFrameBSInfo* pBsInfo);
        public IntPtr EncodeParameterSets; //int (* EncodeParameterSets) (ISVCEncoder*, SFrameBSInfo* pBsInfo);
        public IntPtr ForceIntraFrame; //int (* ForceIntraFrame) (ISVCEncoder*, bool bIDR);
        public IntPtr SetOption; //int (* SetOption) (ISVCEncoder*, ENCODER_OPTION eOptionId, void* pOption);
        public IntPtr GetOption; //int (* GetOption) (ISVCEncoder*, ENCODER_OPTION eOptionId, void* pOption);

        /**
        * @brief  Initialize the encoder
        * @param  pParam  basic encoder parameter
        * @return CM_RETURN: 0 - success; otherwise - failed;
        */
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate int InitializeProc(
            [In]SVCEncoder/*ISVCEncoder^*/	encoder,
            [In]IntPtr/* SEncParamBase^*/ pParam
        );
        /**
        * @brief  Initilaize encoder by using extension parameters.
        * @param  pParam  extension parameter for encoder
        * @return CM_RETURN: 0 - success; otherwise - failed;
        */
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate int InitializeExtProc(
            [In]SVCEncoder/*ISVCEncoder^*/	encoder,
            [In]IntPtr/* SEncParamExt^*/ pParam
        );
        /**
        * @brief   Get the default extension parameters.
        *          If you want to change some parameters of encoder, firstly you need to get the default encoding parameters,
        *          after that you can change part of parameters you want to.
        * @param   pParam  extension parameter for encoder
        * @return  CM_RETURN: 0 - success; otherwise - failed;
        * */
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate int GetDefaultParamsProc(
            [In]SVCEncoder/*ISVCEncoder^*/	encoder,
            [In]IntPtr/* SEncParamExt^*/ pParam
        );

        /// uninitialize the encoder
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate int UninitializeProc(
            [In]SVCEncoder/*ISVCEncoder^*/	encoder
        );
        /**
        * @brief Encode one frame
        * @param kpSrcPic the pointer to the source luminance plane
        *        chrominance data:
        *        CbData = kpSrc  +  m_iMaxPicWidth * m_iMaxPicHeight;
        *        CrData = CbData + (m_iMaxPicWidth * m_iMaxPicHeight)/4;
        *        the application calling this interface needs to ensure the data validation between the location
        * @param pBsInfo output bit stream
        * @return  0 - success; otherwise -failed;
        */
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate int EncodeFrameProc(
            [In]SVCEncoder/*ISVCEncoder^*/	encoder,
            [In]IntPtr/* SSourcePicture^*/ kpSrcPic,
            [In]IntPtr/* SFrameBSInfo^*/ pBsInfo
        );
        /**
        * @brief  Encode the parameters from output bit stream
        * @param  pBsInfo output bit stream
        * @return 0 - success; otherwise - failed;
        */
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate int EncodeParameterSetsProc(
            [In]SVCEncoder/*ISVCEncoder^*/	encoder,
            [In]IntPtr/* SFrameBSInfo^*/ pBsInfo
        );
        /**
        * @brief  Force encoder to encoder frame as IDR if bIDR set as true
        * @param  bIDR true: force encoder to encode frame as IDR frame;false, return 1 and nothing to do
        * @return 0 - success; otherwise - failed;
        */
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate int ForceIntraFrameProc(
            [In]SVCEncoder/*ISVCEncoder^*/	encoder,
            [In]bool bIDR
        );
        /**
        * @brief   Set option for encoder, detail option type, please refer to enumurate ENCODER_OPTION.
        * @param   pOption option for encoder such as InDataFormat, IDRInterval, SVC Encode Param, Frame Rate, Bitrate,...
        * @return  CM_RETURN: 0 - success; otherwise - failed;
        */
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate int SetOptionProc(
            [In]SVCEncoder/*ISVCEncoder^*/	encoder,
            [In]ENCODER_OPTION eOptionId,
            [In]IntPtr	pOption
        );
        /**
        * @brief   Set option for encoder, detail option type, please refer to enumurate ENCODER_OPTION.
        * @param   pOption option for encoder such as InDataFormat, IDRInterval, SVC Encode Param, Frame Rate, Bitrate,...
        * @return  CM_RETURN: 0 - success; otherwise - failed;
        */
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate int GetOptionProc(
            [In]SVCEncoder/*ISVCEncoder^*/	encoder,
            [In]ENCODER_OPTION eOptionId,
            [In]IntPtr pOption
        );
    }

    //typedef void (*WelsTraceCallback) (void* ctx, int level, const char* string);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
    internal delegate void WelsTraceCallback(
        [In]IntPtr	ctx,
        [In]int level,
        [In]IntPtr string_
    );

    internal static class Libraries
    {
        public const string OpenH264 = "openH264.dll";
    }

    public sealed class SVCEncoder : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        private SVCEncoder() : base(true)
        {
        }

        override protected bool ReleaseHandle()
        {
            Trace.WriteLine("WelsDestroySVCEncoder");
            SafeNativeMethods.WelsDestroySVCEncoder(handle);
            return true;
        }
    }

    public static class SafeNativeMethods
    {
        /** @brief   Create encoder
          *  @param   ppEncoder encoder
          *  @return  0 - success; otherwise - failed;
         */
        //int WelsCreateSVCEncoder(ISVCEncoder** ppEncoder);
        [DllImport(Libraries.OpenH264, CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:安全でない DllImportSearchPath 値を使用しない", Justification = "<保留中>")]
        internal static extern int WelsCreateSVCEncoder(
            [Out] out SVCEncoder ppEncoder
        );

        /** @brief   Destroy encoder
        *   @param   pEncoder encoder
         *  @return  void
        */
        //void WelsDestroySVCEncoder(ISVCEncoder* pEncoder);
        [DllImport(Libraries.OpenH264, CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:安全でない DllImportSearchPath 値を使用しない", Justification = "<保留中>")]
        internal static extern void WelsDestroySVCEncoder(
            [In] IntPtr pEncoder
        );

        /** @brief   Get the capability of decoder
         *  @param   pDecCapability  decoder capability
         *  @return  0 - success; otherwise - failed;
        */
        //int WelsGetDecoderCapability(SDecoderCapability* pDecCapability);


        /** @brief   Create decoder
         *  @param   ppDecoder decoder
         *  @return  0 - success; otherwise - failed;
        */
        //long WelsCreateDecoder(ISVCDecoder** ppDecoder);


        /** @brief   Destroy decoder
         *  @param   pDecoder  decoder
         *  @return  void
        */
        //void WelsDestroyDecoder(ISVCDecoder* pDecoder);

        /** @brief   Get codec version
         *           Note, old versions of Mingw (GCC < 4.7) are buggy and use an
         *           incorrect/different ABI for calling this function, making it
         *           incompatible with MSVC builds.
         *  @return  The linked codec version
        */
        //OpenH264Version WelsGetCodecVersion(void);

        /** @brief   Get codec version
         *  @param   pVersion  struct to fill in with the version
        */
        [DllImport(Libraries.OpenH264, CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:安全でない DllImportSearchPath 値を使用しない", Justification = "<保留中>")]
        internal static extern void WelsGetCodecVersionEx(
            [Out] out OpenH264Version pVersion
        );
    }
}

#pragma warning restore CA1069 // 列挙値を重複させることはできない
#pragma warning restore CA1700 // 列挙型値に 'Reserved' という名前を指定しません
#pragma warning restore CA1008 // 列挙型は 0 値を含んでいなければなりません
#pragma warning restore CA1711 // 識別子は、不適切なサフィックスを含むことはできません
#pragma warning restore CA1712 // 列挙値の前に型名を付けないでください
#pragma warning restore CA1051 // 参照可能なインスタンス フィールドを宣言しません
#pragma warning restore CA1815 // equals および operator equals を値型でオーバーライドします
#pragma warning restore CA1707 // 識別子はアンダースコアを含むことはできません

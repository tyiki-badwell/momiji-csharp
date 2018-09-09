using System;
using System.Runtime.InteropServices;

namespace Momiji.Interop.Vst
{
    //-------------------------------------------------------------------------------------------------------
    /** String length limits (in characters excl. 0 byte) */
    //-------------------------------------------------------------------------------------------------------
    public enum VstStringConstants : Int32
    {
        //-------------------------------------------------------------------------------------------------------
        kVstMaxProgNameLen = 24,    //< used for #effGetProgramName, #effSetProgramName, #effGetProgramNameIndexed
        kVstMaxParamStrLen = 8, //< used for #effGetParamLabel, #effGetParamDisplay, #effGetParamName
        kVstMaxVendorStrLen = 64,   //< used for #effGetVendorString, #audioMasterGetVendorString
        kVstMaxProductStrLen = 64,  //< used for #effGetProductString, #audioMasterGetProductString
        kVstMaxEffectNameLen = 32,  //< used for #effGetEffectName
        kVstMaxNameLen = 64,    //< used for #MidiProgramName, #MidiProgramCategory, #MidiKeyName, #VstSpeakerProperties, #VstPinProperties
        kVstMaxLabelLen = 64,   //< used for #VstParameterProperties->label, #VstPinProperties->label
        kVstMaxShortLabelLen = 8,   //< used for #VstParameterProperties->shortLabel, #VstPinProperties->shortLabel
        kVstMaxCategLabelLen = 24,  //< used for #VstParameterProperties->label
        kVstMaxFileNameLen = 100    //< used for #VstAudioFile->name
                                    //-------------------------------------------------------------------------------------------------------
    };

    public class AudioMaster
    {
        private AudioMaster()
        {
        }

        //-------------------------------------------------------------------------------------------------------
        /** Basic dispatcher Opcodes (Plug-in to Host) */
        //-------------------------------------------------------------------------------------------------------
        public enum Opcodes : Int32
        {
            //-------------------------------------------------------------------------------------------------------
            audioMasterAutomate = 0,    ///< [index]: parameter index [opt]: parameter value  @see AudioEffect::setParameterAutomated
            audioMasterVersion,         ///< [return value]: Host VST version (for example 2400 for VST 2.4) @see AudioEffect::getMasterVersion
            audioMasterCurrentId,       ///< [return value]: current unique identifier on shell plug-in  @see AudioEffect::getCurrentUniqueId
            audioMasterIdle,            ///< no arguments  @see AudioEffect::masterIdle
            audioMasterPinConnectedDeprecated,      ///< \deprecated deprecated in VST 2.4 r2

            audioMasterDeprecated,
            audioMasterWantMidiDeprecated,  ///< \deprecated deprecated in VST 2.4

            audioMasterGetTime,             ///< [return value]: #VstTimeInfo* or null if not supported [value]: request mask  @see VstTimeInfoFlags @see AudioEffectX::getTimeInfo
            audioMasterProcessEvents,       ///< [ptr]: pointer to #VstEvents  @see VstEvents @see AudioEffectX::sendVstEventsToHost

            audioMasterSetTimeDeprecated,   ///< \deprecated deprecated in VST 2.4
            audioMasterTempoAtDeprecated,   ///< \deprecated deprecated in VST 2.4
            audioMasterGetNumAutomatableParametersDeprecated,   ///< \deprecated deprecated in VST 2.4
            audioMasterGetParameterQuantizationDeprecated,      ///< \deprecated deprecated in VST 2.4

            audioMasterIOChanged,           ///< [return value]: 1 if supported  @see AudioEffectX::ioChanged

            audioMasterNeedIdleDeprecated,  ///< \deprecated deprecated in VST 2.4

            audioMasterSizeWindow,          ///< [index]: new width [value]: new height [return value]: 1 if supported  @see AudioEffectX::sizeWindow
            audioMasterGetSampleRate,       ///< [return value]: current sample rate  @see AudioEffectX::updateSampleRate
            audioMasterGetBlockSize,        ///< [return value]: current block size  @see AudioEffectX::updateBlockSize
            audioMasterGetInputLatency,     ///< [return value]: input latency in audio samples  @see AudioEffectX::getInputLatency
            audioMasterGetOutputLatency,    ///< [return value]: output latency in audio samples  @see AudioEffectX::getOutputLatency

            audioMasterGetPreviousPlugDeprecated,           ///< \deprecated deprecated in VST 2.4
            audioMasterGetNextPlugDeprecated,               ///< \deprecated deprecated in VST 2.4
            audioMasterWillReplaceOrAccumulateDeprecated,   ///< \deprecated deprecated in VST 2.4

            audioMasterGetCurrentProcessLevel,  ///< [return value]: current process level  @see VstProcessLevels
            audioMasterGetAutomationState,      ///< [return value]: current automation state  @see VstAutomationStates

            audioMasterOfflineStart,            ///< [index]: numNewAudioFiles [value]: numAudioFiles [ptr]: #VstAudioFile*  @see AudioEffectX::offlineStart
            audioMasterOfflineRead,             ///< [index]: bool readSource [value]: #VstOfflineOption* @see VstOfflineOption [ptr]: #VstOfflineTask*  @see VstOfflineTask @see AudioEffectX::offlineRead
            audioMasterOfflineWrite,            ///< @see audioMasterOfflineRead @see AudioEffectX::offlineRead
            audioMasterOfflineGetCurrentPass,   ///< @see AudioEffectX::offlineGetCurrentPass
            audioMasterOfflineGetCurrentMetaPass,   ///< @see AudioEffectX::offlineGetCurrentMetaPass

            audioMasterSetOutputSampleRateDeprecated,           ///< \deprecated deprecated in VST 2.4
            audioMasterGetOutputSpeakerArrangementDeprecated,   ///< \deprecated deprecated in VST 2.4

            audioMasterGetVendorString,         ///< [ptr]: char buffer for vendor string, limited to #kVstMaxVendorStrLen  @see AudioEffectX::getHostVendorString
            audioMasterGetProductString,        ///< [ptr]: char buffer for vendor string, limited to #kVstMaxProductStrLen  @see AudioEffectX::getHostProductString
            audioMasterGetVendorVersion,        ///< [return value]: vendor-specific version  @see AudioEffectX::getHostVendorVersion
            audioMasterVendorSpecific,          ///< no definition, vendor specific handling  @see AudioEffectX::hostVendorSpecific

            audioMasterSetIconDeprecated,       ///< \deprecated deprecated in VST 2.4

            audioMasterCanDo,                   ///< [ptr]: "can do" string [return value]: 1 for supported
            audioMasterGetLanguage,             ///< [return value]: language code  @see VstHostLanguage

            audioMasterOpenWindowDeprecated,        ///< \deprecated deprecated in VST 2.4
            audioMasterCloseWindowDeprecated,   ///< \deprecated deprecated in VST 2.4

            audioMasterGetDirectory,            ///< [return value]: FSSpec on MAC, else char*  @see AudioEffectX::getDirectory
            audioMasterUpdateDisplay,           ///< no arguments	
            audioMasterBeginEdit,               ///< [index]: parameter index  @see AudioEffectX::beginEdit
            audioMasterEndEdit,                 ///< [index]: parameter index  @see AudioEffectX::endEdit
            audioMasterOpenFileSelector,        ///< [ptr]: VstFileSelect* [return value]: 1 if supported  @see AudioEffectX::openFileSelector
            audioMasterCloseFileSelector,       ///< [ptr]: VstFileSelect*  @see AudioEffectX::closeFileSelector

            audioMasterEditFileDeprecated,      ///< \deprecated deprecated in VST 2.4

            audioMasterGetChunkFileDeprecated,  ///< \deprecated deprecated in VST 2.4 [ptr]: char[2048] or sizeof (FSSpec) [return value]: 1 if supported  @see AudioEffectX::getChunkFile

            audioMasterGetInputSpeakerArrangementDeprecated,    ///< \deprecated deprecated in VST 2.4

            //-------------------------------------------------------------------------------------------------------
        };
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate IntPtr CallBack(
            [In]IntPtr/*AEffect^*/		effect,
            [In]Opcodes opcode,
            [In]Int32 index,
            [In]IntPtr value,
            [In]IntPtr ptr,
            [In]Single opt
        );

    }

   [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class AEffect
    {
        //-------------------------------------------------------------------------------------------------------
        /** Basic dispatcher Opcodes (Host to Plug-in) */
        //-------------------------------------------------------------------------------------------------------
        public enum Opcodes : Int32
        {
            effOpen = 0,        ///< no arguments  @see AudioEffect::open
            effClose,           ///< no arguments  @see AudioEffect::close

            effSetProgram,      ///< [value]: new program number  @see AudioEffect::setProgram
            effGetProgram,      ///< [return value]: current program number  @see AudioEffect::getProgram
            effSetProgramName,  ///< [ptr]: char* with new program name, limited to #kVstMaxProgNameLen  @see AudioEffect::setProgramName
            effGetProgramName,  ///< [ptr]: char buffer for current program name, limited to #kVstMaxProgNameLen  @see AudioEffect::getProgramName

            effGetParamLabel,   ///< [ptr]: char buffer for parameter label, limited to #kVstMaxParamStrLen  @see AudioEffect::getParameterLabel
            effGetParamDisplay, ///< [ptr]: char buffer for parameter display, limited to #kVstMaxParamStrLen  @see AudioEffect::getParameterDisplay
            effGetParamName,    ///< [ptr]: char buffer for parameter name, limited to #kVstMaxParamStrLen  @see AudioEffect::getParameterName

            effGetVuDeprecated, ///< \deprecated deprecated in VST 2.4

            effSetSampleRate,   ///< [opt]: new sample rate for audio processing  @see AudioEffect::setSampleRate
            effSetBlockSize,    ///< [value]: new maximum block size for audio processing  @see AudioEffect::setBlockSize
            effMainsChanged,    ///< [value]: 0 means "turn off", 1 means "turn on"  @see AudioEffect::suspend @see AudioEffect::resume

            effEditGetRect,     ///< [ptr]: #ERect** receiving pointer to editor size  @see ERect @see AEffEditor::getRect
            effEditOpen,        ///< [ptr]: system dependent Window pointer, e.g. HWND on Windows  @see AEffEditor::open
            effEditClose,       ///< no arguments @see AEffEditor::close

            effEditDrawDeprecated,  ///< \deprecated deprecated in VST 2.4
            effEditMouseDeprecated, ///< \deprecated deprecated in VST 2.4
            effEditKeyDeprecated,   ///< \deprecated deprecated in VST 2.4

            effEditIdle,        ///< no arguments @see AEffEditor::idle

            effEditTopDeprecated,   ///< \deprecated deprecated in VST 2.4
            effEditSleepDeprecated, ///< \deprecated deprecated in VST 2.4
            effIdentifyDeprecated,  ///< \deprecated deprecated in VST 2.4

            effGetChunk,        ///< [ptr]: void** for chunk data address [index]: 0 for bank, 1 for program  @see AudioEffect::getChunk
            effSetChunk,        ///< [ptr]: chunk data [value]: byte size [index]: 0 for bank, 1 for program  @see AudioEffect::setChunk

            effProcessEvents,   ///< [ptr]: #VstEvents*  @see AudioEffectX::processEvents

            effCanBeAutomated,                      ///< [index]: parameter index [return value]: 1=true, 0=false  @see AudioEffectX::canParameterBeAutomated
            effString2Parameter,                    ///< [index]: parameter index [ptr]: parameter string [return value]: true for success  @see AudioEffectX::string2parameter

            effGetNumProgramCategoriesDeprecated,   ///< \deprecated deprecated in VST 2.4

            effGetProgramNameIndexed,               ///< [index]: program index [ptr]: buffer for program name, limited to #kVstMaxProgNameLen [return value]: true for success  @see AudioEffectX::getProgramNameIndexed

            effCopyProgramDeprecated,   ///< \deprecated deprecated in VST 2.4
            effConnectInputDeprecated,  ///< \deprecated deprecated in VST 2.4
            effConnectOutputDeprecated, ///< \deprecated deprecated in VST 2.4

            effGetInputProperties,                  ///< [index]: input index [ptr]: #VstPinProperties* [return value]: 1 if supported  @see AudioEffectX::getInputProperties
            effGetOutputProperties,             ///< [index]: output index [ptr]: #VstPinProperties* [return value]: 1 if supported  @see AudioEffectX::getOutputProperties
            effGetPlugCategory,                 ///< [return value]: category  @see VstPlugCategory @see AudioEffectX::getPlugCategory

            effGetCurrentPositionDeprecated,    ///< \deprecated deprecated in VST 2.4
            effGetDestinationBufferDeprecated,  ///< \deprecated deprecated in VST 2.4

            effOfflineNotify,                       ///< [ptr]: #VstAudioFile array [value]: count [index]: start flag  @see AudioEffectX::offlineNotify
            effOfflinePrepare,                      ///< [ptr]: #VstOfflineTask array [value]: count  @see AudioEffectX::offlinePrepare
            effOfflineRun,                          ///< [ptr]: #VstOfflineTask array [value]: count  @see AudioEffectX::offlineRun

            effProcessVarIo,                        ///< [ptr]: #VstVariableIo*  @see AudioEffectX::processVariableIo
            effSetSpeakerArrangement,               ///< [value]: input #VstSpeakerArrangement* [ptr]: output #VstSpeakerArrangement*  @see AudioEffectX::setSpeakerArrangement

            effSetBlockSizeAndSampleRateDeprecated, ///< \deprecated deprecated in VST 2.4

            effSetBypass,                           ///< [value]: 1 = bypass, 0 = no bypass  @see AudioEffectX::setBypass
            effGetEffectName,                       ///< [ptr]: buffer for effect name, limited to #kVstMaxEffectNameLen  @see AudioEffectX::getEffectName

            effGetErrorTextDeprecated,  ///< \deprecated deprecated in VST 2.4

            effGetVendorString,                 ///< [ptr]: buffer for effect vendor string, limited to #kVstMaxVendorStrLen  @see AudioEffectX::getVendorString
            effGetProductString,                    ///< [ptr]: buffer for effect vendor string, limited to #kVstMaxProductStrLen  @see AudioEffectX::getProductString
            effGetVendorVersion,                    ///< [return value]: vendor-specific version  @see AudioEffectX::getVendorVersion
            effVendorSpecific,                      ///< no definition, vendor specific handling  @see AudioEffectX::vendorSpecific
            effCanDo,                               ///< [ptr]: "can do" string [return value]: 0: "don't know" -1: "no" 1: "yes"  @see AudioEffectX::canDo
            effGetTailSize,                     ///< [return value]: tail size (for example the reverb time of a reverb plug-in); 0 is default (return 1 for 'no tail')

            effIdleDeprecated,              ///< \deprecated deprecated in VST 2.4
            effGetIconDeprecated,           ///< \deprecated deprecated in VST 2.4
            effSetViewPositionDeprecated,   ///< \deprecated deprecated in VST 2.4

            effGetParameterProperties,              ///< [index]: parameter index [ptr]: #VstParameterProperties* [return value]: 1 if supported  @see AudioEffectX::getParameterProperties

            effKeysRequiredDeprecated,  ///< \deprecated deprecated in VST 2.4

            effGetVstVersion,                       ///< [return value]: VST version  @see AudioEffectX::getVstVersion

            effEditKeyDown,                     ///< [index]: ASCII character [value]: virtual key [opt]: modifiers [return value]: 1 if key used  @see AEffEditor::onKeyDown
            effEditKeyUp,                           ///< [index]: ASCII character [value]: virtual key [opt]: modifiers [return value]: 1 if key used  @see AEffEditor::onKeyUp
            effSetEditKnobMode,                 ///< [value]: knob mode 0: circular, 1: circular relativ, 2: linear (CKnobMode in VSTGUI)  @see AEffEditor::setKnobMode

            effGetMidiProgramName,                  ///< [index]: MIDI channel [ptr]: #MidiProgramName* [return value]: number of used programs, 0 if unsupported  @see AudioEffectX::getMidiProgramName
            effGetCurrentMidiProgram,               ///< [index]: MIDI channel [ptr]: #MidiProgramName* [return value]: index of current program  @see AudioEffectX::getCurrentMidiProgram
            effGetMidiProgramCategory,              ///< [index]: MIDI channel [ptr]: #MidiProgramCategory* [return value]: number of used categories, 0 if unsupported  @see AudioEffectX::getMidiProgramCategory
            effHasMidiProgramsChanged,              ///< [index]: MIDI channel [return value]: 1 if the #MidiProgramName(s) or #MidiKeyName(s) have changed  @see AudioEffectX::hasMidiProgramsChanged
            effGetMidiKeyName,                      ///< [index]: MIDI channel [ptr]: #MidiKeyName* [return value]: true if supported, false otherwise  @see AudioEffectX::getMidiKeyName

            effBeginSetProgram,                 ///< no arguments  @see AudioEffectX::beginSetProgram
            effEndSetProgram,                       ///< no arguments  @see AudioEffectX::endSetProgram

            effGetSpeakerArrangement,               ///< [value]: input #VstSpeakerArrangement* [ptr]: output #VstSpeakerArrangement*  @see AudioEffectX::getSpeakerArrangement
            effShellGetNextPlugin,                  ///< [ptr]: buffer for plug-in name, limited to #kVstMaxProductStrLen [return value]: next plugin's uniqueID  @see AudioEffectX::getNextShellPlugin

            effStartProcess,                        ///< no arguments  @see AudioEffectX::startProcess
            effStopProcess,                     ///< no arguments  @see AudioEffectX::stopProcess
            effSetTotalSampleToProcess,         ///< [value]: number of samples to process, offline only!  @see AudioEffectX::setTotalSampleToProcess
            effSetPanLaw,                           ///< [value]: pan law [opt]: gain  @see VstPanLawType @see AudioEffectX::setPanLaw

            effBeginLoadBank,                       ///< [ptr]: #VstPatchChunkInfo* [return value]: -1: bank can't be loaded, 1: bank can be loaded, 0: unsupported  @see AudioEffectX::beginLoadBank
            effBeginLoadProgram,                    ///< [ptr]: #VstPatchChunkInfo* [return value]: -1: prog can't be loaded, 1: prog can be loaded, 0: unsupported  @see AudioEffectX::beginLoadProgram

            effSetProcessPrecision,             ///< [value]: @see VstProcessPrecision  @see AudioEffectX::setProcessPrecision
            effGetNumMidiInputChannels,         ///< [return value]: number of used MIDI input channels (1-15)  @see AudioEffectX::getNumMidiInputChannels
            effGetNumMidiOutputChannels,            ///< [return value]: number of used MIDI output channels (1-15)  @see AudioEffectX::getNumMidiOutputChannels
        };

        //-------------------------------------------------------------------------------------------------------
        /** AEffect flags */
        //-------------------------------------------------------------------------------------------------------
        [Flags]
        public enum VstAEffectFlags : Int32
        {
            //-------------------------------------------------------------------------------------------------------
            effFlagsHasEditor = 1 << 0,         //< set if the plug-in provides a custom editor
            effFlagsCanReplacing = 1 << 4,          //< supports replacing process mode (which should the default mode in VST 2.4)
            effFlagsProgramChunks = 1 << 5,         //< program data is handled in formatless chunks
            effFlagsIsSynth = 1 << 8,           //< plug-in is a synth (VSTi), Host may assign mixer channels for its outputs
            effFlagsNoSoundInStop = 1 << 9,         //< plug-in does not produce sound when input is all silence
            effFlagsCanDoubleReplacing = 1 << 12,   //< plug-in supports double precision processing

            effFlagsHasClipDeprecated = 1 << 1,         //< \deprecated deprecated in VST 2.4
            effFlagsHasVuDeprecated = 1 << 2,           //< \deprecated deprecated in VST 2.4
            effFlagsCanMonoDeprecated = 1 << 3,         //< \deprecated deprecated in VST 2.4
            effFlagsExtIsAsyncDeprecated = 1 << 10, //< \deprecated deprecated in VST 2.4
            effFlagsExtHasBufferDeprecated = 1 << 11    //< \deprecated deprecated in VST 2.4
                                                        //-------------------------------------------------------------------------------------------------------
        };

        //-------------------------------------------------------------------------------------------------------
        public Int32 magic;            //< must be #kEffectMagic ('VstP')

        /** Host to Plug-in dispatcher @see AudioEffect::dispatcher */
        public IntPtr dispatcher;

        /** \deprecated Accumulating process mode is deprecated in VST 2.4! Use AEffect::processReplacing instead! */
        public IntPtr processDeprecated;

        /** Set new value of automatable parameter @see AudioEffect::setParameter */
        public IntPtr setParameter;

        /** Returns current value of automatable parameter @see AudioEffect::getParameter*/
        public IntPtr getParameter;

        public Int32 numPrograms;  //< number of programs
        public Int32 numParams;    //< all programs are assumed to have numParams parameters
        public Int32 numInputs;    //< number of audio inputs
        public Int32 numOutputs;   //< number of audio outputs

        public VstAEffectFlags flags;      //< @see VstAEffectFlags

        public IntPtr resvd1;      //< reserved for Host, must be 0
        public IntPtr resvd2;      //< reserved for Host, must be 0

        public Int32 initialDelay; //< for algorithms which need input in the first place (Group delay or latency in Samples). This value should be initialized in a resume state.

        public Int32 realQualitiesDeprecated;  //< \deprecated unused member
        public Int32 offQualitiesDeprecated;   //< \deprecated unused member
        public Single ioRatioDeprecated;       //< \deprecated unused member

        public IntPtr _object;          //< #AudioEffect class pointer
        public IntPtr user;            //< user-defined pointer

        public Int32 uniqueID;     //< registered unique identifier (register it at Steinberg 3rd party support Web). This is used to identify a plug-in during save+load of preset and project.
        public Int32 version;      //< plug-in version (example 1100 for version 1.1.0.0)

        /** Process audio samples in replacing mode @see AudioEffect::processReplacing */
        public IntPtr processReplacing;

        /** Process double-precision audio samples in replacing mode @see AudioEffect::processDoubleReplacing */
        public IntPtr processDoubleReplacing;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 56)]
        char[] future;  //< reserved for future use (please zero)
                        //-------------------------------------------------------------------------------------------------------

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate IntPtr VSTPluginMain(
            //[In][MarshalAs(UnmanagedType.FunctionPtr)]AudioMaster.CallBack audioMaster
            [In]IntPtr/*AudioMaster.CallBack*/ audioMaster
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate IntPtr DispatcherProc(
            [In]IntPtr/*AEffect^*/		effect,
            [In]Opcodes opcode,
            [In]Int32 index,
            [In]IntPtr value,
            [In]IntPtr ptr,
            [In]Single opt
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate void ProcessProc(
            [In]IntPtr/*AEffect^*/		effect,
            [In]IntPtr inputs,
            [In]IntPtr outputs,
            [In]Int32 sampleFrames
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate void ProcessDoubleProc(
            [In]IntPtr/*AEffect^*/		effect,
            [In]IntPtr inputs,
            [In]IntPtr outputs,
            [In]Int32 sampleFrames
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate void SetParameterProc(
            [In]IntPtr/*AEffect^*/		effect,
            [In]Int32 index,
            [In]Single parameter
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = false)]
        internal delegate Single GetParameterProc(
            [In]IntPtr/*AEffect^*/		effect,
            [In]Int32 index
        );
    };


    //-------------------------------------------------------------------------------------------------------
    /** Parameter Properties used in #effGetParameterProperties. */
    //-------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct VstParameterProperties
    {
        [FlagsAttribute]
        public enum VstParameterFlags : Int32
        {
            //-------------------------------------------------------------------------------------------------------
            kVstParameterIsSwitch = 1 << 0, ///< parameter is a switch (on/off)
            kVstParameterUsesIntegerMinMax = 1 << 1,    ///< minInteger, maxInteger valid
            kVstParameterUsesFloatStep = 1 << 2,    ///< stepFloat, smallStepFloat, largeStepFloat valid
            kVstParameterUsesIntStep = 1 << 3,  ///< stepInteger, largeStepInteger valid
            kVstParameterSupportsDisplayIndex = 1 << 4, ///< displayIndex valid
            kVstParameterSupportsDisplayCategory = 1 << 5,  ///< category, etc. valid
            kVstParameterCanRamp = 1 << 6   ///< set if parameter value can ramp up/down
            //-------------------------------------------------------------------------------------------------------
        };

        //-------------------------------------------------------------------------------------------------------
        public Single stepFloat;           //< Single step
        public Single smallStepFloat;      //< small Single step
        public Single largeStepFloat;      //< large Single step
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (Int32)(VstStringConstants.kVstMaxLabelLen))]
        public string label;        //< parameter label
        public VstParameterFlags flags;                //< @see VstParameterFlags
        public Int32 minInteger;       //< integer minimum
        public Int32 maxInteger;       //< integer maximum
        public Int32 stepInteger;      //< integer step
        public Int32 largeStepInteger; //< large integer step
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (Int32)(VstStringConstants.kVstMaxShortLabelLen))]
        public string shortLabel;       //< short label, recommended: 6 + delimiter

        // The following are for remote controller display purposes.
        // Note that the kVstParameterSupportsDisplayIndex flag must be set.
        // Host can scan all parameters, and find out in what order
        // to display them:

        public Int16 displayIndex;     //< index where this parameter should be displayed (starting with 0)

        // Host can also possibly display the parameter group (category), such as...
        // ---------------------------
        // Osc 1
        // Wave  Detune  Octave  Mod
        // ---------------------------
        // ...if the plug-in supports it (flag #kVstParameterSupportsDisplayCategory)

        public Int16 category;         //< 0: no category, else group index + 1
        public Int16 numParametersInCategory;          //< number of parameters in category
        public Int16 reserved;         //< zero
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (Int32)(VstStringConstants.kVstMaxCategLabelLen))]
        public string categoryLabel;        //< category label, e.g. "Osc 1" 

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        char[] future;  //< reserved for future use
                        //-------------------------------------------------------------------------------------------------------
    };


    //-------------------------------------------------------------------------------------------------------
    /** Speaker Arrangement Types*/
    //-------------------------------------------------------------------------------------------------------
    public enum VstSpeakerArrangementType : Int32
    {
        //-------------------------------------------------------------------------------------------------------
        kSpeakerArrUserDefined = -2,//< user defined
        kSpeakerArrEmpty = -1,      //< empty arrangement
        kSpeakerArrMono = 0,        //< M
        kSpeakerArrStereo,          //< L R
        kSpeakerArrStereoSurround,  //< Ls Rs
        kSpeakerArrStereoCenter,    //< Lc Rc
        kSpeakerArrStereoSide,      //< Sl Sr
        kSpeakerArrStereoCLfe,      //< C Lfe
        kSpeakerArr30Cine,          //< L R C
        kSpeakerArr30Music,         //< L R S
        kSpeakerArr31Cine,          //< L R C Lfe
        kSpeakerArr31Music,         //< L R Lfe S
        kSpeakerArr40Cine,          //< L R C   S (LCRS)
        kSpeakerArr40Music,         //< L R Ls  Rs (Quadro)
        kSpeakerArr41Cine,          //< L R C   Lfe S (LCRS+Lfe)
        kSpeakerArr41Music,         //< L R Lfe Ls Rs (Quadro+Lfe)
        kSpeakerArr50,              //< L R C Ls  Rs 
        kSpeakerArr51,              //< L R C Lfe Ls Rs
        kSpeakerArr60Cine,          //< L R C   Ls  Rs Cs
        kSpeakerArr60Music,         //< L R Ls  Rs  Sl Sr 
        kSpeakerArr61Cine,          //< L R C   Lfe Ls Rs Cs
        kSpeakerArr61Music,         //< L R Lfe Ls  Rs Sl Sr 
        kSpeakerArr70Cine,          //< L R C Ls  Rs Lc Rc 
        kSpeakerArr70Music,         //< L R C Ls  Rs Sl Sr
        kSpeakerArr71Cine,          //< L R C Lfe Ls Rs Lc Rc
        kSpeakerArr71Music,         //< L R C Lfe Ls Rs Sl Sr
        kSpeakerArr80Cine,          //< L R C Ls  Rs Lc Rc Cs
        kSpeakerArr80Music,         //< L R C Ls  Rs Cs Sl Sr
        kSpeakerArr81Cine,          //< L R C Lfe Ls Rs Lc Rc Cs
        kSpeakerArr81Music,         //< L R C Lfe Ls Rs Cs Sl Sr 
        kSpeakerArr102,             //< L R C Lfe Ls Rs Tfl Tfc Tfr Trl Trr Lfe2
        kNumSpeakerArr
        //-------------------------------------------------------------------------------------------------------
    };

    //-------------------------------------------------------------------------------------------------------
    /** Pin Properties used in #effGetInputProperties and #effGetOutputProperties. */
    //-------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct VstPinProperties
    {
        [FlagsAttribute]
        public enum VstPinPropertiesFlags : Int32
        {
            //-------------------------------------------------------------------------------------------------------
            kVstPinIsActive = 1 << 0,       //< pin is active, ignored by Host
            kVstPinIsStereo = 1 << 1,       //< pin is first of a stereo pair
            kVstPinUseSpeaker = 1 << 2      //< #VstPinProperties::arrangementType is valid and can be used to get the wanted arrangement
                                            //-------------------------------------------------------------------------------------------------------
        };

        //-------------------------------------------------------------------------------------------------------
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (Int32)(VstStringConstants.kVstMaxLabelLen))]
        public string label;                //< pin name
        public VstPinPropertiesFlags flags;                //< @see VstPinPropertiesFlags
        public VstSpeakerArrangementType arrangementType;  //< @see VstSpeakerArrangementType
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (Int32)(VstStringConstants.kVstMaxShortLabelLen))]
        public string shortLabel;           //< short name (recommended: 6 + delimiter)

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        char[] future;              //< reserved for future use
                                    //-------------------------------------------------------------------------------------------------------
    };

    //-------------------------------------------------------------------------------------------------------
    // VstEvent
    //-------------------------------------------------------------------------------------------------------
    //-------------------------------------------------------------------------------------------------------
    /** A generic timestamped event. */
    //-------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Ansi)]
    public struct VstEvent
    {
        //-------------------------------------------------------------------------------------------------------
        /** VstEvent Types used by #VstEvent. */
        //-------------------------------------------------------------------------------------------------------
        public enum VstEventTypes : Int32
        {
            //-------------------------------------------------------------------------------------------------------
            kVstMidiType = 1,                   //< MIDI event  @see VstMidiEvent
            kVstAudioTypeDeprecated,        //< \deprecated unused event type
            kVstVideoTypeDeprecated,        //< \deprecated unused event type
            kVstParameterTypeDeprecated,    //< \deprecated unused event type
            kVstTriggerTypeDeprecated,      //< \deprecated unused event type
            kVstSysExType                       //< MIDI system exclusive  @see VstMidiSysexEvent
                                                //-------------------------------------------------------------------------------------------------------
        };
    };

    //-------------------------------------------------------------------------------------------------------
    /** MIDI Event (to be casted from VstEvent). */
    //-------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct VstMidiEvent
    {
        //-------------------------------------------------------------------------------------------------------
        /** Flags used in #VstMidiEvent. */
        //-------------------------------------------------------------------------------------------------------
        public enum VstMidiEventFlags : Int32
        {
            //-------------------------------------------------------------------------------------------------------
            kVstMidiEventIsRealtime = 1 << 0    //< means that this event is played life (not in playback from a sequencer track).\n This allows the Plug-In to handle these flagged events with higher priority, especially when the Plug-In has a big latency (AEffect::initialDelay)
                                                //-------------------------------------------------------------------------------------------------------
        };

        //-------------------------------------------------------------------------------------------------------
        public VstEvent.VstEventTypes type;          ///< #kVstMidiType
        public Int32 byteSize;      ///< sizeof (VstMidiEvent)
        public Int32 deltaFrames;   ///< sample frames related to the current block start sample position
        public VstMidiEventFlags flags;         ///< @see VstMidiEventFlags
        public Int32 noteLength;    ///< (in sample frames) of entire note, if available, else 0
        public Int32 noteOffset;    ///< offset (in sample frames) into note from note start if available, else 0
        public Byte midiData0;      //< 1 to 3 MIDI bytes; midiData[3] is reserved (zero)
        public Byte midiData1;
        public Byte midiData2;
        public Byte midiData3;
        public Byte detune;            ///< -64 to +63 cents; for scales other than 'well-tempered' ('microtuning')
        public Byte noteOffVelocity;   ///< Note Off Velocity [0, 127]
        public Byte reserved1;         ///< zero (Reserved for future use)
        public Byte reserved2;         ///< zero (Reserved for future use)
        //-------------------------------------------------------------------------------------------------------
        override public string ToString()
        {
            return $"type[{type:F}] byteSize[{byteSize}] deltaFrames[{deltaFrames}] flags[{flags:F}] noteLength[{noteLength}] noteOffset[{noteOffset}] midiData[{midiData0:X2}{midiData1:X2}{midiData2:X2}{midiData3:X2}] detune[{detune}] noteOffVelocity[{noteOffVelocity}]";
        }
    };

    //-------------------------------------------------------------------------------------------------------
    /** A block of events for the current processed audio block. */
    //-------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct VstEvents
    {
        //-------------------------------------------------------------------------------------------------------
        public Int32 numEvents;        //< number of Events in array
        public IntPtr reserved;        //< zero (Reserved for future use)
                                       //IntPtr	events;			//VstEvent* events[2];	///< event pointer array, variable size
                                       //-------------------------------------------------------------------------------------------------------
    };

    //-------------------------------------------------------------------------------------------------------
    // VstTimeInfo
    //-------------------------------------------------------------------------------------------------------
    //-------------------------------------------------------------------------------------------------------
    /** VstTimeInfo requested via #audioMasterGetTime.  @see AudioEffectX::getTimeInfo 

    \note VstTimeInfo::samplePos :Current Position. It must always be valid, and should not cost a lot to ask for. The sample position is ahead of the time displayed to the user. In sequencer stop mode, its value does not change. A 32 bit integer is too small for sample positions, and it's a double to make it easier to convert between ppq and samples.
    \note VstTimeInfo::ppqPos : At tempo 120, 1 quarter makes 1/2 second, so 2.0 ppq translates to 48000 samples at 48kHz sample rate.
    .25 ppq is one sixteenth note then. if you need something like 480ppq, you simply multiply ppq by that scaler.
    \note VstTimeInfo::barStartPos : Say we're at bars/beats readout 3.3.3. That's 2 bars + 2 q + 2 sixteenth, makes 2 * 4 + 2 + .25 = 10.25 ppq. at tempo 120, that's 10.25 * .5 = 5.125 seconds, times 48000 = 246000 samples (if my calculator servers me well :-). 
    \note VstTimeInfo::samplesToNextClock : MIDI Clock Resolution (24 per Quarter Note), can be negative the distance to the next midi clock (24 ppq, pulses per quarter) in samples. unless samplePos falls precicely on a midi clock, this will either be negative such that the previous MIDI clock is addressed, or positive when referencing the following (future) MIDI clock.
    */
    //-------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class VstTimeInfo
    {
        //-------------------------------------------------------------------------------------------------------
        /** Flags used in #VstTimeInfo. */
        //-------------------------------------------------------------------------------------------------------
        [Flags]
        public enum VstTimeInfoFlags : Int32
        {
            //-------------------------------------------------------------------------------------------------------
            kVstTransportChanged = 1,       ///< indicates that play, cycle or record state has changed
            kVstTransportPlaying = 1 << 1,  ///< set if Host sequencer is currently playing
            kVstTransportCycleActive = 1 << 2,  ///< set if Host sequencer is in cycle mode
            kVstTransportRecording = 1 << 3,    ///< set if Host sequencer is in record mode
            kVstAutomationWriting = 1 << 6, ///< set if automation write mode active (record parameter changes)
            kVstAutomationReading = 1 << 7, ///< set if automation read mode active (play parameter changes)
            kVstNanosValid = 1 << 8,    ///< VstTimeInfo::nanoSeconds valid
            kVstPpqPosValid = 1 << 9,   ///< VstTimeInfo::ppqPos valid
            kVstTempoValid = 1 << 10,   ///< VstTimeInfo::tempo valid
            kVstBarsValid = 1 << 11,    ///< VstTimeInfo::barStartPos valid
            kVstCyclePosValid = 1 << 12,    ///< VstTimeInfo::cycleStartPos and VstTimeInfo::cycleEndPos valid
            kVstTimeSigValid = 1 << 13, ///< VstTimeInfo::timeSigNumerator and VstTimeInfo::timeSigDenominator valid
            kVstSmpteValid = 1 << 14,   ///< VstTimeInfo::smpteOffset and VstTimeInfo::smpteFrameRate valid
            kVstClockValid = 1 << 15    ///< VstTimeInfo::samplesToNextClock valid
            //-------------------------------------------------------------------------------------------------------
        };

        //-------------------------------------------------------------------------------------------------------
        /** SMPTE Frame Rates. */
        //-------------------------------------------------------------------------------------------------------
        public enum VstSmpteFrameRate : Int32
        {
            //-------------------------------------------------------------------------------------------------------
            kVstSmpte24fps = 0,     ///< 24 fps
            kVstSmpte25fps = 1,     ///< 25 fps
            kVstSmpte2997fps = 2,       ///< 29.97 fps
            kVstSmpte30fps = 3,     ///< 30 fps
            kVstSmpte2997dfps = 4,      ///< 29.97 drop
            kVstSmpte30dfps = 5,        ///< 30 drop

            kVstSmpteFilm16mm = 6,      ///< Film 16mm
            kVstSmpteFilm35mm = 7,      ///< Film 35mm
            kVstSmpte239fps = 10,       ///< HDTV: 23.976 fps
            kVstSmpte249fps = 11,       ///< HDTV: 24.976 fps
            kVstSmpte599fps = 12,       ///< HDTV: 59.94 fps
            kVstSmpte60fps = 13     ///< HDTV: 60 fps
            //-------------------------------------------------------------------------------------------------------
        };

        //-------------------------------------------------------------------------------------------------------
        public Double samplePos;               ///< current Position in audio samples (always valid)
        public Double sampleRate;              ///< current Sample Rate in Herz (always valid)
        public Double nanoSeconds;             ///< System Time in nanoseconds (10^-9 second)
        public Double ppqPos;                  ///< Musical Position, in Quarter Note (1.0 equals 1 Quarter Note)
        public Double tempo;                   ///< current Tempo in BPM (Beats Per Minute)
        public Double barStartPos;             ///< last Bar Start Position, in Quarter Note
        public Double cycleStartPos;           ///< Cycle Start (left locator), in Quarter Note
        public Double cycleEndPos;             ///< Cycle End (right locator), in Quarter Note
        public Int32 timeSigNumerator;     ///< Time Signature Numerator (e.g. 3 for 3/4)
        public Int32 timeSigDenominator;   ///< Time Signature Denominator (e.g. 4 for 3/4)
        public Int32 smpteOffset;          ///< SMPTE offset (in SMPTE subframes (bits; 1/80 of a frame)). The current SMPTE position can be calculated using #samplePos, #sampleRate, and #smpteFrameRate.
        public VstSmpteFrameRate smpteFrameRate;       ///< @see VstSmpteFrameRate
        public Int32 samplesToNextClock;   ///< MIDI Clock Resolution (24 Per Quarter Note), can be negative (nearest clock)
        public VstTimeInfoFlags flags;                 ///< @see VstTimeInfoFlags
        //-------------------------------------------------------------------------------------------------------
    };


    //-------------------------------------------------------------------------------------------------------
    /** Variable IO for Offline Processing. */
    //-------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct VstVariableIo
    {
        //-------------------------------------------------------------------------------------------------------
        public IntPtr inputs;                      //< float** input audio buffers
        public IntPtr outputs;                 //< float** output audio buffers
        public Int32 numSamplesInput;          //< number of incoming samples
        public Int32 numSamplesOutput;         //< number of outgoing samples
        public IntPtr numSamplesInputProcessed;    //< Int32* number of samples actually processed of input
        public IntPtr numSamplesOutputProcessed;   //< Int32* number of samples actually processed of output
                                                   //-------------------------------------------------------------------------------------------------------
    };

    //-------------------------------------------------------------------------------------------------------
    /** Structure used for #effEditGetRect. */
    //-------------------------------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct ERect
    {
        //-------------------------------------------------------------------------------------------------------
        public Int16 top;       ///< top coordinate
        public Int16 left;      ///< left coordinate
        public Int16 bottom;    ///< bottom coordinate
        public Int16 right;     ///< right coordinate
        //-------------------------------------------------------------------------------------------------------
    };

}
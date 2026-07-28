// The native NVENC translation unit (src/NvencEncoder.cpp:1160) references
// `extern thread_local char g_errorBuffer[1024];`, which the production DLL
// defines in the AMF TU (src/CinematicRecorderNative.cpp:126). The harness does
// not link that TU, so it supplies the definition here, plus a read accessor
// mirroring the DLL's CR_GetLastError (src/CinematicRecorderNative.cpp:132).
// No name collision: the AMF TU is not part of this binary.

thread_local char g_errorBuffer[1024];

extern "C" const char* CR_GetLastError() {
    return g_errorBuffer;
}

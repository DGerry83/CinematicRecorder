// The native NVENC translation unit (src/NvencEncoder.cpp:1160) references
// `extern thread_local char g_errorBuffer[1024];`, which the production DLL
// defines in the AMF TU (src/CinematicRecorderNative.cpp:126). The harness does
// not link that TU, so it supplies the definition here, plus a read accessor
// mirroring the DLL's CR_GetLastError (src/CinematicRecorderNative.cpp:132).
// It also supplies the shared file-logger entry point CRNativeLog, which the
// production DLL implements in the AMF TU; here it forwards to stderr so
// harness output is still captured.
// No name collision: the AMF TU is not part of this binary.

#include <cstdio>
#include <cstdarg>

thread_local char g_errorBuffer[1024];

extern "C" const char* CR_GetLastError() {
    return g_errorBuffer;
}

extern "C" void CRNativeLog(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    vfprintf(stderr, fmt, args);
    va_end(args);
    fprintf(stderr, "\n");
}

// NVENC zero-copy test harness - CLI, D3D11 context, encode runner.
// Proves (or disproves) the C++ NVENC GPU zero-copy path outside Unity/KSP by
// compiling the real src/NvencEncoder.cpp and feeding it generated textures.
// Exit codes: 0 = PASS, 1 = verify FAIL, 2 = encoder fail, 3 = usage.
#include <windows.h>
#include <d3d11.h>
#include <direct.h>

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <future>
#include <string>
#include <thread>
#include <vector>

#include "../include/NvencEncoder.h" // the real NvencEncoderSettings (no redefinition)
#include "TestPattern.h"
#include "VideoVerifier.h"

// Native export prototypes, declared locally per the architecture contract:
// no new header in the native module, no changes to existing headers.
extern "C" {
    void* CR_InitNvencEncoder(ID3D11Device* unityDevice, ID3D11Texture2D* textureHint,
                              int width, int height, int fps, const char* outputPath,
                              const NvencEncoderSettings* settings);
    int CR_EncodeNvencFrame(void* encoderHandle, ID3D11Texture2D* texture, long long frameIndex);
    int CR_ShutdownNvencEncoder(void* encoderHandle);
    const char* CR_GetLastError(); // provided by HarnessShim.cpp
}

namespace {

constexpr int EXIT_PASS = 0;
constexpr int EXIT_VERIFY_FAIL = 1;
constexpr int EXIT_ENCODER_FAIL = 2;
constexpr int EXIT_USAGE = 3;

struct HarnessConfig {
    int width = 1920;
    int height = 1080;
    int fps = 30;
    int frames = 90;
    PatternPixelFormat format = PatternPixelFormat::BGRA;
    std::string outPath;    // default chosen per mode: out.mkv / reference.mkv
    std::string ffmpegPath; // default: ffmpeg.exe beside this exe
    bool selftest = false;
};

std::string ExeDir() {
    char buf[MAX_PATH] = {};
    GetModuleFileNameA(nullptr, buf, MAX_PATH);
    std::string p(buf);
    size_t slash = p.find_last_of("\\/");
    return slash == std::string::npos ? "." : p.substr(0, slash);
}

void PrintUsage() {
    printf(
        "NVENC zero-copy test harness (CinematicRecorder)\n"
        "Usage: NvencHarness.exe [options]\n"
        "  --width N           frame width   (default 1920)\n"
        "  --height N          frame height  (default 1080)\n"
        "  --fps N             frame rate    (default 30)\n"
        "  --frames N          frame count   (default 90)\n"
        "  --format bgra|rgba  source texture byte order (default bgra = Unity layout,\n"
        "                      exercises F3; rgba exercises the F2 ARGB registration path)\n"
        "  --out FILE.mkv      output file   (default out.mkv; selftest: reference.mkv)\n"
        "  --ffmpeg PATH       ffmpeg.exe    (default: ffmpeg.exe beside this exe)\n"
        "  --selftest          no NVENC: pipe the pattern to ffmpeg (libx264, fallback\n"
        "                      rawvideo) and verify with the identical check chain\n"
        "Exit codes: 0 = PASS, 1 = verify FAIL, 2 = encoder fail, 3 = usage\n");
}

bool ParseInt(const char* s, int& out) {
    if (!s || !*s) return false;
    char* end = nullptr;
    long v = strtol(s, &end, 10);
    if (!end || *end != 0) return false;
    out = (int)v;
    return true;
}

bool ParseArgs(int argc, char** argv, HarnessConfig& cfg) {
    for (int i = 1; i < argc; i++) {
        std::string a = argv[i];
        auto needValue = [&](const char* flag) -> const char* {
            if (i + 1 >= argc) { printf("ERROR: %s needs a value\n", flag); return nullptr; }
            return argv[++i];
        };
        if (a == "--selftest") {
            cfg.selftest = true;
        } else if (a == "--help" || a == "-h") {
            return false;
        } else if (a == "--width") {
            const char* v = needValue("--width");  if (!v || !ParseInt(v, cfg.width))  return false;
        } else if (a == "--height") {
            const char* v = needValue("--height"); if (!v || !ParseInt(v, cfg.height)) return false;
        } else if (a == "--fps") {
            const char* v = needValue("--fps");    if (!v || !ParseInt(v, cfg.fps))    return false;
        } else if (a == "--frames") {
            const char* v = needValue("--frames"); if (!v || !ParseInt(v, cfg.frames)) return false;
        } else if (a == "--out") {
            const char* v = needValue("--out");    if (!v) return false; cfg.outPath = v;
        } else if (a == "--ffmpeg") {
            const char* v = needValue("--ffmpeg"); if (!v) return false; cfg.ffmpegPath = v;
        } else if (a == "--format") {
            const char* v = needValue("--format");
            if (!v) return false;
            if (_stricmp(v, "bgra") == 0) cfg.format = PatternPixelFormat::BGRA;
            else if (_stricmp(v, "rgba") == 0) cfg.format = PatternPixelFormat::RGBA;
            else { printf("ERROR: --format must be bgra or rgba\n"); return false; }
        } else {
            printf("ERROR: unknown argument: %s\n", a.c_str());
            return false;
        }
    }
    if (cfg.width < 64 || cfg.height < 64 || cfg.fps < 1 || cfg.frames < 1) {
        printf("ERROR: width/height >= 64 and fps/frames >= 1 required\n");
        return false;
    }
    if (cfg.ffmpegPath.empty()) cfg.ffmpegPath = ExeDir() + "\\ffmpeg.exe";
    if (cfg.outPath.empty()) cfg.outPath = cfg.selftest ? "reference.mkv" : "out.mkv";
    return true;
}

// Owns the harness D3D11 device/context and the source + staging textures.
// Source texture shape is the documented NVENC-friendly one: USAGE_DEFAULT,
// BIND_RENDER_TARGET|BIND_SHADER_RESOURCE, MipLevels=1, ArraySize=1, no MSAA.
struct D3D11Context {
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    ID3D11Texture2D* source = nullptr;
    ID3D11Texture2D* staging = nullptr;

    ~D3D11Context() { ReleaseAll(); }

    void ReleaseAll() {
        if (staging) staging->Release();
        if (source) source->Release();
        if (context) context->Release();
        if (device) device->Release();
        staging = source = nullptr;
        context = nullptr;
        device = nullptr;
    }

    bool Create(int w, int h, bool rgba, std::string& err) {
        D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_0 };
        HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
                                       D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 1,
                                       D3D11_SDK_VERSION, &device, nullptr, &context);
        if (FAILED(hr)) {
            char b[96];
            snprintf(b, sizeof b, "D3D11CreateDevice failed (0x%08lX)", (unsigned long)hr);
            err = b;
            return false;
        }
        DXGI_FORMAT fmt = rgba ? DXGI_FORMAT_R8G8B8A8_UNORM : DXGI_FORMAT_B8G8R8A8_UNORM;
        D3D11_TEXTURE2D_DESC td = {};
        td.Width = w;
        td.Height = h;
        td.MipLevels = 1;
        td.ArraySize = 1;
        td.Format = fmt;
        td.SampleDesc.Count = 1;
        td.Usage = D3D11_USAGE_DEFAULT;
        td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        hr = device->CreateTexture2D(&td, nullptr, &source);
        if (FAILED(hr)) { err = "CreateTexture2D(source) failed"; return false; }
        td.Usage = D3D11_USAGE_STAGING;
        td.BindFlags = 0;
        td.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        hr = device->CreateTexture2D(&td, nullptr, &staging);
        if (FAILED(hr)) { err = "CreateTexture2D(staging) failed"; return false; }
        return true;
    }

    // CPU pattern -> staging texture -> CopyResource into the DEFAULT source.
    bool Upload(const uint8_t* pixels, int w, int h, std::string& err) {
        D3D11_MAPPED_SUBRESOURCE m = {};
        HRESULT hr = context->Map(staging, 0, D3D11_MAP_WRITE, 0, &m);
        if (FAILED(hr)) { err = "staging Map failed"; return false; }
        for (int y = 0; y < h; y++)
            memcpy((uint8_t*)m.pData + (size_t)y * m.RowPitch, pixels + (size_t)y * (size_t)w * 4, (size_t)w * 4);
        context->Unmap(staging, 0);
        context->CopyResource(source, staging);
        context->Flush();
        return true;
    }
};

// Drives the native export sequence init -> N x encode -> shutdown, exactly one
// encoder instance per process (F16), reporting native errors verbatim.
struct EncodeRunner {
    void* handle = nullptr;

    bool Init(ID3D11Device* device, ID3D11Texture2D* textureHint,
              int w, int h, int fps, const std::string& outPath, std::string& nativeErr) {
        NvencEncoderSettings s = {};
        s.RateControlMode = 0;   // CQP
        s.TargetBitrateKbps = 0;
        s.QpI = 20;
        s.QpP = 20;
        s.QpB = 20;
        s.QualityPreset = 1;     // P4
        s.Codec = 0;             // H.264
        s.GopSize = 30;
        // EnableTAB / EnableCAS / EnableDither / TABSubFrameCount / CASSharpness
        // remain zeroed (F17: ignored by init anyway).
        handle = CR_InitNvencEncoder(device, textureHint, w, h, fps, outPath.c_str(), &s);
        if (!handle) { nativeErr = CR_GetLastError(); return false; }
        return true;
    }

    bool Encode(ID3D11Texture2D* tex, long long index, std::string& nativeErr) {
        if (CR_EncodeNvencFrame(handle, tex, index) != 0) { nativeErr = CR_GetLastError(); return false; }
        return true;
    }

    // F9 guard: shutdown runs on a worker thread; a hang is reported, not waited out.
    // Returns 0 = clean shutdown, 1 = hang (caller must _exit, the thread is stuck).
    int ShutdownGuarded(int timeoutMs) {
        if (!handle) return 0;
        void* h = handle;
        handle = nullptr;
        std::packaged_task<int()> task([h] { return CR_ShutdownNvencEncoder(h); });
        std::future<int> fut = task.get_future();
        std::thread(std::move(task)).detach();
        if (fut.wait_for(std::chrono::milliseconds(timeoutMs)) == std::future_status::timeout) return 1;
        return 0;
    }
};

void PrintReport(const VerifyReport& report) {
    for (const CheckResult& c : report.checks) {
        if (c.name == "ORIENTATION")
            printf("CHECK ORIENTATION: %s\n", c.verdict.c_str()); // literal "ORIENTATION: CORRECT/FLIPPED"
        else
            printf("CHECK %s: %s\n", c.name.c_str(), c.pass ? "PASS" : "FAIL");
        for (const std::string& e : c.evidence) printf("  %s\n", e.c_str());
    }
}

} // namespace

int main(int argc, char** argv) {
    HarnessConfig cfg;
    if (!ParseArgs(argc, argv, cfg)) {
        PrintUsage();
        return EXIT_USAGE;
    }

    printf("NVENC zero-copy test harness\n");
    printf("MODE: %s\n", cfg.selftest ? "SELFTEST (no NVENC)" : "ENCODE (native NVENC path)");
    printf("CONFIG: %dx%d @ %d fps, frames=%d, format=%s, out=%s\n",
           cfg.width, cfg.height, cfg.fps, cfg.frames,
           cfg.format == PatternPixelFormat::BGRA ? "bgra" : "rgba", cfg.outPath.c_str());
    printf("FFMPEG: %s\n", cfg.ffmpegPath.c_str());

    if (GetFileAttributesA(cfg.ffmpegPath.c_str()) == INVALID_FILE_ATTRIBUTES) {
        printf("HARNESS ERROR: ffmpeg.exe not found at %s\n", cfg.ffmpegPath.c_str());
        return EXIT_VERIFY_FAIL;
    }

    const char* workDir = "verify_tmp";
    _mkdir(workDir);

    if (cfg.selftest) {
        std::string usedEncoder, err;
        printf("[SELFTEST] building reference clip (%s) via ffmpeg stdin pipe...\n", cfg.outPath.c_str());
        if (!BuildReferenceClip(cfg.ffmpegPath, workDir, cfg.width, cfg.height, cfg.fps,
                                cfg.frames, cfg.outPath, usedEncoder, err)) {
            printf("HARNESS ERROR: reference clip build failed: %s\n", err.c_str());
            return EXIT_VERIFY_FAIL;
        }
        printf("[SELFTEST] reference clip built with %s\n", usedEncoder.c_str());
        VerifyReport report;
        if (!RunVerification(cfg.ffmpegPath, workDir, cfg.outPath, cfg.width, cfg.height,
                             cfg.frames, report, err)) {
            printf("HARNESS ERROR: verification could not run: %s\n", err.c_str());
            return EXIT_VERIFY_FAIL;
        }
        PrintReport(report);
        printf("VERDICT: %s\n", report.AllPass() ? "PASS" : "FAIL");
        return report.AllPass() ? EXIT_PASS : EXIT_VERIFY_FAIL;
    }

    // ENCODE mode.
    unsigned long long t0 = GetTickCount64();
    D3D11Context d3d;
    std::string err;
    if (!d3d.Create(cfg.width, cfg.height, cfg.format == PatternPixelFormat::RGBA, err)) {
        printf("HARNESS ERROR: %s\n", err.c_str());
        return EXIT_VERIFY_FAIL;
    }
    printf("[ENCODE] D3D11 device + %s source texture created\n",
           cfg.format == PatternPixelFormat::BGRA ? "B8G8R8A8_UNORM" : "R8G8B8A8_UNORM");

    EncodeRunner runner;
    std::string nativeErr;
    if (!runner.Init(d3d.device, d3d.source, cfg.width, cfg.height, cfg.fps, cfg.outPath, nativeErr)) {
        printf("[ENCODE] CR_InitNvencEncoder failed after %llu ms\n", GetTickCount64() - t0);
        printf("NATIVE ERROR: %s\n", nativeErr.c_str());
        return EXIT_ENCODER_FAIL;
    }
    printf("[ENCODE] CR_InitNvencEncoder OK\n");

    std::vector<uint8_t> frame(PatternGenerator::BufferSize(cfg.width, cfg.height));
    for (int i = 0; i < cfg.frames; i++) {
        PatternGenerator::Generate(i, cfg.width, cfg.height, cfg.format, frame.data());
        if (!d3d.Upload(frame.data(), cfg.width, cfg.height, err)) {
            printf("HARNESS ERROR: %s\n", err.c_str());
            runner.ShutdownGuarded(30000);
            return EXIT_VERIFY_FAIL;
        }
        if (!runner.Encode(d3d.source, i, nativeErr)) {
            printf("[ENCODE] CR_EncodeNvencFrame failed at frame %d\n", i);
            printf("NATIVE ERROR: %s\n", nativeErr.c_str());
            runner.ShutdownGuarded(30000);
            return EXIT_ENCODER_FAIL;
        }
    }
    printf("[ENCODE] %d frames submitted\n", cfg.frames);

    if (runner.ShutdownGuarded(30000) == 1) {
        printf("CHECK SHUTDOWN: FAIL (CR_ShutdownNvencEncoder hung > 30 s - F9 drain-loop signature)\n");
        printf("VERDICT: FAIL\n");
        fflush(stdout);
        _exit(EXIT_VERIFY_FAIL); // the hung thread would block a clean exit
    }
    printf("[ENCODE] shutdown clean (%llu ms total)\n", GetTickCount64() - t0);

    VerifyReport report;
    if (!RunVerification(cfg.ffmpegPath, workDir, cfg.outPath, cfg.width, cfg.height,
                         cfg.frames, report, err)) {
        printf("HARNESS ERROR: verification could not run: %s\n", err.c_str());
        return EXIT_VERIFY_FAIL;
    }
    PrintReport(report);
    printf("VERDICT: %s\n", report.AllPass() ? "PASS" : "FAIL");
    return report.AllPass() ? EXIT_PASS : EXIT_VERIFY_FAIL;
}

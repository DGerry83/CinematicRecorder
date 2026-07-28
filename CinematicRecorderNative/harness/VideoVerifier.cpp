#include "VideoVerifier.h"
#include "TestPattern.h"

#include <windows.h>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <regex>

namespace {

// ------------------------------------------------------------ file helpers

bool ReadWholeFile(const std::string& path, std::vector<uint8_t>& data) {
    std::ifstream f(path, std::ios::binary);
    if (!f) return false;
    f.seekg(0, std::ios::end);
    std::streamoff n = f.tellg();
    f.seekg(0, std::ios::beg);
    if (n <= 0) return false;
    data.resize((size_t)n);
    f.read((char*)data.data(), n);
    return f.good() || f.gcount() == n;
}

bool ReadTextFile(const std::string& path, std::string& text) {
    std::vector<uint8_t> d;
    if (!ReadWholeFile(path, d)) return false;
    text.assign((const char*)d.data(), d.size());
    return true;
}

// ------------------------------------------------------- process execution
// CreateProcess directly (no cmd shell). stdout+stderr are merged into a log
// FILE, never an undrained pipe, so the child can never deadlock on a full
// stderr buffer while we write its stdin. Waits for exit, records exit code.

bool RunToLog(const std::string& cmdLine, const std::string& logPath,
              unsigned long& exitCode, std::string& err) {
    SECURITY_ATTRIBUTES sa = {};
    sa.nLength = sizeof(sa);
    sa.bInheritHandle = TRUE;
    HANDLE hLog = CreateFileA(logPath.c_str(), GENERIC_WRITE, FILE_SHARE_READ, &sa,
                              CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hLog == INVALID_HANDLE_VALUE) { err = "cannot create log file " + logPath; return false; }

    STARTUPINFOA si = {};
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESTDHANDLES;
    si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
    si.hStdOutput = hLog;
    si.hStdError = hLog;

    PROCESS_INFORMATION pi = {};
    std::string cmd = cmdLine; // CreateProcessA requires a mutable buffer
    BOOL ok = CreateProcessA(nullptr, cmd.data(), nullptr, nullptr, TRUE, 0, nullptr, nullptr, &si, &pi);
    CloseHandle(hLog);
    if (!ok) {
        err = "CreateProcess failed (win32 " + std::to_string(GetLastError()) + "): " + cmdLine;
        return false;
    }
    CloseHandle(pi.hThread);
    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD code = 1;
    GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hProcess);
    exitCode = code;
    return true;
}

// ------------------------------------------------------------ BMP helpers

uint16_t RD16(const uint8_t* p) { return (uint16_t)(p[0] | (p[1] << 8)); }
uint32_t RD32(const uint8_t* p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

// ------------------------------------------------------------- ffmpeg ops

// libx264 detection: run the encoder help and inspect output; robust to
// whatever exit code this ffmpeg build returns.
bool HasLibX264(const std::string& ffmpeg, const std::string& workDir, bool& present, std::string& err) {
    std::string log = workDir + "\\probe_encoder.log";
    unsigned long code = 0;
    if (!RunToLog("\"" + ffmpeg + "\" -hide_banner -nostdin -h encoder=libx264", log, code, err))
        return false;
    std::string text;
    if (!ReadTextFile(log, text)) { err = "cannot read " + log; return false; }
    present = (text.find("libx264") != std::string::npos) &&
              (text.find("is not recognized") == std::string::npos);
    return true;
}

// ffmpeg -i writes stream info to STDERR; captured to a log file here.
bool ProbeResolution(const std::string& ffmpeg, const std::string& workDir, const std::string& mkv,
                     int& w, int& h, std::string& streamInfo, std::string& err) {
    std::string log = workDir + "\\probe_stream.log";
    unsigned long code = 0;
    if (!RunToLog("\"" + ffmpeg + "\" -hide_banner -nostdin -i \"" + mkv + "\"", log, code, err))
        return false;
    std::string text;
    if (!ReadTextFile(log, text)) { err = "cannot read " + log; return false; }
    size_t vpos = text.find("Video:");
    if (vpos == std::string::npos) { err = "no 'Video:' stream line in ffmpeg -i output"; return false; }
    size_t eol = text.find('\n', vpos);
    streamInfo = text.substr(vpos, eol == std::string::npos ? std::string::npos : eol - vpos);
    while (!streamInfo.empty() && (streamInfo.back() == '\r' || streamInfo.back() == '\n')) streamInfo.pop_back();
    std::smatch m;
    std::regex dimRe("(\\d{2,5})x(\\d{2,5})");
    if (!std::regex_search(streamInfo, m, dimRe)) { err = "no WxH in stream line: " + streamInfo; return false; }
    w = std::stoi(m[1].str());
    h = std::stoi(m[2].str());
    return true;
}

// Extracts the requested source indices to BMP. The select filter numbers its
// outputs positionally (frame_1..frame_K), not by source index; slots map back
// to indices in ascending order. Missing outputs leave invalid FramePixels
// slots (the checks report them as evidence instead of dying here).
bool ExtractFrames(const std::string& ffmpeg, const std::string& workDir, const std::string& mkv,
                   const std::vector<int>& indices, std::vector<FramePixels>& out, std::string& err) {
    std::string sel;
    for (size_t i = 0; i < indices.size(); i++) {
        if (i) sel += "+";
        sel += "eq(n," + std::to_string(indices[i]) + ")";
    }
    std::string log = workDir + "\\extract.log";
    std::string pattern = workDir + "\\frame_%d.bmp";
    std::string cmd = "\"" + ffmpeg + "\" -hide_banner -nostdin -y -i \"" + mkv +
                      "\" -vf select='" + sel + "' -vsync 0 \"" + pattern + "\"";
    unsigned long code = 0;
    if (!RunToLog(cmd, log, code, err)) return false;
    if (code != 0) {
        err = "ffmpeg frame extraction exited " + std::to_string(code) + " (see " + log + ")";
        return false;
    }
    out.assign(indices.size(), FramePixels{});
    int parsed = 0;
    for (size_t i = 0; i < indices.size(); i++) {
        FramePixels fp;
        if (ParseBmpFile(workDir + "\\frame_" + std::to_string(i + 1) + ".bmp", fp, err)) {
            out[i] = std::move(fp);
            parsed++;
        }
    }
    if (parsed == 0) { err = "no frames extracted from " + mkv + " (see " + log + ")"; return false; }
    return true;
}

// --------------------------------------------------------- check helpers

void AvgPatchRGB(const FramePixels& f, int cx, int cy, double& r, double& g, double& b) {
    r = g = b = 0;
    int n = 0;
    for (int y = cy - 2; y <= cy + 2; y++)
        for (int x = cx - 2; x <= cx + 2; x++) {
            if (x < 0 || y < 0 || x >= f.width || y >= f.height) continue;
            const uint8_t* p = &f.rgba[((size_t)y * (size_t)f.width + (size_t)x) * 4];
            r += p[0]; g += p[1]; b += p[2]; n++;
        }
    if (n) { r /= n; g /= n; b /= n; }
}

double Luma(double r, double g, double b) { return 0.299 * r + 0.587 * g + 0.114 * b; }

double PatchLuma(const FramePixels& f, int cx, int cy) {
    double r, g, b;
    AvgPatchRGB(f, cx, cy, r, g, b);
    return Luma(r, g, b);
}

double MeanLuma(const FramePixels& f) {
    if (!f.Valid()) return 0.0;
    double sum = 0;
    long long n = 0;
    for (int y = 0; y < f.height; y += 2)
        for (int x = 0; x < f.width; x += 2) {
            const uint8_t* p = &f.rgba[((size_t)y * (size_t)f.width + (size_t)x) * 4];
            sum += Luma(p[0], p[1], p[2]);
            n++;
        }
    return n ? sum / (double)n : 0.0;
}

void AppendFrameStats(CheckResult& c, const std::vector<FramePixels>& frames, const std::vector<int>& indices) {
    char buf[128];
    for (size_t k = 0; k < frames.size(); k++) {
        if (frames[k].Valid())
            snprintf(buf, sizeof buf, "frameStats: sampled frame %d meanLuma=%.1f", indices[k], MeanLuma(frames[k]));
        else
            snprintf(buf, sizeof buf, "frameStats: sampled frame %d ABSENT from container", indices[k]);
        c.evidence.push_back(buf);
    }
}

// ---------------------------------------------------------------- checks

CheckResult CheckResolution(const std::vector<FramePixels>& frames, const std::vector<int>& indices,
                            int expW, int expH, bool streamParsed, int streamW, int streamH,
                            const std::string& streamInfo) {
    CheckResult c;
    c.name = "RESOLUTION";
    c.pass = true;
    char buf[512];
    if (streamParsed) {
        snprintf(buf, sizeof buf, "ffmpeg -i stream: %dx%d (expected %dx%d)", streamW, streamH, expW, expH);
        c.evidence.push_back(buf);
        c.evidence.push_back("stream line: " + streamInfo);
        if (streamW != expW || streamH != expH) c.pass = false;
    } else {
        c.evidence.push_back("ffmpeg -i stream line could not be parsed: " + streamInfo);
        c.pass = false;
    }
    for (size_t k = 0; k < frames.size(); k++) {
        if (!frames[k].Valid()) {
            snprintf(buf, sizeof buf, "bmp frame %d: ABSENT", indices[k]);
            c.evidence.push_back(buf);
            c.pass = false;
            continue;
        }
        snprintf(buf, sizeof buf, "bmp frame %d: %dx%d (expected %dx%d)",
                 indices[k], frames[k].width, frames[k].height, expW, expH);
        c.evidence.push_back(buf);
        if (frames[k].width != expW || frames[k].height != expH) c.pass = false;
    }
    AppendFrameStats(c, frames, indices);
    return c;
}

CheckResult CheckColor(const std::vector<FramePixels>& frames, const std::vector<int>& indices) {
    CheckResult c;
    c.name = "COLOR";
    c.pass = true;
    char buf[256];
    const int tol = TestPatternSpec::COLOR_TOLERANCE;
    snprintf(buf, sizeof buf, "tolerance: per-channel +/-%d/255 at bar centers, y=%.2f*h",
             tol, TestPatternSpec::BAR_SAMPLE_Y);
    c.evidence.push_back(buf);
    for (size_t k = 0; k < frames.size(); k++) {
        const FramePixels& f = frames[k];
        if (!f.Valid()) {
            snprintf(buf, sizeof buf, "frame %d: ABSENT - cannot sample", indices[k]);
            c.evidence.push_back(buf);
            c.pass = false;
            continue;
        }
        int cy = (int)(f.height * TestPatternSpec::BAR_SAMPLE_Y);
        for (int i = 0; i < TestPatternSpec::BAR_COUNT; i++) {
            int cx = (int)((i + 0.5) / (double)TestPatternSpec::BAR_COUNT * f.width);
            double r, g, b;
            AvgPatchRGB(f, cx, cy, r, g, b);
            const uint8_t* e = TestPatternSpec::BARS[i];
            double worst = (std::max)({ fabs(r - e[0]), fabs(g - e[1]), fabs(b - e[2]) });
            bool ok = worst <= tol;
            if (!ok) c.pass = false;
            snprintf(buf, sizeof buf,
                     "frame %d bar%d %-8s pt(%4d,%4d) expected(%3d,%3d,%3d) measured(%5.0f,%5.0f,%5.0f) maxDiff=%5.0f %s",
                     indices[k], i, TestPatternSpec::BAR_NAMES[i], cx, cy, e[0], e[1], e[2],
                     r, g, b, worst, ok ? "OK" : "FAIL");
            c.evidence.push_back(buf);
        }
    }
    AppendFrameStats(c, frames, indices);
    return c;
}

CheckResult CheckOrientation(const std::vector<FramePixels>& frames, const std::vector<int>& indices) {
    CheckResult c;
    c.name = "ORIENTATION";
    const double BRIGHT = 160.0, DARK = 110.0; // 235-ish white / 16-ish black; 128 gray sits between
    char buf[320];
    const FramePixels& f = frames[0];
    if (!f.Valid()) {
        c.verdict = "INDETERMINATE";
        c.evidence.push_back("sampled frame 0 ABSENT - orientation cannot be measured");
        AppendFrameStats(c, frames, indices);
        return c;
    }
    int w = f.width, h = f.height;
    double body = PatchLuma(f, (int)(TestPatternSpec::BodyX() * w), (int)(TestPatternSpec::BodyY() * h));
    double nTop = PatchLuma(f, (int)(TestPatternSpec::NotchX() * w), (int)(TestPatternSpec::NotchY() * h));
    double nBot = PatchLuma(f, (int)(TestPatternSpec::NotchX() * w), (int)((1.0 - TestPatternSpec::NotchY()) * h));
    snprintf(buf, sizeof buf,
             "probe luma: markerBody(%.3f,%.3f)=%.1f notch(%.3f,%.3f)=%.1f notchVMirror(%.3f,%.3f)=%.1f (bright>%.0f, dark<%.0f)",
             TestPatternSpec::BodyX(), TestPatternSpec::BodyY(), body,
             TestPatternSpec::NotchX(), TestPatternSpec::NotchY(), nTop,
             TestPatternSpec::NotchX(), 1.0 - TestPatternSpec::NotchY(), nBot, BRIGHT, DARK);
    c.evidence.push_back(buf);
    bool bodyBright = body > BRIGHT;
    bool topDark = nTop < DARK, botBright = nBot > BRIGHT;
    bool topBright = nTop > BRIGHT, botDark = nBot < DARK;
    if (bodyBright && topDark && botBright) {
        c.verdict = "CORRECT";
        c.pass = true;
        c.evidence.push_back("notch measured at top of marker column, as generated");
    } else if (bodyBright && topBright && botDark) {
        c.verdict = "FLIPPED";
        c.pass = false;
        c.evidence.push_back("notch measured at BOTTOM of marker column: vertical flip detected");
    } else {
        c.verdict = "INDETERMINATE";
        c.pass = false;
        c.evidence.push_back("marker pattern not recognizable (e.g. blank/black frame) - no orientation claim made");
    }
    AppendFrameStats(c, frames, indices);
    return c;
}

CheckResult CheckMotion(const std::vector<FramePixels>& frames, const std::vector<int>& indices, int expW) {
    CheckResult c;
    c.name = "MOTION";
    c.pass = true;
    char buf[256];
    const int boxSize = TestPatternSpec::BoxSize(expW);
    const int minPixels = (boxSize * boxSize) / 4;
    std::vector<double> centroids;
    std::vector<bool> found;
    for (size_t k = 0; k < frames.size(); k++) {
        const FramePixels& f = frames[k];
        if (!f.Valid()) {
            snprintf(buf, sizeof buf, "frame %d: ABSENT - box not measurable", indices[k]);
            c.evidence.push_back(buf);
            centroids.push_back(0.0);
            found.push_back(false);
            continue;
        }
        int y0 = (int)(f.height * TestPatternSpec::MOTION_BAND_Y0);
        int y1 = (int)(f.height * TestPatternSpec::MOTION_BAND_Y1);
        double sumX = 0;
        int count = 0;
        for (int y = y0; y < y1; y++)
            for (int x = 0; x < f.width; x++) {
                const uint8_t* p = &f.rgba[((size_t)y * (size_t)f.width + (size_t)x) * 4];
                int r = p[0], g = p[1], b = p[2];
                if (abs(r - TestPatternSpec::BOX_GRAY) <= 40 &&
                    abs(g - TestPatternSpec::BOX_GRAY) <= 40 &&
                    abs(b - TestPatternSpec::BOX_GRAY) <= 40 &&
                    (std::max)({ abs(r - g), abs(g - b), abs(r - b) }) <= 32) {
                    sumX += x;
                    count++;
                }
            }
        if (count >= minPixels) {
            centroids.push_back(sumX / count);
            found.push_back(true);
            snprintf(buf, sizeof buf, "frame %d: grayPixels=%d centroidX=%.1f", indices[k], count, centroids.back());
        } else {
            centroids.push_back(0.0);
            found.push_back(false);
            snprintf(buf, sizeof buf, "frame %d: grayPixels=%d (< %d) - moving box not found",
                     indices[k], count, minPixels);
        }
        c.evidence.push_back(buf);
    }
    bool allFound = true;
    for (bool fnd : found) allFound = allFound && fnd;
    bool increasing = true;
    for (size_t k = 1; k < centroids.size(); k++)
        if (!(centroids[k] > centroids[k - 1])) increasing = false;
    c.pass = allFound && increasing && found.size() >= 2;
    if (c.pass)
        c.evidence.push_back("centroidX strictly increasing left->right: frame ordering intact");
    else
        c.evidence.push_back("expected gray-box centroidX strictly increasing across sampled frames");
    AppendFrameStats(c, frames, indices);
    return c;
}

} // namespace

// ------------------------------------------------------------ BMP parsing

bool ParseBmpFile(const std::string& path, FramePixels& out, std::string& err) {
    std::vector<uint8_t> d;
    if (!ReadWholeFile(path, d)) { err = "cannot read " + path; return false; }
    if (d.size() < 54 || d[0] != 'B' || d[1] != 'M') { err = path + ": not a BMP"; return false; }
    uint32_t pixelOfs = RD32(&d[10]);
    uint32_t hdrSize = RD32(&d[14]);
    if (hdrSize < 40 || d.size() < 14ull + hdrSize) { err = path + ": unsupported DIB header"; return false; }
    int32_t w = (int32_t)RD32(&d[18]);
    int32_t hSigned = (int32_t)RD32(&d[22]);
    uint16_t bpp = RD16(&d[28]);
    uint32_t compression = RD32(&d[30]);
    if (w <= 0 || hSigned == 0) { err = path + ": degenerate dimensions"; return false; }
    if (compression != 0) { err = path + ": compressed BMP unsupported"; return false; }
    if (bpp != 24 && bpp != 32) { err = path + ": bpp " + std::to_string(bpp) + " unsupported (want 24/32)"; return false; }
    int h = hSigned < 0 ? -hSigned : hSigned;
    bool topDown = hSigned < 0; // honor biHeight sign; never assume bottom-up
    size_t stride = ((size_t)w * bpp / 8 + 3) & ~(size_t)3;
    if ((uint64_t)pixelOfs + (uint64_t)stride * (uint64_t)h > d.size()) { err = path + ": truncated pixel data"; return false; }

    out.width = w;
    out.height = h;
    out.rgba.assign((size_t)w * (size_t)h * 4, 0);
    int bytesPerPx = bpp / 8;
    for (int row = 0; row < h; row++) {
        int y = topDown ? row : (h - 1 - row); // file row -> image row, y=0 is top
        const uint8_t* src = d.data() + pixelOfs + (size_t)row * stride;
        uint8_t* dst = out.rgba.data() + (size_t)y * (size_t)w * 4;
        for (int x = 0; x < w; x++) {
            dst[x * 4 + 0] = src[x * bytesPerPx + 2]; // stored BGR(A) -> RGBA
            dst[x * 4 + 1] = src[x * bytesPerPx + 1];
            dst[x * 4 + 2] = src[x * bytesPerPx + 0];
            dst[x * 4 + 3] = 255;
        }
    }
    return true;
}

// ------------------------------------------------- reference clip builder

bool BuildReferenceClip(const std::string& ffmpegPath, const std::string& workDir,
                        int width, int height, int fps, int frames,
                        const std::string& outMkv, std::string& usedEncoder, std::string& err) {
    bool hasX264 = false;
    if (!HasLibX264(ffmpegPath, workDir, hasX264, err)) return false;
    usedEncoder = hasX264 ? "libx264 -qp 18" : "rawvideo (libx264 absent - fallback)";
    std::string vcodec = hasX264 ? "-c:v libx264 -qp 18" : "-c:v rawvideo";
    std::string log = workDir + "\\reference_build.log";
    std::string cmd = "\"" + ffmpegPath + "\" -hide_banner -y"
        + " -f rawvideo -pix_fmt bgra -s " + std::to_string(width) + "x" + std::to_string(height)
        + " -r " + std::to_string(fps) + " -i - " + vcodec + " \"" + outMkv + "\"";

    SECURITY_ATTRIBUTES sa = {};
    sa.nLength = sizeof(sa);
    sa.bInheritHandle = TRUE;
    HANDLE hChildRd = nullptr, hParentWr = nullptr;
    if (!CreatePipe(&hChildRd, &hParentWr, &sa, 0)) { err = "CreatePipe failed"; return false; }
    if (!SetHandleInformation(hParentWr, HANDLE_FLAG_INHERIT, 0)) {
        err = "SetHandleInformation failed";
        CloseHandle(hChildRd);
        CloseHandle(hParentWr);
        return false;
    }
    HANDLE hLog = CreateFileA(log.c_str(), GENERIC_WRITE, FILE_SHARE_READ, &sa,
                              CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hLog == INVALID_HANDLE_VALUE) {
        err = "cannot create log file " + log;
        CloseHandle(hChildRd);
        CloseHandle(hParentWr);
        return false;
    }

    STARTUPINFOA si = {};
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESTDHANDLES;
    si.hStdInput = hChildRd;
    si.hStdOutput = hLog;
    si.hStdError = hLog;
    PROCESS_INFORMATION pi = {};
    std::string mutableCmd = cmd;
    if (!CreateProcessA(nullptr, mutableCmd.data(), nullptr, nullptr, TRUE, 0, nullptr, nullptr, &si, &pi)) {
        err = "CreateProcess failed (win32 " + std::to_string(GetLastError()) + "): " + cmd;
        CloseHandle(hChildRd);
        CloseHandle(hParentWr);
        CloseHandle(hLog);
        return false;
    }
    CloseHandle(hChildRd);
    CloseHandle(hLog);

    // Pipe frames: pattern generated per frame, BGRA rawvideo, chunked writes.
    bool writeOk = true;
    std::vector<uint8_t> frame(PatternGenerator::BufferSize(width, height));
    for (int i = 0; i < frames && writeOk; i++) {
        PatternGenerator::Generate(i, width, height, PatternPixelFormat::BGRA, frame.data());
        size_t off = 0;
        while (off < frame.size()) {
            DWORD written = 0;
            DWORD chunk = (DWORD)std::min<size_t>(frame.size() - off, (size_t)(1u << 20));
            if (!WriteFile(hParentWr, frame.data() + off, chunk, &written, nullptr) || written == 0) {
                writeOk = false;
                break;
            }
            off += written;
        }
    }
    CloseHandle(hParentWr);
    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD code = 1;
    GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    if (!writeOk) { err = "broken pipe writing raw frames to ffmpeg stdin"; return false; }
    if (code != 0) { err = "ffmpeg reference encode exited " + std::to_string(code) + " (see " + log + ")"; return false; }
    return true;
}

// ------------------------------------------------------- verification run

bool RunVerification(const std::string& ffmpegPath, const std::string& workDir,
                     const std::string& mkvPath, int width, int height, int frameCount,
                     VerifyReport& report, std::string& err) {
    // Sample frames 0, N/2, N-1 (deduplicated, ascending).
    std::vector<int> indices;
    indices.push_back(0);
    int mid = frameCount / 2, last = frameCount - 1;
    if (mid != 0) indices.push_back(mid);
    if (last != mid && last != 0) indices.push_back(last);

    // Stream probe failure is recorded by the resolution check, not infra-fatal.
    int streamW = 0, streamH = 0;
    std::string streamInfo;
    std::string probeErr;
    bool streamParsed = ProbeResolution(ffmpegPath, workDir, mkvPath, streamW, streamH, streamInfo, probeErr);
    if (!streamParsed) streamInfo = probeErr;

    std::vector<FramePixels> frames;
    if (!ExtractFrames(ffmpegPath, workDir, mkvPath, indices, frames, err)) return false;

    report.checks.clear();
    report.checks.push_back(CheckResolution(frames, indices, width, height,
                                            streamParsed, streamW, streamH, streamInfo));
    report.checks.push_back(CheckColor(frames, indices));
    report.checks.push_back(CheckOrientation(frames, indices));
    report.checks.push_back(CheckMotion(frames, indices, width));
    return true;
}

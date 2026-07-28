#pragma once
// Verification chain types for the NVENC zero-copy harness.
#include <cstdint>
#include <string>
#include <vector>

// One extracted frame, normalized: top-down rows (row 0 = top), 4 bytes/px R,G,B,A.
struct FramePixels {
    int width = 0;
    int height = 0;
    std::vector<uint8_t> rgba;
    bool Valid() const { return width > 0 && height > 0 && rgba.size() == (size_t)width * (size_t)height * 4; }
};

struct CheckResult {
    std::string name;                // RESOLUTION / COLOR / ORIENTATION / MOTION
    bool pass = false;
    std::string verdict;             // ORIENTATION only: CORRECT / FLIPPED / INDETERMINATE
    std::vector<std::string> evidence;
};

struct VerifyReport {
    std::vector<CheckResult> checks;
    bool AllPass() const {
        for (const CheckResult& c : checks) if (!c.pass) return false;
        return true;
    }
};

// Parses 24/32-bit uncompressed BMP. Row order is derived from the biHeight
// sign (negative = top-down); never assumes bottom-up.
bool ParseBmpFile(const std::string& path, FramePixels& out, std::string& err);

// Selftest reference clip: pipes BGRA rawvideo pattern frames to ffmpeg stdin
// ("-f rawvideo -pix_fmt bgra -s WxH -r fps -i -"). Uses "-c:v libx264 -qp 18"
// when the shipped ffmpeg has libx264 (detected by running it, not assumed),
// else falls back to "-c:v rawvideo". usedEncoder reports which was used.
// ffmpeg stdout/stderr go to a log file (never an undrained pipe -> no deadlock).
bool BuildReferenceClip(const std::string& ffmpegPath, const std::string& workDir,
                        int width, int height, int fps, int frames,
                        const std::string& outMkv, std::string& usedEncoder, std::string& err);

// Probes stream info (ffmpeg -i, stderr captured), extracts frames
// {0, N/2, N-1} via the select filter (-vsync 0, positionally numbered BMPs
// mapped back to sample indices in ascending order), parses the BMPs, and runs
// all four checks. Returns false only on harness-infra failure (err set);
// otherwise the report is filled (individual checks may still FAIL).
bool RunVerification(const std::string& ffmpegPath, const std::string& workDir,
                     const std::string& mkvPath, int width, int height, int frameCount,
                     VerifyReport& report, std::string& err);

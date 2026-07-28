#pragma once
// Test pattern definition for the NVENC zero-copy harness.
// Single source of truth: PatternGenerator (TestPattern.cpp) and the verifier
// (VideoVerifier.cpp) share these constants so the two can never drift apart.
// All geometry is fractional (resolution-independent).
#include <cstddef>
#include <cstdint>

// Byte order of generated frames and of the D3D11 source texture.
// BGRA mimics the Unity render texture layout (exercises F3: blind CopyResource
// into the R8G8B8A8 encode texture). RGBA matches the encode texture format
// (exercises F2: NV_ENC_BUFFER_FORMAT_ARGB registration mismatch -> R/B swap).
enum class PatternPixelFormat { BGRA, RGBA };

struct TestPatternSpec {
    // 7 vertical bars, classic ordering (makes an R/B swap obvious).
    static constexpr int BAR_COUNT = 7;
    static constexpr uint8_t BARS[BAR_COUNT][3] = {
        {255, 255, 255}, {255, 255, 0}, {0, 255, 255}, {0, 255, 0},
        {255, 0, 255}, {255, 0, 0}, {0, 0, 255}
    };
    static constexpr const char* BAR_NAMES[BAR_COUNT] = {
        "WHITE", "YELLOW", "CYAN", "GREEN", "MAGENTA", "RED", "BLUE"
    };

    // Orientation marker: solid white block occupying the top-left ~12%, with a
    // black notch offset in both axes so flip vs mirror vs rotation differ.
    static constexpr double MARKER_W = 0.12;
    static constexpr double MARKER_H = 0.12;
    static constexpr double NOTCH_X0 = 0.07, NOTCH_X1 = 0.10;
    static constexpr double NOTCH_Y0 = 0.025, NOTCH_Y1 = 0.055;

    // Moving box: 32 px @1920 wide, travels left->right at 16 px/frame @1920,
    // vertically centered, neutral gray (no bar is gray, achromatic = immune to
    // 4:2:0 chroma bleed and to R/B swap).
    static constexpr int BOX_GRAY = 128;

    // Sampling / tolerances.
    static constexpr double BAR_SAMPLE_Y = 0.85;   // bar sample row (clears marker and box)
    static constexpr int COLOR_TOLERANCE = 48;     // per-channel +/- out of 255
    static constexpr double MOTION_BAND_Y0 = 0.45, MOTION_BAND_Y1 = 0.55;

    static int BoxSize(int w) { int s = w / 60; return s < 8 ? 8 : s; }
    static int BoxSpeed(int w) { int s = w / 120; return s < 1 ? 1 : s; }
    static int BoxX(int frame, int w) { return (frame * BoxSpeed(w)) % w; }
    static int BoxY(int w, int h) { return h / 2 - BoxSize(w) / 2; }

    // Orientation probe points (fractions of width/height).
    static constexpr double BodyX() { return 0.03; }   // inside marker, outside notch
    static constexpr double BodyY() { return 0.08; }
    static constexpr double NotchX() { return (NOTCH_X0 + NOTCH_X1) / 2.0; }
    static constexpr double NotchY() { return (NOTCH_Y0 + NOTCH_Y1) / 2.0; }
};

class PatternGenerator {
public:
    // Fills `buffer` (BufferSize bytes) with frame `frameIndex` of the pattern.
    static void Generate(int frameIndex, int width, int height,
                         PatternPixelFormat format, uint8_t* buffer);
    static size_t BufferSize(int w, int h) { return (size_t)w * (size_t)h * 4; }
};

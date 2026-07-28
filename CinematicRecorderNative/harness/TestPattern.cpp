#include "TestPattern.h"

// Renders: 7 vertical color bars, the top-left orientation marker (white block
// + black notch), and the moving gray box. Draw order: bars -> marker -> notch
// -> box (box on top; it never overlaps marker/notch at y = h/2).
void PatternGenerator::Generate(int frameIndex, int width, int height,
                                PatternPixelFormat format, uint8_t* buffer) {
    const int markerW = (int)(width * TestPatternSpec::MARKER_W);
    const int markerH = (int)(height * TestPatternSpec::MARKER_H);
    const int notchX0 = (int)(width * TestPatternSpec::NOTCH_X0);
    const int notchX1 = (int)(width * TestPatternSpec::NOTCH_X1);
    const int notchY0 = (int)(height * TestPatternSpec::NOTCH_Y0);
    const int notchY1 = (int)(height * TestPatternSpec::NOTCH_Y1);
    const int boxSize = TestPatternSpec::BoxSize(width);
    const int boxX = TestPatternSpec::BoxX(frameIndex, width);
    const int boxY = TestPatternSpec::BoxY(width, height);

    for (int y = 0; y < height; y++) {
        uint8_t* row = buffer + (size_t)y * (size_t)width * 4;
        for (int x = 0; x < width; x++) {
            int bar = (int)((long long)x * TestPatternSpec::BAR_COUNT / width);
            if (bar >= TestPatternSpec::BAR_COUNT) bar = TestPatternSpec::BAR_COUNT - 1;
            uint8_t r = TestPatternSpec::BARS[bar][0];
            uint8_t g = TestPatternSpec::BARS[bar][1];
            uint8_t b = TestPatternSpec::BARS[bar][2];

            if (x < markerW && y < markerH) { r = g = b = 255; }
            if (x >= notchX0 && x < notchX1 && y >= notchY0 && y < notchY1) { r = g = b = 0; }
            if (x >= boxX && x < boxX + boxSize && y >= boxY && y < boxY + boxSize) {
                r = g = b = (uint8_t)TestPatternSpec::BOX_GRAY;
            }

            uint8_t* p = row + (size_t)x * 4;
            if (format == PatternPixelFormat::BGRA) { p[0] = b; p[1] = g; p[2] = r; }
            else                                    { p[0] = r; p[1] = g; p[2] = b; }
            p[3] = 255;
        }
    }
}

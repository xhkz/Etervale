# 021. The macOS window renders at the display's full (Retina) resolution

Parts: runtime, framework (interface version).

## Symptom

On a Retina display every frame looked soft: text, the HUD and the 3D scene were drawn at half the display's
resolution and stretched 2x by macOS. A 1280 x 720 window on a 5120 x 2880 display ("looks like" 2560 x 1440) had a
1280 x 720 swapchain covering 2560 x 1440 physical pixels.

## Cause

SDL2 works in points on macOS. The runtime created its window without `SDL_WINDOW_ALLOW_HIGHDPI`, so the window's
Metal layer kept a contents scale of 1 and its drawable had the window's size in points. The framework takes the
back buffer, the window size and the mouse as one unit, so asking for the pixel size alone would have mixed points and
pixels.

## Change

`src/sdl/MGP_sdl.cpp`, `src/include/mg_common.h`, `src/vulkan/MGG_Vulkan.cpp`:

- On macOS the window is created with `SDL_WINDOW_ALLOW_HIGHDPI`; its drawable, and so the swapchain the framework
  sizes from the back buffer, is in pixels.
- Every size, position and mouse coordinate the runtime exchanges is in pixels. The runtime converts at its edge: a
  window's sizes (creation, `MGP_Window_SetClientSize`, resize events) and its mouse (motion, buttons,
  `MGP_Mouse_WarpPosition`) by that window's density (`SDL_GetWindowSizeInPixels` over `SDL_GetWindowSize`); window
  positions (`MGP_Window_GetPosition`/`SetPosition`, move events) and the display modes and current mode of the
  adapter by the main display's density (`mg_display_pixel_density`, from its CoreGraphics display mode). A change of
  backing scale (the window moving to another display) arrives as a resize event in pixels.
- Elsewhere the density is 1 and nothing changes: SDL's units on Windows and Linux are already the drawable's.
- The interface version is 8 in both halves (issue 002): the exports keep their signatures, but their units changed on
  macOS, and a framework must not run with a runtime that reports points.

The framework is unchanged otherwise. A size a game asks for (a back buffer, a window) is in pixels; on a 2x display a
1280 x 720 window is 640 x 360 points.

## Test

macOS 26 on Apple silicon (M5), two 5120 x 2880 displays at 2x: `build.sh` builds the runtime; the client asked for a
1280 x 720 window, got a 1280 x 720 back buffer, and its window measured 640 x 360 points on screen
(`CGWindowListCopyWindowInfo`). The client's regression suite passes. Not verified here: the image itself (no screen
capture), mouse picking against the pixel back buffer, exclusive full screen, borderless, and moving the window between
displays of different scale; Windows and Linux were not rebuilt (their code path is unchanged: density 1).

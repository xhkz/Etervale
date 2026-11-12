> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI. This note was written by an AI agent, so what can
> go upstream is an issue or a fix written by a human. The facts below are the material for that.

# The Vulkan runtime renders at half resolution on Retina displays

**Where:** `native/monogame/sdl/MGP_sdl.cpp`, the window creation flags (MonoGame v3.8.6).

**Problem:** the SDL window is created without `SDL_WINDOW_ALLOW_HIGHDPI`, so on a macOS Retina display the swapchain
has the window's size in points and macOS stretches it 2x: every frame is blurry.

**Fix:** create the window with `SDL_WINDOW_ALLOW_HIGHDPI` on macOS, and convert at the runtime's edge between SDL's
points and the pixels MonoGame's back buffer uses: window sizes and mouse coordinates by the window's density
(`SDL_GetWindowSizeInPixels` / `SDL_GetWindowSize`), window positions and display modes by the display's. A game then
asks for, and receives, sizes in pixels.

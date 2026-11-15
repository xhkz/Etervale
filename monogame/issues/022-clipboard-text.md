# 022. The clipboard's text

Parts: runtime, framework.

## Need

Etervale's chat line takes Ctrl+V (Cmd+V on macOS) to paste. MonoGame offers no clipboard, and a window's text input
delivers only typed characters, so the game could not read what the player copied.

## Cause

The runtime exposes none of SDL's clipboard functions, and `GameWindow` has no member for the clipboard.

## Change

**Runtime** (`src/sdl/MGP_sdl.cpp`): `MGP_Clipboard_GetText(buffer, capacity)` returns the clipboard text's length in
UTF-8 bytes, 0 when it holds no text (`SDL_HasClipboardText`), and copies as much of it as fits into the buffer,
zero-terminated (`SDL_GetClipboardText`, freed with `SDL_free`), on every platform SDL drives.

**Framework** (`GameWindow.cs`, `Platform/Native/GameWindow.Native.cs`, `Platform/Native/Platform.Interop.cs`):
`GameWindow.GetClipboardText()`, virtual and empty on other platforms, implemented by the Native window: it reads into a
256-byte buffer and reads again into one that holds the whole text when it is longer, then decodes it as UTF-8.

Interface version 9 (issue 002).

## Test

Etervale's client on macOS: text copied in another application pastes into the chat line with Cmd+V; a line break in it
becomes a space and a text longer than the line stops at its 78 characters. `ChatClientTests` covers the chat side with a
clipboard stand-in.

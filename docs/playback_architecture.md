# Playback Architecture

## 1. Engine Abstraction Boundary
The UI layer coordinates with an `IPlaybackEngine` interface. All LibVLCSharp details (Native library initialization, Media objects, MediaPlayers) are encapsulated inside `LibVlcPlaybackEngine : IPlaybackEngine`.
- ViewModels inject `IPlaybackEngine` and subscribe to standard .NET events (`StateChanged`, `PositionChanged`, `ErrorEncountered`).

## 2. Threading: DispatcherQueue Marshalling
**Rule:** `LibVLCSharp` pumps media events on non-UI threads. 
ALL UI-bound property updates inside ViewModels MUST be marshalled.
```csharp
_dispatcher.TryEnqueue(() => { 
    PlaybackPosition = pos; 
});
```

## 3. CancellationTokens
All background operations (Network requests to load a stream chunk, fetching VOD metadata) must take a `CancellationToken`. When the user rapidly changes channels, the previous cancellation token will cancel pending operations instantly to conserve bandwidth and prevent thread locking.

## 4. Policy: Buffering / Reconnect / Error
- **Timeout**: If LibVLC gets stuck in `Loading` state for > 15 seconds, abort and throw `ConnectionTimeoutException`.
- **Reconnect**: On unexpected stream drop, automatically attempt reconnection up to 3 times before displaying hard error.
- **Error Recovery**: Explicit log hook capturing VLC's `stderr` equivalents for precise diagnosis in settings.

## 5. Input State Machine & Fullscreen
The media overlay sits top-level inside the WinUI 3 Window.
- **Double Click**: Toggles Fullscreen state.
- **Escape**: Exits Fullscreen. Modifies AppWindow to overlapped presentation.
- **F11**: Toggles Fullscreen. Modifies AppWindow to full-screen presentation.
- **Pointer Visibility**: Pointer is hidden after 3000ms idle in `Playing` state. Reappears on `PointerMoved`.

## 6. Multi-Monitor & Window Restore
The application will listen to `DisplayInformation` events. If the display scale changes or the window is dragged to a different monitor, the player must suspend its rendering texture, update its D3D11 swapchain configuration, and resume seamlessly.

## 7. Media Handoff Contract
If hardware decoding fails or format is unsupported natively, the `IPlaybackEngine` attempts to resolve `ExternalPlayerHandoff`. It opens VLC.exe or MPC-HC.exe with the stream URL as an argument, keeping KROIRA in a "Paused/Delegated" state until the external process exits.

## 8. Track Switching
Dedicated UI pickers exposed by parsing the native engine's available Subtitle Layouts and Audio Device pipelines. Updates handled synchronously without restarting the stream.

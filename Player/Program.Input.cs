using System;
using System.Numerics;
using Silk.NET.Input;
using SilkWindows;
using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.SystemUi;

namespace T3.Player;

internal static partial class Program
{
    private static void InitializeInput(SilkRenderWindow window)
    {
        // Track keyboard via Silk.NET input
        foreach (var keyboard in window.Keyboards)
        {
            keyboard.KeyDown += (kb, key, scancode) =>
                                {
                                    var vk = SilkKeyMap.ToVirtualKey(key);
                                    if (vk != 0)
                                        KeyHandler.SetKeyDown(vk);
                                };
            keyboard.KeyUp += OnKeyUp;
        }

        // Track mouse via Silk.NET input
        foreach (var mouse in window.Mice)
        {
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += (m, btn) =>
                               {
                                   if (btn == MouseButton.Left)
                                       _mouseLeftDown = true;
                               };
            mouse.MouseUp += (m, btn) =>
                             {
                                 if (btn == MouseButton.Left)
                                     _mouseLeftDown = false;
                             };
        }
    }

    private static void OnKeyUp(IKeyboard keyboard, Key key, int scancode)
    {
        var vk = SilkKeyMap.ToVirtualKey(key);
        if (vk != 0)
            KeyHandler.SetKeyUp(vk);

        var coreUi = CoreUi.Instance;

        // Alt+Enter for fullscreen toggle
        if (_resolvedOptions.Windowed && SilkKeyMap.IsAlt(key))
        {
            // Check if Enter was also just pressed (handled via key state)
        }

        if (key == Key.Enter && keyboard.IsKeyPressed(Key.AltLeft))
        {
            _swapChain.IsFullScreen = !_swapChain.IsFullScreen;
            RebuildBackBuffer(_renderWindow, _device, ref _renderView, ref _backBuffer, _swapChain);
            coreUi.Cursor.SetVisible(!_swapChain.IsFullScreen);
        }

        var currentPlayback = Playback.Current;
        if (ProjectSettings.Config.EnablePlaybackControlWithKeyboard)
        {
            switch (key)
            {
                case Key.Left:
                    currentPlayback.TimeInBars -= 4;
                    break;
                case Key.Right:
                    currentPlayback.TimeInBars += 4;
                    break;
                case Key.Space:
                    currentPlayback.PlaybackSpeed = Math.Abs(currentPlayback.PlaybackSpeed) > 0.01f ? 0 : 1;
                    break;
            }
        }

        if (key == Key.Escape)
        {
            coreUi.ExitApplication();
        }
    }

    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        var windowWidth = _renderWindow.ClientWidth;
        var windowHeight = _renderWindow.ClientHeight;

        if (windowWidth <= 0 || windowHeight <= 0)
            return;

        var relativePosition = new Vector2(position.X / windowWidth,
                                           position.Y / windowHeight);

        MouseInput.Set(relativePosition, _mouseLeftDown);
    }

    private static bool _mouseLeftDown;
}

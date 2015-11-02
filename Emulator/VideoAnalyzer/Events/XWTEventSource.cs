//
// Copyright (c) Antmicro
// Copyright (c) Realtime Embedded
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using System;
using Xwt;
using Emul8.Logging;
using Emul8.Extensions.Analyzers.Video.Handlers;

namespace Emul8.Extensions.Analyzers.Video.Events
{
    internal class XWTEventSource : IEventSource
    {
        public XWTEventSource(Widget source)
        {
            this.source = source;
        }

        public void AttachHandler(IOHandler h)
        {
            handler = h;

            source.MouseMoved += HandleMouseMoved;
            source.ButtonPressed += HandleButtonPressed;
            source.ButtonReleased += HandleButtonReleased;
            source.KeyPressed += HandleKeyPressed;
            source.KeyReleased += HandleKeyReleased;
        }

        private void HandleKeyReleased(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var entryKey = Gdk.Keymap.Default.GetEntriesForKeyval((uint)e.Key)[0].Keycode;

            var key = X11ToKeyScanCodeConverter.Instance.GetScanCode((int)entryKey);
            if(key != null)
            {
                handler.KeyReleased(key.Value);
                return;
            }

            Logger.LogAs(this, LogLevel.Warning, "Unhandled keycode: {0}", entryKey);
        }

        private void HandleKeyPressed(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var entryKey = Gdk.Keymap.Default.GetEntriesForKeyval((uint)e.Key)[0].Keycode;

            var key = X11ToKeyScanCodeConverter.Instance.GetScanCode((int)entryKey);
            if(key != null)
            {
                handler.KeyPressed(key.Value);
                return;
            }

            Logger.LogAs(this, LogLevel.Warning, "Unhandled keycode: {0}", entryKey);
        }

        private void HandleButtonReleased(object sender, ButtonEventArgs e)
        {
            if(!e.Handled)
            {
                handler.ButtonReleased(e.Button);
                e.Handled = true;
            }
        }

        private void HandleButtonPressed(object sender, ButtonEventArgs e)
        {
            if(!e.Handled)
            {
                handler.ButtonPressed(e.Button);
                e.Handled = true;
            }
        }

        private void HandleMouseMoved(object sender, MouseMovedEventArgs e)
        {
            if(lastX == null || lastY == null)
            {
                lastX = (int)e.X;
                lastY = (int)e.Y;
                return;
            }

            handler.MouseMoved((int)e.X, (int)e.Y, lastX.Value - (int)e.X, lastY.Value - (int)e.Y);
            lastX = (int)e.X;
            lastY = (int)e.Y;
        }

        public void DetachHandler()
        {
            source.MouseMoved -= HandleMouseMoved;
            source.ButtonPressed -= HandleButtonPressed;
            source.ButtonReleased -= HandleButtonReleased;
            source.KeyPressed -= HandleKeyPressed;
            source.KeyReleased -= HandleKeyReleased;

            handler = null;
        }

        private Widget source;
        private IOHandler handler;

        private int? lastX;
        private int? lastY;

        public int X { get { return lastX ?? 0; } }
        public int Y { get { return lastY ?? 0; } }
    }
}

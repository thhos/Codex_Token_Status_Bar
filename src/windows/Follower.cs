using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace CodexPetCredits {
    // Geometry travels separately from account data; native pointer motion stays on the UI timer.
    internal sealed class AttachmentFollower {
        private readonly AttachmentPlacement placement = new AttachmentPlacement();
        private object geometry;
        private DateTime observedAt = DateTime.MinValue, lastDiscovery = DateTime.MinValue, missingSince = DateTime.MinValue, releasedUntil = DateTime.MinValue;
        private bool visible, seenCodex, first = true, mouseWasDown, pressedOnPet, transparent;
        private Point press, offset, releasedPoint;
        private Rect lastRect = Rect.Empty;
        private double x, y;
        private int placedX = Int32.MinValue, placedY = Int32.MinValue;
        private sealed class Discovery { public int Count; public IntPtr Handle; }
        private Task<Discovery> discovery;
        private AttachmentTransition transition;
        private DateTime transitionStarted;
        private Point transitionAnchor, transitionTarget;
        public IntPtr PetHandle { get; private set; }
        public bool Dragging { get; private set; }
        public void Observe(object data) {
            var next = Json.Get(data, "pet"); double timestamp = Json.Number(data, "observedAt");
            if (next != null && timestamp > 0) { geometry = next; observedAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(timestamp); visible = Json.Flag(data, "visible"); }
            else if ((DateTime.UtcNow - observedAt).TotalMilliseconds > 1200) visible = false;
        }
        public void Tick(Window window, IntPtr handle) {
            var now = DateTime.UtcNow; Native.Point cursor; Native.GetCursorPos(out cursor);
            var mouse = new Point(cursor.X, cursor.Y); bool down = (Native.GetAsyncKeyState(1) & 0x8000) != 0;
            if (down && !mouseWasDown) { pressedOnPet = !lastRect.IsEmpty && lastRect.Contains(mouse); press = mouse; offset = lastRect.IsEmpty ? new Point() : new Point(mouse.X - lastRect.X, mouse.Y - lastRect.Y); }
            if (down && pressedOnPet && (mouse - press).Length > 3) Dragging = true;
            if (!down && mouseWasDown) { if (Dragging) { releasedUntil = now.AddMilliseconds(100); releasedPoint = mouse; } Dragging = false; pressedOnPet = false; }
            mouseWasDown = down;
            if (transparent != Dragging) { long style = Native.GetWindowLongPtr(handle, -20).ToInt64(); Native.SetWindowLongPtr(handle, -20, new IntPtr(Dragging ? style | 0x20 : style & ~0x20)); transparent = Dragging; }
            // Enumerating process modules costs several milliseconds; never do it on the rendering thread.
            if (discovery != null && discovery.IsCompleted) {
                var result = discovery.IsFaulted ? null : discovery.Result; discovery = null;
                if (result != null && result.Count > 0) { seenCodex = true; missingSince = DateTime.MinValue; }
                else { if (missingSince == DateTime.MinValue) missingSince = now; if ((seenCodex || (now - missingSince).TotalSeconds > 30) && (now - missingSince).TotalSeconds > 3) { window.Close(); return; } }
                if (result != null && result.Handle != IntPtr.Zero) PetHandle = result.Handle;
            }
            if (discovery == null && (now - lastDiscovery).TotalMilliseconds >= (PetHandle == IntPtr.Zero ? 500 : 2000)) {
                lastDiscovery = now;
                discovery = Task.Run(delegate { var ids = Native.CodexProcesses(); return new Discovery { Count = ids.Count, Handle = Native.FindPet(ids, 0, 1) }; });
            }
            bool nativeVisible = PetHandle != IntPtr.Zero && Native.IsWindow(PetHandle) && Native.IsWindowVisible(PetHandle);
            bool fresh = geometry != null && visible && (now - observedAt).TotalMilliseconds < 1400;
            if (!Dragging && (!nativeVisible || !fresh)) { if (window.IsVisible) window.Hide(); first = true; transition = null; return; }
            double dpr = Json.Number(geometry, "dpr", 1); Rect pet;
            if (Dragging || now < releasedUntil) { var pointer = Dragging ? mouse : releasedPoint; pet = new Rect(pointer.X - offset.X, pointer.Y - offset.Y, Math.Max(1, lastRect.Width), Math.Max(1, lastRect.Height)); }
            else { var origin = new Native.Point(); Native.ClientToScreen(PetHandle, ref origin); pet = new Rect(origin.X + Json.Number(geometry, "x") * dpr, origin.Y + Json.Number(geometry, "y") * dpr, Math.Max(1, Json.Number(geometry, "width") * dpr), Math.Max(1, Json.Number(geometry, "height") * dpr)); }
            var obstacles = new List<Rect> { pet };
            foreach (var r in Json.Items(Json.Get(geometry, "regions"))) obstacles.Add(new Rect(pet.X + (Json.Number(r, "x") - Json.Number(geometry, "x")) * dpr, pet.Y + (Json.Number(r, "y") - Json.Number(geometry, "y")) * dpr, Math.Max(1, Json.Number(r, "width") * dpr), Math.Max(1, Json.Number(r, "height") * dpr)));
            lastRect = pet; var bounds = Native.WorkArea((int)(pet.X + pet.Width / 2), (int)(pet.Y + pet.Height / 2));
            var work = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            double dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
            double maxHeight = Math.Max(140, work.Height / dpi - 24); if (window.MaxHeight != maxHeight) window.MaxHeight = maxHeight;
            var companion=window as CompanionWindow;
            if(companion!=null && (Math.Abs(window.Height-maxHeight)>.5 || companion.SurfaceHeight<1)){window.Height=maxHeight;window.UpdateLayout();}
            int previousSide = placement.Side;
            double requestedHeight=Math.Max(1,companion==null?window.ActualHeight:companion.RequestedSurfaceHeight)*dpi;
            var destination = placement.Place(pet, new Size(window.Width*dpi,requestedHeight), work, obstacles, Dragging);
            // A limit is needed only when space actually runs out, not on every animation frame.
            if(companion!=null){companion.SetSurfaceHeightLimit(destination.Height<requestedHeight-.01?destination.Height/dpi:Double.PositiveInfinity);companion.SetAttachmentSide(placement.Side);}
            if (!first && previousSide != placement.Side && SystemParameters.ClientAreaAnimation) {
                transition = new AttachmentTransition(new Rect(x, y, destination.Width, destination.Height), destination, work, obstacles);
                transitionStarted = now; transitionAnchor = pet.TopLeft; transitionTarget = destination.TopLeft;
            }
            double opacity = 1;
            if (transition != null) {
                double progress = Math.Min(1, (now - transitionStarted).TotalMilliseconds / 340);
                var point = transition.Sample(progress); var translation = pet.TopLeft - transitionAnchor;
                // Keep a direction-change animation attached to the moving pet, including edge clamping.
                point += translation + (destination.TopLeft - (transitionTarget + translation)) * progress;
                x = Math.Max(work.Left + 2, Math.Min(point.X, work.Right - destination.Width - 2));
                y = Math.Max(work.Top + 2, Math.Min(point.Y, work.Bottom - destination.Height - 2));
                opacity = transition.Visibility(progress);
                if (obstacles.Exists(r => new Rect(x,y,destination.Width,destination.Height).IntersectsWith(r))) opacity = 0;
                if (progress >= 1) { transition = null; x = destination.X; y = destination.Y; opacity = 1; }
            } else { x = destination.X; y = destination.Y; }
            first = false;
            if (Math.Abs(window.Opacity - opacity) > .001) window.Opacity = opacity;
            // Placement uses the painted panel, excluding the fully transparent reserved viewport.
            // Anchor the native viewport to the fixed edge, not the difference of two animated heights.
            double nativeTop=y-(companion==null?0:companion.SurfaceOffset*dpi);
            if(companion!=null && transition==null)nativeTop=placement.Side==0?destination.Bottom-window.ActualHeight*dpi:destination.Top;
            int nextX = (int)Math.Round(x), nextY = (int)Math.Round(nativeTop);
            // Preserve popup z-order and skip stationary frames. Raising the main HWND hides its dropdowns.
            if (nextX != placedX || nextY != placedY) { Native.SetWindowPos(handle, IntPtr.Zero, nextX, nextY, 0, 0, 0x0010 | 0x0001 | 0x0004); placedX = nextX; placedY = nextY; }
            if (!window.IsVisible) window.Show();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CodexPetCredits {
    // Geometry travels separately from account data; native pointer motion stays on the UI timer.
    internal sealed class AttachmentFollower {
        private readonly AttachmentPlacement placement = new AttachmentPlacement();
        private object geometry;
        private DateTime observedAt = DateTime.MinValue, lastDiscovery = DateTime.MinValue, lastFrame = DateTime.UtcNow, missingSince = DateTime.MinValue, releasedUntil = DateTime.MinValue;
        private bool visible, seenCodex, first = true, mouseWasDown, pressedOnPet, transparent;
        private Point press, offset;
        private Rect lastRect = Rect.Empty;
        private double x, y;
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
            if (!down && mouseWasDown) { if (Dragging) releasedUntil = now.AddMilliseconds(120); Dragging = false; pressedOnPet = false; }
            mouseWasDown = down;
            if (transparent != Dragging) { long style = Native.GetWindowLongPtr(handle, -20).ToInt64(); Native.SetWindowLongPtr(handle, -20, new IntPtr(Dragging ? style | 0x20 : style & ~0x20)); transparent = Dragging; }
            if ((now - lastDiscovery).TotalMilliseconds >= 250) {
                var ids = Native.CodexProcesses(); lastDiscovery = now;
                if (ids.Count > 0) { seenCodex = true; missingSince = DateTime.MinValue; }
                else { if (missingSince == DateTime.MinValue) missingSince = now; if ((seenCodex || (now - missingSince).TotalSeconds > 30) && (now - missingSince).TotalSeconds > 3) { window.Close(); return; } }
                var candidate = Native.FindPet(ids, Json.Number(geometry, "innerWidth"), Json.Number(geometry, "dpr", 1)); if (candidate != IntPtr.Zero) PetHandle = candidate;
            }
            bool nativeVisible = PetHandle != IntPtr.Zero && Native.IsWindow(PetHandle) && Native.IsWindowVisible(PetHandle);
            bool fresh = geometry != null && visible && (now - observedAt).TotalMilliseconds < 1400;
            if (!Dragging && (!nativeVisible || !fresh)) { if (window.IsVisible) window.Hide(); first = true; return; }
            double dpr = Json.Number(geometry, "dpr", 1); Rect pet;
            if (Dragging || now < releasedUntil) pet = new Rect(mouse.X - offset.X, mouse.Y - offset.Y, Math.Max(1, lastRect.Width), Math.Max(1, lastRect.Height));
            else { var origin = new Native.Point(); Native.ClientToScreen(PetHandle, ref origin); pet = new Rect(origin.X + Json.Number(geometry, "x") * dpr, origin.Y + Json.Number(geometry, "y") * dpr, Math.Max(1, Json.Number(geometry, "width") * dpr), Math.Max(1, Json.Number(geometry, "height") * dpr)); }
            var obstacles = new List<Rect>();
            foreach (var r in Json.Items(Json.Get(geometry, "regions"))) obstacles.Add(new Rect(pet.X + (Json.Number(r, "x") - Json.Number(geometry, "x")) * dpr, pet.Y + (Json.Number(r, "y") - Json.Number(geometry, "y")) * dpr, Math.Max(1, Json.Number(r, "width") * dpr), Math.Max(1, Json.Number(r, "height") * dpr)));
            lastRect = pet; var bounds = Native.WorkArea((int)(pet.X + pet.Width / 2), (int)(pet.Y + pet.Height / 2));
            var work = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            double dpi = VisualTreeHelper.GetDpi(window).DpiScaleX; window.MaxHeight = Math.Max(140, work.Height / dpi - 24);
            var destination = placement.Place(pet, new Size(window.Width * dpi, Math.Max(80, window.ActualHeight) * dpi), work, obstacles, Dragging);
            double dt = Math.Min(.1, Math.Max(.001, (now - lastFrame).TotalSeconds)); lastFrame = now;
            double factor = SystemParameters.ClientAreaAnimation ? 1 - Math.Exp(-dt / .035) : 1;
            // No easing debt while dragging; easing is reserved for the short settling movement.
            if (Dragging || first || Math.Abs(destination.X - x) > 450 || Math.Abs(destination.Y - y) > 450) { x = destination.X; y = destination.Y; first = false; }
            else {
                double nextX = x + (destination.X - x) * factor, nextY = y + (destination.Y - y) * factor;
                var nextBounds = new Rect(nextX, nextY, destination.Width, destination.Height);
                // A direction change must not animate through the pet or its native controls.
                if (nextBounds.IntersectsWith(pet) || obstacles.Exists(r => nextBounds.IntersectsWith(r))) { x = destination.X; y = destination.Y; }
                else { x = nextX; y = nextY; }
            }
            Native.SetWindowPos(handle, new IntPtr(-1), (int)Math.Round(x), (int)Math.Round(y), 0, 0, 0x0010 | 0x0001);
            if (!window.IsVisible) window.Show();
        }
    }
}

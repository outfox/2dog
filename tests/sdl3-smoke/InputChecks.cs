using System.Diagnostics;
using System.Runtime.InteropServices;
using Godot;
using Engine = twodog.Engine;

// Reaches the engine's statically linked SDL through libgodot_sdl3_get_function and feeds it SDL_Events, so the whole path runs:
//   SDL_Event -> DisplayServerSDL3 -> Input -> the window's WindowInput signal and a focused LineEdit.
// No operating-system input is generated: nothing here moves the cursor, takes focus or types into another window,
// and it behaves the same on every OS the port builds for.
internal static class InputChecks
{
    // SDL 3.2 event types.
    private const uint KeyDown = 0x300, KeyUp = 0x301, TextInput = 0x303, MouseMotion = 0x400,
        MouseButtonDown = 0x401, MouseButtonUp = 0x402, MouseWheel = 0x403;

    private const ushort ModLeftShift = 0x0001;

    // SDL scancodes and keycodes. Printable keys carry their character as the keycode (SDLK_1), while keys with no
    // character use SDL_SCANCODE_TO_KEYCODE(x) = x | 1 << 30.
    private const uint ScanA = 4, ScanH = 11, ScanI = 12, Scan1 = 30, ScanLeft = 80, ScanLShift = 225;
    private const uint KeyLeft = (1u << 30) | 80, KeyLShift = (1u << 30) | 225, KeySDLK1 = '1';

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte PushEvent(nint sdlEvent);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AttachVirtualJoystick(nint desc);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte DetachVirtualJoystick(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint OpenJoystick(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CloseJoystick(nint joystick);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte SetVirtualAxis(nint joystick, int axis, short value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte SetVirtualButton(nint joystick, int button, byte down);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetFunction([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    private static GetFunction? _getFunction;
    private static PushEvent? _push;

    private static T Sdl<T>(string name) where T : Delegate
    {
        _getFunction ??= LoadGetFunction();
        var pointer = _getFunction(name);
        if (pointer == 0) throw new InvalidOperationException($"libgodot_sdl3_get_function does not know {name}");
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    public static void Run(Engine engine, Action<bool, string> check)
    {
        _push ??= Sdl<PushEvent>("SDL_PushEvent");
        var root = engine.Tree.Root;
        var events = new List<InputEvent>();
        root.WindowInput += events.Add;

        // Positions reach Godot in pixels; the port multiplies SDL's logical coordinates by the display scale.
        float scale = DisplayServer.ScreenGetScale();
        if (scale <= 0) scale = 1;

        var edit = new LineEdit { Size = new Vector2(300, 40), Position = new Vector2(300, 300) };
        root.AddChild(edit);
        edit.GrabFocus();
        Pump(engine);

        // --- mouse: motion, buttons, wheel
        Push(MouseMotion, e => { F(e, 28, 120); F(e, 32, 90); F(e, 36, 5); F(e, 40, 3); });
        Pump(engine);
        var motion = events.OfType<InputEventMouseMotion>().FirstOrDefault();
        check(motion is not null && Near(motion.Position, new Vector2(120, 90) * scale) && Near(motion.Relative, new Vector2(5, 3) * scale),
            $"motion event: {(motion is null ? "missing" : $"position={motion.Position} relative={motion.Relative}")}, expected (120, 90) / (5, 3) x {scale}");

        events.Clear();
        Push(MouseButtonDown, e => { B(e, 24, 1); B(e, 25, 1); F(e, 28, 120); F(e, 32, 90); });
        Pump(engine);
        check(Input.IsMouseButtonPressed(MouseButton.Left), "left button not pressed");
        var press = events.OfType<InputEventMouseButton>().FirstOrDefault(e => e.Pressed && e.ButtonIndex == MouseButton.Left);
        check(press is not null && Near(press.Position, new Vector2(120, 90) * scale), $"left press: {(press is null ? "missing" : press.Position.ToString())}");
        Push(MouseButtonUp, e => { B(e, 24, 1); B(e, 25, 0); F(e, 28, 120); F(e, 32, 90); });
        Pump(engine);
        check(!Input.IsMouseButtonPressed(MouseButton.Left), "left button still pressed after release");

        events.Clear();
        Push(MouseWheel, e => { F(e, 24, -2); F(e, 28, 1); F(e, 36, 120); F(e, 40, 90); });
        Pump(engine);
        var up = events.OfType<InputEventMouseButton>().Where(e => e.ButtonIndex == MouseButton.WheelUp).ToList();
        var wheelLeft = events.OfType<InputEventMouseButton>().Where(e => e.ButtonIndex == MouseButton.WheelLeft).ToList();
        check(up.Count == 2 && up[0].Pressed && !up[1].Pressed && up[0].Factor == 1, $"wheel up: {string.Join(",", up.Select(e => $"{(e.Pressed ? "down" : "up")}x{e.Factor}"))}");
        check(wheelLeft.Count == 2 && wheelLeft[0].Factor == 2, $"wheel left: {string.Join(",", wheelLeft.Select(e => $"{(e.Pressed ? "down" : "up")}x{e.Factor}"))}");

        // --- keyboard: SDL sends the key, then the text it typed. A typed character must be the key event's unicode.
        events.Clear();
        Tap(ScanH, 'h', 0, "h");
        Tap(ScanI, 'i', 0, "i");
        Pump(engine);
        check(edit.Text == "hi", $"typing 'hi' produced \"{edit.Text}\"");
        var keyH = events.OfType<InputEventKey>().FirstOrDefault(e => e.Pressed && e.Keycode == Key.H);
        check(keyH is { Unicode: 'h', PhysicalKeycode: Key.H }, $"H event: {(keyH is null ? "missing" : $"unicode={keyH.Unicode} physical={keyH.PhysicalKeycode}")}");
        check(events.OfType<InputEventKey>().Count() == 4, $"expected 4 key events (2 presses, 2 releases), got {events.OfType<InputEventKey>().Count()}");

        events.Clear();
        Push(KeyDown, e => { U(e, 24, ScanLShift); U(e, 28, KeyLShift); S(e, 32, ModLeftShift); B(e, 36, 1); });
        Tap(ScanA, 'a', ModLeftShift, "A");
        Pump(engine);
        check(Input.IsKeyPressed(Key.Shift), "shift is not pressed while held");
        check(edit.Text == "hiA", $"shift+A produced \"{edit.Text}\", expected \"hiA\"");
        var keyA = events.OfType<InputEventKey>().FirstOrDefault(e => e.Pressed && e.Keycode == Key.A);
        check(keyA is { ShiftPressed: true, Unicode: 'A' }, $"shift+A event: {(keyA is null ? "missing" : $"shift={keyA.ShiftPressed} unicode={keyA.Unicode}")}");

        Push(KeyUp, e => { U(e, 24, ScanLShift); U(e, 28, KeyLShift); B(e, 36, 0); });
        Pump(engine);
        check(!Input.IsKeyPressed(Key.Shift), "shift still pressed after release");

        // Shift+digit: SDL's keycode is the bare digit even with Shift held, so the event stays bound to the "1"
        // action while the unicode carries '!'.
        events.Clear();
        Push(KeyDown, e => { U(e, 24, ScanLShift); U(e, 28, KeyLShift); S(e, 32, ModLeftShift); B(e, 36, 1); });
        Push(KeyDown, e => { U(e, 24, Scan1); U(e, 28, KeySDLK1); S(e, 32, ModLeftShift); B(e, 36, 1); });
        Push(TextInput, e => P(e, 24, "!"));
        Push(KeyUp, e => { U(e, 24, Scan1); U(e, 28, KeySDLK1); S(e, 32, ModLeftShift); B(e, 36, 0); });
        Push(KeyUp, e => { U(e, 24, ScanLShift); U(e, 28, KeyLShift); B(e, 36, 0); });
        Pump(engine);
        check(edit.Text == "hiA!", $"shift+1 produced \"{edit.Text}\", expected \"hiA!\"");
        var key1 = events.OfType<InputEventKey>().FirstOrDefault(e => e.Pressed && e.Keycode == Key.Key1);
        check(key1 is { PhysicalKeycode: Key.Key1, ShiftPressed: true, Unicode: '!' },
            $"shift+1 event: {(key1 is null ? "missing" : $"keycode={key1.Keycode} physical={key1.PhysicalKeycode} shift={key1.ShiftPressed} unicode={key1.Unicode}")}");
        // Back to "hiA" and caret at the end, which the checks below assume.
        edit.Text = "hiA";
        edit.CaretColumn = 3;

        // Keys that type nothing must not carry a unicode, or LineEdit inserts it.
        events.Clear();
        Push(KeyDown, e => { U(e, 24, ScanLeft); U(e, 28, KeyLeft); B(e, 36, 1); });
        Push(KeyUp, e => { U(e, 24, ScanLeft); U(e, 28, KeyLeft); B(e, 36, 0); });
        Pump(engine);
        var left = events.OfType<InputEventKey>().FirstOrDefault(e => e.Pressed && e.Keycode == Key.Left);
        check(left is { Unicode: 0 }, $"left arrow event: {(left is null ? "missing" : $"unicode={left.Unicode}")}");
        check(edit.Text == "hiA" && edit.CaretColumn == 2, $"left arrow left text \"{edit.Text}\" with the caret at {edit.CaretColumn}, expected \"hiA\" and 2");

        // Text with no key press (IME, composed characters) still reaches the control, as a unicode-only event.
        events.Clear();
        Push(TextInput, e => P(e, 24, "é"));
        Pump(engine);
        check(edit.Text == "hiéA", $"IME text produced \"{edit.Text}\", expected \"hiéA\"");
        check(events.OfType<InputEventKey>().Any(e => e.Pressed && e.Unicode == 'é'), "no unicode-only key event for the IME text");

        // --- gamepad: a virtual Xbox-style pad inside the engine's own SDL, through JoypadSDL into Input
        RunGamepad(engine, check);

        root.WindowInput -= events.Add;
        edit.QueueFree();
        Pump(engine);
    }

    private static void RunGamepad(Engine engine, Action<bool, string> check)
    {
        // SDL drops joystick events while its window is unfocused unless told otherwise.
        Sdl<SetHint>("SDL_SetHint")("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");

        // SDL_VirtualJoystickDesc (SDL 3.2, 64-bit): version = struct size, type 1 = gamepad, 6 axes, 15 buttons.
        nint desc = Marshal.AllocHGlobal(136);
        nint name = Marshal.StringToCoTaskMemUTF8("2dog test pad");
        for (int i = 0; i < 136; i += 8) Marshal.WriteInt64(desc, i, 0);
        Marshal.WriteInt32(desc, 0, 136);
        Marshal.WriteInt16(desc, 4, 1);
        Marshal.WriteInt16(desc, 8, 0x045E);
        Marshal.WriteInt16(desc, 10, 0x028E);
        Marshal.WriteInt16(desc, 12, 6);
        Marshal.WriteInt16(desc, 14, 15);
        Marshal.WriteInt32(desc, 28, (1 << 15) - 1);
        Marshal.WriteInt32(desc, 32, (1 << 6) - 1);
        Marshal.WriteIntPtr(desc, 40, name);

        uint id = Sdl<AttachVirtualJoystick>("SDL_AttachVirtualJoystick")(desc);
        check(id != 0, "SDL_AttachVirtualJoystick failed");
        if (id == 0) return;

        try
        {
            check(WaitFor(engine, () => Input.GetConnectedJoypads().Count == 1), $"the virtual pad never connected ({Input.GetConnectedJoypads().Count} joypads)");
            if (Input.GetConnectedJoypads().Count != 1) return;
            int pad = Input.GetConnectedJoypads()[0];
            Console.WriteLine($"SDL3_INPUT_GAMEPAD joypad {pad} connected as \"{Input.GetJoyName(pad)}\" (guid {Input.GetJoyGuid(pad)})");
            check(Input.GetJoyName(pad).Contains("2dog test pad"), $"joypad name is \"{Input.GetJoyName(pad)}\"");

            nint joystick = Sdl<OpenJoystick>("SDL_OpenJoystick")(id);
            check(joystick != 0, "SDL_OpenJoystick failed");
            var setButton = Sdl<SetVirtualButton>("SDL_SetJoystickVirtualButton");
            var setAxis = Sdl<SetVirtualAxis>("SDL_SetJoystickVirtualAxis");

            setButton(joystick, 0, 1); // SDL_GAMEPAD_BUTTON_SOUTH
            check(WaitFor(engine, () => Input.IsJoyButtonPressed(pad, JoyButton.A)), "south button did not read as JoyButton.A");
            setButton(joystick, 0, 0);
            check(WaitFor(engine, () => !Input.IsJoyButtonPressed(pad, JoyButton.A)), "JoyButton.A stayed pressed after release");

            setAxis(joystick, 0, short.MaxValue); // SDL_GAMEPAD_AXIS_LEFTX
            check(WaitFor(engine, () => Input.GetJoyAxis(pad, JoyAxis.LeftX) > 0.95f), $"left stick X reads {Input.GetJoyAxis(pad, JoyAxis.LeftX)}, expected about 1");
            setAxis(joystick, 0, 0);
            check(WaitFor(engine, () => Math.Abs(Input.GetJoyAxis(pad, JoyAxis.LeftX)) < 0.05f), "left stick X did not return to centre");

            Sdl<CloseJoystick>("SDL_CloseJoystick")(joystick);
        }
        finally
        {
            Sdl<DetachVirtualJoystick>("SDL_DetachVirtualJoystick")(id);
        }

        check(WaitFor(engine, () => Input.GetConnectedJoypads().Count == 0), "the pad stayed connected after it was detached");
        Marshal.FreeCoTaskMem(name);
        Marshal.FreeHGlobal(desc);
    }

    // Joypad hot-plug and state changes land a frame or two after SDL sees them.
    private static bool WaitFor(Engine engine, Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 3000 && engine.Update()) Thread.Sleep(10);
        return condition();
    }

    // A key press as SDL emits it: the press, the text it types, the release.
    private static void Tap(uint scancode, uint keycode, ushort mod, string text)
    {
        Push(KeyDown, e => { U(e, 24, scancode); U(e, 28, keycode); S(e, 32, mod); B(e, 36, 1); });
        Push(TextInput, e => P(e, 24, text));
        Push(KeyUp, e => { U(e, 24, scancode); U(e, 28, keycode); S(e, 32, mod); B(e, 36, 0); });
    }

    private static bool Near(Vector2 a, Vector2 b) => Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1;

    private static void Pump(Engine engine)
    {
        // Two frames: events pushed before the first are all dispatched by it, and the second lets the UI react.
        for (int i = 0; i < 2; i++) engine.Update();
    }

    // SDL_Event is a 128-byte union; every event starts with { Uint32 type, Uint32 reserved, Uint64 timestamp }.
    private static void Push(uint type, Action<nint> fill)
    {
        nint memory = Marshal.AllocHGlobal(128);
        try
        {
            for (int i = 0; i < 128; i += 8) Marshal.WriteInt64(memory, i, 0);
            Marshal.WriteInt32(memory, 0, (int)type);
            fill(memory);
            if (_push!(memory) == 0) throw new InvalidOperationException($"SDL rejected event 0x{type:x}");
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static void F(nint e, int offset, float value) => Marshal.WriteInt32(e, offset, BitConverter.SingleToInt32Bits(value));
    private static void U(nint e, int offset, uint value) => Marshal.WriteInt32(e, offset, (int)value);
    private static void S(nint e, int offset, ushort value) => Marshal.WriteInt16(e, offset, (short)value);
    private static void B(nint e, int offset, byte value) => Marshal.WriteByte(e, offset, value);

    // SDL copies the text of a pushed event, so the string only has to outlive the call.
    private static readonly List<nint> Strings = [];

    private static void P(nint e, int offset, string text)
    {
        nint ptr = Marshal.StringToCoTaskMemUTF8(text);
        Strings.Add(ptr);
        Marshal.WriteIntPtr(e, offset, ptr);
    }

    private static GetFunction LoadGetFunction()
    {
        // The engine already loaded this library; loading the same path again returns the same module.
        var path = Directory.GetFiles(AppContext.BaseDirectory, "libgodot-*")
            .First(file => !Path.GetFileName(file).Contains("dawn", StringComparison.OrdinalIgnoreCase)
                           && Path.GetExtension(file) is ".dll" or ".so" or ".dylib");
        var module = NativeLibrary.Load(path);
        return Marshal.GetDelegateForFunctionPointer<GetFunction>(NativeLibrary.GetExport(module, "libgodot_sdl3_get_function"));
    }
}

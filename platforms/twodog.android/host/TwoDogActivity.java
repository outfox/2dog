package dev.twodog.host;

import java.util.ArrayList;
import java.util.List;

/** Godot owns the surface, input, render thread and Activity lifecycle. */
public final class TwoDogActivity extends org.godotengine.godot.GodotActivity {
    @Override
    public List<String> getCommandLine() {
        List<String> args = new ArrayList<>(super.getCommandLine());
        args.add("--main-pack");
        args.add("res://game.pck");
        args.add("--verbose");
        return args;
    }
}

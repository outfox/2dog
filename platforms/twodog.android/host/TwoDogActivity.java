package dev.twodog.host;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.concurrent.CopyOnWriteArrayList;

/** Godot owns the surface, input, render thread and Activity lifecycle. */
public final class TwoDogActivity extends org.godotengine.godot.GodotActivity {
    private static final List<Runnable> mainLoopStartedListeners = new CopyOnWriteArrayList<>();

    /**
     * Runs the listener on Godot's render thread (the engine's main thread) once the main loop has started and the
     * main scene is in the tree: the first point where a .NET host can use the SceneTree. Listeners must not throw.
     */
    public static void addMainLoopStartedListener(Runnable listener) {
        mainLoopStartedListeners.add(Objects.requireNonNull(listener));
    }

    @Override
    public List<String> getCommandLine() {
        List<String> args = new ArrayList<>(super.getCommandLine());
        args.add("--main-pack");
        args.add("res://game.pck");
        return args;
    }

    @Override
    public void onGodotMainLoopStarted() {
        super.onGodotMainLoopStarted();
        for (Runnable listener : mainLoopStartedListeners) {
            listener.run();
        }
    }
}

package dev.twodog.maui;

import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import androidx.fragment.app.FragmentActivity;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import org.godotengine.godot.Godot;
import org.godotengine.godot.GodotFragment;

/** Godot owns its native GPU surface and render thread; MAUI owns the surrounding UI. */
public final class TwoDogMauiFragment extends GodotFragment {
    private static final String TAG = "2dog.maui";
    private static boolean engineDestroyed;
    private volatile Runnable started;
    private volatile Runnable quit;
    private volatile boolean ready;
    private boolean startNotified;

    private synchronized void setListeners(Runnable started, Runnable quit) {
        this.started = started;
        this.quit = quit;
        startNotified = false;
    }

    private synchronized void notifyStarted() {
        if (ready && !startNotified && started != null) {
            startNotified = true;
            started.run();
        }
    }

    // Public no-argument constructor is required for Android fragment restoration.
    public TwoDogMauiFragment() { }

    public static TwoDogMauiFragment attach(FragmentActivity activity, FrameLayout container,
            String[] arguments, Runnable started, Runnable quit) {
        if (engineDestroyed) throw new IllegalStateException("Restarting Godot in this Android process is unsupported.");
        androidx.fragment.app.FragmentManager manager = activity.getSupportFragmentManager();
        if (manager.isStateSaved()) throw new IllegalStateException("Cannot attach Godot after Activity state was saved.");
        androidx.fragment.app.Fragment existing = manager.findFragmentByTag(TAG);
        TwoDogMauiFragment fragment;
        if (existing == null) {
            fragment = new TwoDogMauiFragment();
            Bundle options = new Bundle();
            options.putStringArray("arguments", arguments);
            fragment.setArguments(options);
            fragment.setListeners(started, quit);
            // No container ID: the fragment can be restored before MAUI reconstructs its page.
            manager.beginTransaction().add(fragment, TAG).commitNow();
        } else {
            fragment = (TwoDogMauiFragment)existing;
            View view = fragment.requireView();
            if (view.getParent() != null && view.getParent() != container)
                throw new IllegalStateException("Only one GodotView may be attached at a time.");
            fragment.setListeners(started, quit);
        }
        container.addView(fragment.requireView(), new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        fragment.notifyStarted();
        return fragment;
    }

    @Override public List<String> getCommandLine() {
        List<String> arguments = new ArrayList<>(super.getCommandLine());
        arguments.add("--main-pack");
        arguments.add("res://game.pck");
        String[] extra = requireArguments().getStringArray("arguments");
        if (extra != null) arguments.addAll(Arrays.asList(extra));
        return arguments;
    }

    @Override public void onGodotMainLoopStarted() {
        super.onGodotMainLoopStarted();
        ready = true;
        notifyStarted();
    }

    public void dispatch(Runnable command) {
        if (!ready) throw new IllegalStateException("Godot has not started.");
        getGodot().runOnRenderThread(command);
    }

    public void detach() {
        // Keep the Activity-owned engine and fragment for page navigation. The next view
        // reuses this surface; destroying the fragment would shut down the process singleton.
        View view = getView();
        if (view != null && view.getParent() instanceof ViewGroup)
            ((ViewGroup)view.getParent()).removeView(view);
        // Synchronize with an in-flight callback before .NET disposes its Runnable peer.
        setListeners(null, null);
    }

    @Override public synchronized void onGodotForceQuit(Godot instance) {
        ready = false;
        Runnable listener = quit;
        if (listener != null) listener.run();
    }

    @Override public void onGodotRestartRequested(Godot instance) { onGodotForceQuit(instance); }

    @Override public void onDestroy() {
        ready = false;
        engineDestroyed = true;
        setListeners(null, null);
        super.onDestroy();
    }
}

# Web startup measurements

Scripts for measuring what a threaded WebGPU bundle does before its first frames. They drive headless Chrome over the
DevTools protocol, so nothing touches the desktop. The `.sh` files expect Git Bash and Chrome in its default location.

Output (Chrome profiles, logs) goes to `$OUT`, or the current directory.

## Per-frame report

```sh
node serve.mjs <AppBundle> 18490 8 &          # instrumented server, reports the first 8 frames
FRAMES=8 bash measure.sh <name> 18490 18491   # fresh profile = cold; KEEP=1 reuses the profile = warm
```

Each frame line shows its wall time and how many shader modules and pipelines were created, synchronously and
asynchronously. `frame 1` starts when the engine script loads, so it includes the CPU boot. The `proxy` lines are the
most expensive WebGPU calls that worker threads proxied to the device thread before the first frame.

Options, as environment variables:

- `ARGS="--autoplay --cam=top"` on `serve.mjs` passes arguments to the engine, for example to reach a later part of a game.
- `LATE=60` on `measure.sh` keeps listening for that many seconds. Frames after the first ones are reported when they
  create shaders or pipelines or take over 250 ms, which shows stalls during play.
- `DRAWS=1` on `serve.mjs` also reports frame rate, render passes and draw calls, averaged over every 300 frames.

Stop a server with `curl http://127.0.0.1:<port>/__quit`. A server left running keeps its port and serves its old bundle.

## Pictures

- `shot.mjs <url> <cdpPort> <out.png> [settleMs]`: one screenshot after the first frame.
- `seq.mjs <url> <cdpPort> <out.jpg> [count] [stepMs]`: a contact sheet over time, to compare two builds.

Both need a Chrome started with `--remote-debugging-port`, as in `measure.sh`.

## CPU profile

`prof.mjs <url> <cdpPort> <out>` profiles the page and every worker until the first frame.

## Replaying pipeline creation

Answers "how long does the browser take for exactly these pipelines" without the engine:

```sh
CAPTURE=$PWD/replay/capture.json node serve.mjs <AppBundle> 18490 8 &
FRAMES=8 bash measure.sh capture 18490 18491  # writes replay/capture.json
node serve.mjs replay 18492 &
bash replay.sh sync 18492 18493               # also: async, prewarm, warmsync; append "&only=Render" or "&only=Compute"
```

`capture.json` holds a game's shaders, so it isn't committed.

## Testing an engine change

The baked shader cache is keyed by the engine's commit hash. A test build must therefore report the hash of the
released natives, or every shader misses the cache.

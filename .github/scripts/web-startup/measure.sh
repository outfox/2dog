# usage: measure.sh <name> <serverPort> <cdpPort>: fresh-profile headless Chrome, per-frame report, then Browser.close.
S="$(cd "$(dirname "$0")" && pwd)"; O="${OUT:-$PWD}"; name=$1; port=$2; cdp=$3
[ -n "$KEEP" ] || rm -rf "$O/profile-$name"
"/c/Program Files/Google/Chrome/Application/chrome.exe" --headless=new --remote-debugging-port=$cdp --user-data-dir="$(cygpath -w "$O/profile-$name")" --disable-extensions --no-first-run --no-default-browser-check --enable-unsafe-webgpu $EXTRA about:blank > "$O/chrome-$name.log" 2>&1 &
for i in $(seq 1 100); do curl -s http://127.0.0.1:$cdp/json/version >/dev/null && break; sleep 0.2; done
echo "== $name"; LOG="$O/log-$name.txt" LABELS=1 timeout 320 node "$S/run.mjs" http://127.0.0.1:$port/ ${FRAMES:-6} $cdp
node --input-type=module -e "const v=await (await fetch('http://127.0.0.1:$cdp/json/version')).json();const ws=new WebSocket(v.webSocketDebuggerUrl);ws.onopen=()=>{ws.send(JSON.stringify({id:1,method:'Browser.close'}));setTimeout(()=>process.exit(0),500)};"

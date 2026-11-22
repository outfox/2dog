# usage: replay.sh <mode> <serverPort> <cdpPort> [name]: replays capture.json on a fresh Chrome profile (KEEP=1 reuses it).
S="$(cd "$(dirname "$0")" && pwd)"; O="${OUT:-$PWD}"; mode=$1; port=$2; cdp=$3; name=${4:-replay-$mode}
[ -n "$KEEP" ] || rm -rf "$O/profile-$name"
"/c/Program Files/Google/Chrome/Application/chrome.exe" --headless=new --remote-debugging-port=$cdp --user-data-dir="$(cygpath -w "$O/profile-$name")" --disable-extensions --no-first-run --no-default-browser-check --enable-unsafe-webgpu $EXTRA about:blank > "$O/chrome-$name.log" 2>&1 &
for i in $(seq 1 100); do curl -s http://127.0.0.1:$cdp/json/version >/dev/null && break; sleep 0.2; done
node "$S/replay-run.mjs" "http://127.0.0.1:$port/?mode=$mode" $cdp
node --input-type=module -e "const v=await (await fetch('http://127.0.0.1:$cdp/json/version')).json();const ws=new WebSocket(v.webSocketDebuggerUrl);ws.onopen=()=>{ws.send(JSON.stringify({id:1,method:'Browser.close'}));setTimeout(()=>process.exit(0),500)};"

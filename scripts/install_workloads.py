"""Install optional workloads from the repository's pinned workload set."""
import json
from pathlib import Path
import subprocess
import sys


def main(workloads):
    if not workloads or any(name.startswith("-") for name in workloads):
        raise ValueError("Pass workload IDs, for example: poe install-workloads wasm-tools")
    version = json.loads((Path(__file__).resolve().parents[1] / "global.json").read_text())["workloadVersion"]
    subprocess.run(["dotnet", "workload", "install", *workloads, "--version", version,
                    "--source", "https://api.nuget.org/v3/index.json"], check=True)


if __name__ == "__main__":
    main(sys.argv[1:])

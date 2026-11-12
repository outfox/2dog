"""Refuse to reuse a published native version made from different package inputs."""
import io
import os
import subprocess
import urllib.error
import urllib.request
import zipfile

from scripts.native_inputs import fingerprint


def verify(data, expected):
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        try:
            actual = archive.read("2dog-native-inputs.txt").decode().strip()
        except KeyError:
            raise ValueError("Published native package has no input fingerprint; release with a new NativesRevision")
    if actual != expected:
        raise ValueError("Published native version contains different inputs; release with a new NativesRevision")


def main():
    version = subprocess.check_output(["dotnet", "msbuild", "twodog.engine/twodog.engine.csproj",
                                       "-getProperty:NativesVersion"], text=True).strip()
    if version.endswith(".0"):
        version = version[:-2]
    suffix = ".double" if os.environ.get("TwoDogPrecision") == "double" else ""
    name = f"2dog.win-x64.debug{suffix}"
    for feed in ("https://api.nuget.org/v3-flatcontainer", "https://apiint.nugettest.org/v3-flatcontainer"):
        try:
            with urllib.request.urlopen(f"{feed}/{name}/{version}/{name}.{version}.nupkg", timeout=60) as response:
                verify(response.read(), fingerprint(kind="package"))
        except urllib.error.HTTPError as error:
            if error.code != 404:
                raise
            print(f"{name} {version} is not published on {feed}")


if __name__ == "__main__":
    main()

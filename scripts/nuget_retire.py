#!/usr/bin/env python3
"""Bulk-retire old 2dog package versions on nuget.org.

Retiring = unlist (hide from search/UI; pinned restores keep working) and/or
deprecate (clients warn via `dotnet list package --deprecated` and the VS
package manager). Neither breaks existing consumers.

Three actions share one plan (the newest --keep versions of every package
stay active, everything older is retired; dead packages retire entirely and
deprecate with an alternate). Versions that are already unlisted or already
deprecated (per the nuget.org registration index) are skipped, so reruns
only touch what still needs retiring:

  plan       print what would be retired (default; no credentials needed)
  unlist     DELETE /api/v2/package/{id}/{version} for every retired version.
             Official, API-key auth. nuget.org rate-limits unlisting to
             250/hour per key - the script sleeps and retries on 429, so a
             large run may take over an hour.
  deprecate  POST /json/deprecation/deprecate per package (all retired
             versions in one call). UNOFFICIAL: this is the endpoint the
             nuget.org website itself uses (no public API exists, see
             NuGet/NuGetGallery#8873); it needs a logged-in browser
             session's Cookie header and may break without notice. The
             officially supported alternative is the web UI, which has a
             "Select all versions" option (one pass per package).

Usage:
  uv run poe nuget-retire                       # plan
  uv run poe nuget-retire unlist --api-key KEY  # or NUGET_UNLIST_KEY env var
  uv run poe nuget-retire --packages "2dog.gdextension.*"     # wildcards ok
  uv run poe nuget-retire deprecate --packages 2dog.osx-x64   # smoke test
  uv run poe nuget-retire deprecate             # NUGET_COOKIE env var

The unlist key must be a classic nuget.org API key with the "Unlist package"
scope on 2dog* - Trusted Publishing OIDC keys are push-only. The deprecation
cookie is the full "Cookie:" request-header value copied from your browser's
devtools on a logged-in nuget.org page.
"""

from __future__ import annotations

import argparse
import fnmatch
import gzip
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

GALLERY = "https://www.nuget.org"

# --packages entries may be fnmatch wildcards (* ? [..]). They expand against
# nuget.org's autocomplete index, which only knows packages with at least one
# *listed* version, so DEAD_PACKAGES ids are always merged into the candidates.
# The default covers every 2dog package, including the 2dog.gdextension.* and
# 2dog.bindings ids published from the gdextension branch.
DEFAULT_PACKAGES = ["2dog", "2dog.*"]

# Dead package ids -> the package that replaced them: ALL versions retire,
# and deprecation points consumers at the replacement.
# 2dog.cli and 2dog.Templates merged into the 2dog tool+template package;
# note that pre-merge versions of the `2dog` id itself were the library
# (now 2dog.engine) — retiring those needs a one-off manual deprecation
# with 2dog.engine as the alternate, not this script's generic message.
DEAD_PACKAGES = {
    "2dog.osx-x64": "2dog.osx-arm64",
    "2dog.cli": "2dog",
    "2dog.Templates": "2dog",
}

# The gallery website (not api.nuget.org) sits behind a CDN that may reject
# the default urllib user agent.
USER_AGENT = "2dog-nuget-retire/1.0 (+https://github.com/outfox/2dog)"


def http(method: str, url: str, headers: dict | None = None, body: bytes | None = None):
    """Returns (status, headers, text) without raising on HTTP errors."""
    request = urllib.request.Request(
        url, method=method, data=body,
        headers={"User-Agent": USER_AGENT, **(headers or {})})

    def decode(response) -> str:
        raw = response.read()
        # The registration blobs are stored gzipped and served with
        # Content-Encoding: gzip whether or not the client asked for it.
        if response.headers.get("Content-Encoding") == "gzip":
            raw = gzip.decompress(raw)
        return raw.decode("utf-8", "replace")

    try:
        with urllib.request.urlopen(request) as response:
            return response.status, dict(response.headers), decode(response)
    except urllib.error.HTTPError as error:
        return error.code, dict(error.headers), decode(error)


def version_sort_key(version: str):
    """NuGet-ish ordering: numeric part, then stable > prerelease."""
    numeric, _, prerelease = version.partition("-")
    parts = [int(p) for p in numeric.split(".")]
    parts += [0] * (4 - len(parts))
    return tuple(parts), prerelease == "", prerelease


def expand_packages(patterns: list[str]) -> list[str]:
    """Expand wildcard patterns to package ids, preserving order and deduplicating."""
    ids: dict[str, None] = {}
    for pattern in patterns:
        if not any(ch in pattern for ch in "*?["):
            ids.setdefault(pattern)
            continue
        prefix = re.split(r"[*?\[]", pattern, maxsplit=1)[0].rstrip(".-")
        status, _, body = http(
            "GET", "https://azuresearch-usnc.nuget.org/autocomplete?"
            + urllib.parse.urlencode({"q": prefix, "take": 1000,
                                      "prerelease": "true", "semVerLevel": "2.0.0"}))
        if status != 200:
            raise RuntimeError(f"autocomplete for {pattern!r} returned HTTP {status}")
        candidates = set(json.loads(body)["data"]) | set(DEAD_PACKAGES)
        matches = [c for c in sorted(candidates)
                   if fnmatch.fnmatchcase(c.lower(), pattern.lower())]
        if not matches:
            raise RuntimeError(f"pattern {pattern!r} matched no packages on nuget.org")
        for match in matches:
            ids.setdefault(match)
    return list(ids)


def published_versions(package_id: str) -> list[str]:
    """Every published version (listed or not), oldest first."""
    status, _, body = http(
        "GET", f"https://api.nuget.org/v3-flatcontainer/{package_id.lower()}/index.json")
    if status != 200:
        raise RuntimeError(f"{package_id}: flat-container index returned HTTP {status}")
    return sorted(json.loads(body)["versions"], key=version_sort_key)


def version_status(package_id: str) -> dict[str, dict]:
    """Normalized version -> {"listed": bool, "deprecated": bool} from the registration index."""
    status, _, body = http(
        "GET", f"https://api.nuget.org/v3/registration5-gz-semver2/{package_id.lower()}/index.json")
    if status != 200:
        raise RuntimeError(f"{package_id}: registration index returned HTTP {status}")
    result: dict[str, dict] = {}
    for page in json.loads(body)["items"]:
        items = page.get("items")
        if items is None:  # large packages get paged; leaves live in a separate blob
            status, _, page_body = http("GET", page["@id"])
            if status != 200:
                raise RuntimeError(f"{package_id}: registration page returned HTTP {status}")
            items = json.loads(page_body)["items"]
        for leaf in items:
            entry = leaf["catalogEntry"]
            version = entry["version"].partition("+")[0].lower()
            result[version] = {"listed": entry.get("listed", True),
                               "deprecated": "deprecation" in entry}
    return result


def build_plan(package_ids: list[str], keep: int) -> list[dict]:
    plan = []
    for package_id in package_ids:
        versions = published_versions(package_id)
        state = version_status(package_id)
        keep_count = 0 if package_id in DEAD_PACKAGES else min(keep, len(versions))
        retire = versions[:len(versions) - keep_count]
        # Versions already unlisted/deprecated need no further action.
        plan.append({
            "id": package_id,
            "alternate": DEAD_PACKAGES.get(package_id),
            "keep": versions[len(versions) - keep_count:],
            "retire": retire,
            "unlist": [v for v in retire if state.get(v, {}).get("listed", True)],
            "deprecate": [v for v in retire if not state.get(v, {}).get("deprecated", False)],
        })
    return plan


def show_plan(plan: list[dict], verbose: bool) -> None:
    for entry in plan:
        note = f" [dead -> {entry['alternate']}]" if entry["alternate"] else ""
        already = len(entry["retire"]) - len(entry["unlist"]), len(entry["retire"]) - len(entry["deprecate"])
        print(f"{entry['id']}{note}: retire {len(entry['retire'])} "
              f"(unlist {len(entry['unlist'])}, deprecate {len(entry['deprecate'])}; "
              f"{already[0]} already unlisted, {already[1]} already deprecated), "
              f"keep {', '.join(entry['keep'])}")
        if verbose:
            for action in ("unlist", "deprecate"):
                if entry[action]:
                    print(f"    {action}: " + " ".join(entry[action]))
    total_unlist = sum(len(e["unlist"]) for e in plan)
    total_deprecate = sum(len(e["deprecate"]) for e in plan)
    print(f"\nTotal versions to unlist: {total_unlist}, to deprecate: {total_deprecate} "
          "(unlist rate limit is 250/hour - the script waits on 429)")


def unlist(plan: list[dict], api_key: str) -> int:
    done, failed = 0, []
    for entry in plan:
        for version in entry["unlist"]:
            for attempt in range(12):
                status, headers, _ = http(
                    "DELETE", f"{GALLERY}/api/v2/package/{entry['id']}/{version}",
                    headers={"X-NuGet-ApiKey": api_key})
                if status < 300:
                    done += 1
                    print(f"unlisted {entry['id']} {version}")
                    break
                if status == 429:
                    wait = int(headers.get("Retry-After") or 300)
                    print(f"rate-limited; sleeping {wait}s ({done} done so far)")
                    time.sleep(wait)
                    continue
                failed.append(f"{entry['id']} {version} -> HTTP {status}")
                print(f"FAILED: {failed[-1]}", file=sys.stderr)
                break
            time.sleep(0.5)
    print(f"\nUnlisted {done} version(s).")
    if failed:
        print("Failed:\n" + "\n".join(failed), file=sys.stderr)
    return 1 if failed else 0


def deprecate(plan: list[dict], cookie: str, message: str) -> int:
    print("WARNING: using the nuget.org website's internal endpoint (no official "
          "API exists, see NuGet/NuGetGallery#8873). It may break without notice.",
          file=sys.stderr)
    failures = 0
    for entry in plan:
        if not entry["deprecate"]:
            continue

        # The antiforgery form token comes from any Manage page of the package.
        any_version = (entry["keep"] or entry["retire"])[-1]
        manage_url = f"{GALLERY}/packages/{entry['id']}/{any_version}/Manage"
        status, _, page = http("GET", manage_url, headers={"Cookie": cookie})
        if status != 200:
            print(f"{entry['id']}: Manage page returned HTTP {status} - cookie "
                  "expired or not an owner? Skipping.", file=sys.stderr)
            failures += 1
            continue
        token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page)
        if not token:
            print(f"{entry['id']}: no antiforgery token on {manage_url} - skipping.",
                  file=sys.stderr)
            failures += 1
            continue

        fields = [
            ("__RequestVerificationToken", token.group(1)),
            ("id", entry["id"]),
            *(("versions", v) for v in entry["deprecate"]),
            ("isLegacy", "true"),
            ("hasCriticalBugs", "false"),
            ("isOther", "false"),
            ("customMessage", message),
        ]
        if entry["alternate"]:
            fields.append(("alternatePackageId", entry["alternate"]))

        status, _, body = http(
            "POST", f"{GALLERY}/json/deprecation/deprecate",
            headers={"Cookie": cookie, "Referer": manage_url,
                     "Content-Type": "application/x-www-form-urlencoded"},
            body=urllib.parse.urlencode(fields).encode())
        if status == 200:
            print(f"deprecated {entry['id']}: {len(entry['deprecate'])} version(s)")
        else:
            print(f"{entry['id']}: deprecation POST returned HTTP {status}: "
                  f"{body[:200]}", file=sys.stderr)
            failures += 1
        time.sleep(2)
    return 1 if failures else 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("action", nargs="?", default="plan",
                        choices=["plan", "unlist", "deprecate"])
    parser.add_argument("--api-key", default=os.environ.get("NUGET_UNLIST_KEY"),
                        help="unlist: classic API key with the Unlist scope "
                             "(default: NUGET_UNLIST_KEY env var)")
    parser.add_argument("--cookie", default=os.environ.get("NUGET_COOKIE"),
                        help="deprecate: logged-in nuget.org Cookie header "
                             "(default: NUGET_COOKIE env var)")
    parser.add_argument("--keep", type=int, default=2,
                        help="newest N versions of each package to keep active (default 2)")
    parser.add_argument("--packages", nargs="+", default=DEFAULT_PACKAGES, metavar="ID",
                        help="package ids or fnmatch wildcards to process "
                             "(default: 2dog and 2dog.*)")
    parser.add_argument("--message",
                        default="Superseded 2dog iteration - use the latest version. https://2dog.dev",
                        help="deprecation custom message (shown on nuget.org)")
    parser.add_argument("--verbose", action="store_true",
                        help="list every version to retire in the plan")
    args = parser.parse_args()

    plan = build_plan(expand_packages(args.packages), args.keep)
    show_plan(plan, args.verbose)

    if args.action == "unlist":
        if not args.api_key:
            parser.error("unlist needs --api-key or NUGET_UNLIST_KEY "
                         "(classic key with the Unlist scope; Trusted Publishing keys are push-only)")
        return unlist(plan, args.api_key)
    if args.action == "deprecate":
        if not args.cookie:
            parser.error("deprecate needs --cookie or NUGET_COOKIE "
                         "(full Cookie header from a logged-in nuget.org browser session)")
        return deprecate(plan, args.cookie, args.message)
    return 0


if __name__ == "__main__":
    sys.exit(main())

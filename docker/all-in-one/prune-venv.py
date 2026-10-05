"""Uninstall every package in this interpreter's environment that the given requirements do not need.

The saic-python-mqtt-gateway image runs a plain `poetry install`, so its runtime venv also holds
the dev group (pytest, mypy, pylint, pre-commit and everything they pull in). None of it runs,
but image scans flag it all the same. Run with the venv's own interpreter, so environment
markers resolve for the Python and platform the gateway runs on. pip itself is left alone; the
Dockerfile removes it as the last step.

Usage: <venv>/bin/python prune-venv.py REQUIREMENT [REQUIREMENT ...]
"""

from __future__ import annotations

import subprocess
import sys
from importlib.metadata import Distribution, distributions

# Private to pip, but pip is the one installer in the image and this script runs before it goes.
from pip._vendor.packaging.requirements import Requirement
from pip._vendor.packaging.utils import canonicalize_name

ALWAYS_KEEP = frozenset({"pip"})


def installed_distributions() -> dict[str, Distribution]:
    return {
        canonicalize_name(dist.metadata["Name"]): dist
        for dist in distributions()
        if dist.metadata["Name"]
    }


def needed_names(roots: list[str], installed: dict[str, Distribution]) -> set[str]:
    """Walk Requires-Dist from the roots, following an extra's dependencies only where requested."""
    missing = [root for root in roots if canonicalize_name(Requirement(root).name) not in installed]
    if missing:
        # A root the venv lacks means the gateway's dependency list changed, so the roots need updating.
        sys.exit(f"Not installed, check the gateway's dependencies: {', '.join(missing)}")

    visited: set[tuple[str, str]] = set()
    pending = [Requirement(root) for root in roots]
    while pending:
        requirement = pending.pop()
        name = canonicalize_name(requirement.name)
        dist = installed.get(name)
        if dist is None:
            continue
        for extra in {"", *requirement.extras}:
            if (name, extra) in visited:
                continue
            visited.add((name, extra))
            for line in dist.requires or []:
                dependency = Requirement(line)
                if dependency.marker is None or dependency.marker.evaluate({"extra": extra}):
                    pending.append(dependency)
    return {name for name, _ in visited}


def main(roots: list[str]) -> None:
    if not roots:
        sys.exit(__doc__)
    installed = installed_distributions()
    surplus = sorted(installed.keys() - needed_names(roots, installed) - ALWAYS_KEEP)
    if not surplus:
        print("Nothing to prune")
        return
    print(f"Pruning {len(surplus)} packages: {' '.join(surplus)}", flush=True)
    subprocess.run(
        [sys.executable, "-m", "pip", "uninstall", "--yes", "--quiet", *surplus], check=True
    )


if __name__ == "__main__":
    main(sys.argv[1:])

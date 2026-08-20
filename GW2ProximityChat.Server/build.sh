#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/build"

# ── dependency check ─────────────────────────────────────────────────────────

PACKAGES=(build-essential cmake ninja-build qt6-base-dev libqt6websockets6-dev)
MISSING=()

for pkg in "${PACKAGES[@]}"; do
    dpkg -s "$pkg" &>/dev/null || MISSING+=("$pkg")
done

if (( ${#MISSING[@]} > 0 )); then
    if [ -f /etc/debian_version ] && command -v apt &>/dev/null; then
        echo "Installing missing packages: ${MISSING[*]}"
        sudo apt install -y "${MISSING[@]}"
    else
        echo "Missing dependencies:"
        echo ""
        for pkg in "${MISSING[@]}"; do
            echo "  - $pkg"
        done
        echo ""
        echo "This script can only install packages automatically on Debian."
        echo "Please install the equivalents for your distribution and run again."
        exit 1
    fi
fi

# ── configure ────────────────────────────────────────────────────────────────

cmake \
    -S "$SCRIPT_DIR" \
    -B "$BUILD_DIR" \
    -G Ninja \
    -DCMAKE_BUILD_TYPE=Release

# ── build ─────────────────────────────────────────────────────────────────────

cmake --build "$BUILD_DIR"

# ── done ──────────────────────────────────────────────────────────────────────

echo ""
echo "Built: $BUILD_DIR/GW2ProximityChatServer"

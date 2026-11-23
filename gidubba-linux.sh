#!/usr/bin/env bash
# Gidubba for Linux, from the top of the repository (no need to cd into Gidubba.Linux).
#
#   ./gidubba-linux.sh              build if needed, then run Gidubba
#   ./gidubba-linux.sh build        build only (Release)
#   ./gidubba-linux.sh publish      self-contained build into Gidubba.Linux/packaging/publish
#   ./gidubba-linux.sh install      publish, then install under ~/.local with an app-menu entry
#   ./gidubba-linux.sh uninstall    remove that install (your tables in ~/.local/share/TableForge stay)
#
# Works from any current directory, and through a symlink (e.g. ~/.local/bin/gidubba-dev -> this file).
set -euo pipefail

root="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"
project="$root/Gidubba.Linux/Gidubba.Linux.csproj"
packaging="$root/Gidubba.Linux/packaging"

command -v dotnet >/dev/null || { echo "The .NET 10 SDK is needed (package dotnet-sdk). See Gidubba.Linux/README-LINUX.md." >&2; exit 1; }

case "${1:-run}" in
  run)       [ $# -gt 0 ] && shift
             exec dotnet run --project "$project" -c Release -- "$@" ;;
  build)     exec dotnet build "$project" -c Release ;;
  publish)   exec "$packaging/build.sh" ;;
  install)   "$packaging/build.sh" && exec "$packaging/install.sh" ;;
  uninstall) exec "$packaging/uninstall.sh" ;;
  -h|--help|help) sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//' ;;
  *)         echo "Unknown command: $1 (try --help)" >&2; exit 2 ;;
esac

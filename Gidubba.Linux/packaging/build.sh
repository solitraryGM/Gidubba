#!/usr/bin/env bash
# Build a self-contained linux-x64 (or $RID) publish into ./publish
set -euo pipefail
cd "$(dirname "$0")/.."
RID="${RID:-linux-x64}"
dotnet publish Gidubba.Linux.csproj -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -o packaging/publish
echo "Published to packaging/publish (run ./packaging/publish/gidubba)"

#!/bin/bash
# One-time setup of the sim harness toolchain (Linux): Mono, plus Roslyn csc and NUnit / NUnitLite from
# NuGet (dotnet itself may be blocked by a network policy; api.nuget.org and apt usually aren't).
# Downloads go to Tools/sim-harness/.cache (gitignored).
set -e
H="$(cd "$(dirname "$0")" && pwd)"
C="$H/.cache"
mkdir -p "$C"
if ! command -v mono >/dev/null || [ ! -d /usr/lib/mono/4.5 ]; then
  apt-get install -y -q mono-devel >/dev/null 2>&1 || (apt-get update -q >/dev/null && apt-get install -y -q mono-devel >/dev/null)
fi
fetch() {  # package version subdir
  [ -d "$C/$1" ] && return
  curl -sSL -o "$C/$1.zip" "https://api.nuget.org/v3-flatcontainer/$1/$2/$1.$2.nupkg"
  unzip -q -o "$C/$1.zip" "$3/*" -d "$C/$1"
  rm "$C/$1.zip"
}
fetch microsoft.net.compilers.toolset 4.8.0 tasks/net472
fetch nunit 3.13.3 lib/net45
fetch nunitlite 3.13.3 lib/net45
cp "$C/nunit/lib/net45/nunit.framework.dll" "$C/nunitlite/lib/net45/nunitlite.dll" "$C/"
echo "sim harness ready"

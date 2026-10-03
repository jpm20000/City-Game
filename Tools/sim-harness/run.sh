#!/bin/bash
# Builds the pure Grid + Simulation code and the Simulation EditMode tests into one Mono exe (so the
# tests see the asmdef's internals) and runs them with NUnitLite. Extra arguments go to NUnitLite,
# e.g. --test=CivicTests or --where "test =~ Water". EXTRA="a.cs b.cs" adds files (throwaway probes).
set -e
H="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$H/../.." && pwd)"
C="$H/.cache"
[ -f "$C/nunitlite.dll" ] || "$H/setup.sh"
M=/usr/lib/mono/4.5
cd "$ROOT"
SRC=$(find Assets/_Game/Scripts/Grid Assets/_Game/Scripts/Simulation -name "*.cs" ! -name GridSystem.cs)
TESTS=$(find Assets/_Game/Tests/EditMode/Simulation -name "*.cs")
OUT=$(mono "$C/microsoft.net.compilers.toolset/tasks/net472/csc.exe" -nologo -noconfig -nostdlib -langversion:9.0 \
  -r:$M/mscorlib.dll -r:$M/System.dll -r:$M/System.Core.dll -r:$M/Facades/netstandard.dll -r:$M/Facades/System.Runtime.dll \
  -r:"$C/nunit.framework.dll" -r:"$C/nunitlite.dll" -nowarn:414,169,649,67,219,168,8632 -target:exe -out:"$C/Tests.exe" \
  "$H"/*.cs $EXTRA $SRC $TESTS 2>&1 | grep -v "warning" || true)
if [ -n "$OUT" ]; then echo "$OUT" | sed "s|$ROOT/||" | sort -u; exit 1; fi
cd "$C"
CITY_GAME_ROOT="$ROOT" mono --debug Tests.exe --noresult "$@"

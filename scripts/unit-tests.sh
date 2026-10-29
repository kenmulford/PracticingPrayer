#!/usr/bin/env bash
# Runs the .NET suite, then the PrayerKit Swift suite. Both always run; exit 1 if either fails.
set -uo pipefail

SELF="${BASH_SOURCE[0]}"
case "$SELF" in
  */*) DIR="${SELF%/*}" ;;
  *) DIR="." ;;
esac
ROOT="$(cd "$DIR/.." && pwd)"

dotnet_status=0
swift_status=0

if command -v dotnet >/dev/null 2>&1; then
  dotnet test "$ROOT/PrayerApp.Tests/PrayerApp.Tests.csproj" || dotnet_status=1
else
  echo "unit-tests.sh: dotnet not found on PATH" >&2
  dotnet_status=1
fi

if command -v swift >/dev/null 2>&1; then
  swift test --package-path "$ROOT/ios/PrayerKit" || swift_status=1
else
  echo "unit-tests.sh: swift not found on PATH" >&2
  swift_status=1
fi

if [ "$dotnet_status" -ne 0 ] || [ "$swift_status" -ne 0 ]; then
  exit 1
fi
exit 0

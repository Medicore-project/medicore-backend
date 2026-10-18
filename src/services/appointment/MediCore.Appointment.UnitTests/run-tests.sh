#!/usr/bin/env bash
# SE3112 Unit Testing & Mocking — demo runner for the Appointment service (Linux/macOS/CI).
#
#   ./run-tests.sh                  everything + coverage report
#   ./run-tests.sh Parameterized    only the [Theory] tests
#   ./run-tests.sh Mocking          only the Moq behaviour-verification tests
#   ./run-tests.sh Booking          one feature: Booking | Lifecycle | Waitlist | Validation | DomainRules
set -uo pipefail
cd "$(dirname "$0")"

FILTER="${1:-All}"
case "$FILTER" in
  All)                    TEST_FILTER="Category=Unit" ;;
  Parameterized|Mocking)  TEST_FILTER="Technique=$FILTER" ;;
  *)                      TEST_FILTER="Feature=$FILTER" ;;
esac

RESULTS="$(pwd)/TestResults"
rm -rf "$RESULTS"

printf '\n== MediCore Appointment - Unit Testing & Mocking (filter: %s) ==\n\n' "$TEST_FILTER"

dotnet test UnitTests.csproj \
  --configuration Release \
  --filter "$TEST_FILTER" \
  --settings coverlet.runsettings \
  --collect "XPlat Code Coverage" \
  --results-directory "$RESULTS" \
  --logger "console;verbosity=normal" \
  --logger "trx;LogFileName=unit-tests.trx"
TEST_EXIT=$?

if ! find "$RESULTS" -name coverage.cobertura.xml 2>/dev/null | grep -q .; then
  printf '\nNo coverage was collected (the build or test run failed) - see the errors above.\n'
  exit $TEST_EXIT
fi

dotnet tool restore > /dev/null
dotnet tool run reportgenerator \
  "-reports:$RESULTS/**/coverage.cobertura.xml" \
  "-targetdir:$RESULTS/coverage-report" \
  "-reporttypes:Html;TextSummary;MarkdownSummaryGithub" \
  "-title:MediCore Appointment - Unit Tests" > /dev/null

printf '\n== Coverage summary (Application layer) ==\n'
head -n 16 "$RESULTS/coverage-report/Summary.txt"
printf '\nHTML report: %s/coverage-report/index.html\n' "$RESULTS"

exit $TEST_EXIT

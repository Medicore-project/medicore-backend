#!/usr/bin/env bash
# =============================================================================
# MediCore — Automated Load Testing Pipeline & Demo Runner (k6)
# =============================================================================
# Usage:
#   ./tests/load/run-demo.sh [public | auth | stress | all]
#
# Default: runs the public reads scenario with live console output and opens HTML report.
# =============================================================================

set -e

# Ensure output directory exists
mkdir -p tests/load/reports

TARGET=${1:-"public"}
BASE_URL=${BASE_URL:-"http://localhost:5000"}

echo "=========================================================="
echo "  MediCore Microservices — Automated Performance Testing  "
echo "  Target: $BASE_URL                                       "
echo "=========================================================="

case "$TARGET" in
  public)
    echo "Running Scenario 1: Public Read Scalability..."
    k6 run tests/load/scenarios/01_public_reads.js
    REPORT_PATH="tests/load/reports/public_reads_report.html"
    ;;
  auth)
    echo "Running Scenario 2: Authenticated Staff Workflow..."
    k6 run tests/load/scenarios/02_auth_workflow.js
    REPORT_PATH="tests/load/reports/auth_workflow_report.html"
    ;;
  stress)
    echo "Running Scenario 3: Stress & Rate-Limit Resilience..."
    k6 run tests/load/scenarios/03_stress_spike.js
    REPORT_PATH="tests/load/reports/stress_spike_report.html"
    ;;
  all)
    echo "Running Full Pipeline (All Scenarios)..."
    k6 run tests/load/scenarios/01_public_reads.js
    k6 run tests/load/scenarios/02_auth_workflow.js
    k6 run tests/load/scenarios/03_stress_spike.js
    REPORT_PATH="tests/load/reports/public_reads_report.html"
    ;;
  *)
    echo "Unknown option '$TARGET'. Use: public | auth | stress | all"
    exit 1
    ;;
esac

echo ""
echo "=========================================================="
echo " Execution finished successfully!"
if [ -f "$REPORT_PATH" ]; then
  echo " HTML Report generated: $REPORT_PATH"
  # Open in browser if running on macOS and interactive
  if [[ "$OSTYPE" == "darwin"* ]] && [ -t 1 ]; then
    open "$REPORT_PATH" || true
  fi
fi
echo "=========================================================="

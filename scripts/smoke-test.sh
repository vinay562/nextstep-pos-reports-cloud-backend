#!/usr/bin/env bash
# Quick manual check of a deployed stack: submits the specification example (dated today, UTC)
# and polls the status endpoint until the calculation result appears.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
outputs_file="$repo_root/cdk.outputs/outputs.json"

if [[ ! -f "$outputs_file" ]]; then
  echo "No $outputs_file found. Deploy first with: npm run deploy" >&2
  exit 1
fi

api_url="$(node -p "require('$outputs_file').NextStepPosReports.ApiUrl")"
report="POS/UL204.FR RGN/TO BKK/$(date -u +%d)1205/N1642.3E09612.5/450/12500/2800"

echo "POST $api_url/pos-reports"
echo "  $report"
submit_response="$(curl --silent --show-error --fail-with-body -H 'Content-Type: text/plain' --data "$report" "$api_url/pos-reports")"
echo "  → $submit_response"

flight_id="$(node -p "JSON.parse(process.argv[1]).flightId" "$submit_response")"

for attempt in {1..15}; do
  sleep 2
  status_response="$(curl --silent --show-error "$api_url/status/$flight_id")"
  echo "GET /status/$flight_id (attempt $attempt) → $status_response"
  if [[ "$status_response" == *remainingFlightTimeMinutes* ]]; then
    exit 0
  fi
done

echo "No calculation result after 30 seconds." >&2
exit 1

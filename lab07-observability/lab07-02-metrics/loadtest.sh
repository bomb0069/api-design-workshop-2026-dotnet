#!/bin/sh
# =============================================================================
# Load Test Script — RED Method Traffic Generator
# =============================================================================
#
# Generates a mixed workload so all three RED signals move on the dashboard:
#   Rate     — steady stream of GETs and POSTs across three route templates
#   Errors   — POST /api/orders fails ~10% of the time (simulated 500s),
#              plus some GETs hit ids that don't exist (404s) and a few
#              POSTs send an invalid body (400s)
#   Duration — GET /api/products/{id} adds 10–300ms of random latency
#
# This script runs inside a Docker container (alpine/curl). No local tools
# needed — just: docker compose --profile loadtest up loadtest
#
# Configuration via environment variables:
#   API_HOST          - API hostname:port (default: api:8080)
#   DURATION_SECONDS  - How long to run (default: 180)
#   REQUESTS_PER_SEC  - Target requests per second (default: 5)
# =============================================================================

set -e

API_HOST="${API_HOST:-api:8080}"
DURATION_SECONDS="${DURATION_SECONDS:-180}"
REQUESTS_PER_SEC="${REQUESTS_PER_SEC:-5}"

# Using awk because sh doesn't support floating point arithmetic
SLEEP_INTERVAL=$(awk "BEGIN {printf \"%.2f\", 1 / $REQUESTS_PER_SEC}")

list_count=0
byid_count=0
order_count=0
error_count=0
start_time=$(date +%s)

echo "============================================="
echo "  RED Method Traffic Generator"
echo "============================================="
echo "  Target:    http://${API_HOST}"
echo "  Duration:  ${DURATION_SECONDS}s"
echo "  Rate:      ~${REQUESTS_PER_SEC} req/s"
echo "============================================="
echo ""
echo "Waiting for API to be ready..."

retries=0
max_retries=30
until curl -sf "http://${API_HOST}/health" > /dev/null 2>&1; do
    retries=$((retries + 1))
    if [ "$retries" -ge "$max_retries" ]; then
        echo "ERROR: API not ready after ${max_retries} retries. Exiting."
        exit 1
    fi
    echo "  Attempt ${retries}/${max_retries} — waiting 2s..."
    sleep 2
done
echo "API is ready!"
echo ""

# ---------------------------------------------------------------------------
# Helper: pick a random number between 1 and 100
# Uses /dev/urandom since $RANDOM is not available in plain sh.
# Read 2 bytes (0-65535) and reduce modulo 100 — reading 1 byte (0-255)
# would skew every weight.
# ---------------------------------------------------------------------------
random_1_to_100() {
    echo $(( $(od -An -tu2 -N2 /dev/urandom | tr -d ' ') % 100 + 1 ))
}

# ---------------------------------------------------------------------------
# Request mix (per roll of 1-100):
#    1-40  GET  /api/products          (fast 200s — baseline rate)
#   41-75  GET  /api/products/{1..7}   (random latency; ids 6-7 return 404)
#   76-100 POST /api/orders            (~10% 500s server-side; every 10th
#                                       POST sends an invalid body -> 400)
# ---------------------------------------------------------------------------
send_request() {
    roll=$(random_1_to_100)

    if [ "$roll" -le 40 ]; then
        curl -s -o /dev/null "http://${API_HOST}/api/products"
        list_count=$((list_count + 1))
    elif [ "$roll" -le 75 ]; then
        # ids 1-7: 1-5 exist, 6-7 produce 404s (client errors, not 5xx)
        id=$(( $(od -An -tu1 -N1 /dev/urandom | tr -d ' ') % 7 + 1 ))
        curl -s -o /dev/null "http://${API_HOST}/api/products/${id}"
        byid_count=$((byid_count + 1))
    else
        order_count=$((order_count + 1))
        if [ $((order_count % 10)) -eq 0 ]; then
            # Invalid body — quantity 0 is rejected with 400
            body='{"productId":1,"quantity":0}'
        else
            product_id=$(( $(od -An -tu1 -N1 /dev/urandom | tr -d ' ') % 5 + 1 ))
            body="{\"productId\":${product_id},\"quantity\":2}"
        fi
        status=$(curl -s -o /dev/null -w "%{http_code}" \
            -X POST "http://${API_HOST}/api/orders" \
            -H "Content-Type: application/json" \
            -d "$body")
        case "$status" in
            5*) error_count=$((error_count + 1)) ;;
        esac
    fi
}

echo "Starting load test at $(date -u '+%Y-%m-%dT%H:%M:%SZ')..."
echo ""

last_report=0

while true; do
    now=$(date +%s)
    elapsed=$((now - start_time))

    if [ "$elapsed" -ge "$DURATION_SECONDS" ]; then
        break
    fi

    # Progress report every 30 seconds
    if [ $((elapsed - last_report)) -ge 30 ] && [ "$elapsed" -gt 0 ]; then
        total=$((list_count + byid_count + order_count))
        echo "[${elapsed}s] Sent ${total} requests (list: ${list_count}, by-id: ${byid_count}, orders: ${order_count}, 5xx: ${error_count})"
        last_report=$elapsed
    fi

    send_request

    sleep "$SLEEP_INTERVAL"
done

total=$((list_count + byid_count + order_count))
echo ""
echo "============================================="
echo "  Load Test Complete"
echo "============================================="
echo "  Duration:        ${DURATION_SECONDS}s"
echo "  Total sent:      ${total} requests"
echo "  GET list:        ${list_count}"
echo "  GET by id:       ${byid_count}"
echo "  POST orders:     ${order_count}"
echo "  5xx responses:   ${error_count}"
echo "============================================="
echo ""
echo "Open Grafana at http://localhost:3000 to see the results."
echo "Dashboard: 'RED Method'"

#!/bin/sh
# Load generator: a steady mix of good and failing orders so that traces,
# logs, and metrics all have interesting data within seconds.
#
#   - most orders use a valid productId (1-3)      -> 201 Created
#   - ~10% of those still fail inside order-service -> 500 (simulated)
#   - some orders use productId 999                 -> 404 product lookup failed
#   - some orders use quantity 0                    -> 400 bad request

TARGET_URL="${TARGET_URL:-http://localhost:8080}"
DURATION_SECONDS="${DURATION_SECONDS:-180}"

echo "loadtest: sending orders to ${TARGET_URL} for ${DURATION_SECONDS}s"

# Wait for order-service to come up.
i=0
until curl -s -o /dev/null "${TARGET_URL}/health"; do
  i=$((i + 1))
  if [ "$i" -gt 30 ]; then
    echo "loadtest: order-service did not become ready, giving up"
    exit 1
  fi
  sleep 1
done

start=$(date +%s)
count=0
ok=0
client_err=0
server_err=0

while [ $(($(date +%s) - start)) -lt "$DURATION_SECONDS" ]; do
  roll=$((count % 10))

  if [ "$roll" -eq 7 ]; then
    # Unknown product -> "product lookup failed" (404)
    product_id=999
    quantity=1
  elif [ "$roll" -eq 9 ]; then
    # Invalid quantity -> 400
    product_id=$(((count % 3) + 1))
    quantity=0
  else
    product_id=$(((count % 3) + 1))
    quantity=$(((count % 5) + 1))
  fi

  status=$(curl -s -o /dev/null -w "%{http_code}" \
    -X POST "${TARGET_URL}/api/orders" \
    -H "Content-Type: application/json" \
    -d "{\"productId\": ${product_id}, \"quantity\": ${quantity}}")

  count=$((count + 1))
  case "$status" in
    2*) ok=$((ok + 1)) ;;
    4*) client_err=$((client_err + 1)) ;;
    *) server_err=$((server_err + 1)) ;;
  esac

  if [ $((count % 50)) -eq 0 ]; then
    echo "loadtest: sent=${count} 2xx=${ok} 4xx=${client_err} 5xx=${server_err}"
  fi

  # ~5 requests/second
  sleep 0.2
done

echo "loadtest: done. sent=${count} 2xx=${ok} 4xx=${client_err} 5xx=${server_err}"

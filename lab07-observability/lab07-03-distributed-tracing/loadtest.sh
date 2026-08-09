#!/usr/bin/env bash
# Generates a mix of traffic so Jaeger has plenty of traces to look at:
# - valid orders (products 1-3)
# - invalid orders (product 99) -> error traces (red spans in Jaeger)
# - product list/detail reads
set -euo pipefail

ORDER_URL="${ORDER_URL:-http://localhost:8080}"
PRODUCT_URL="${PRODUCT_URL:-http://localhost:8081}"
REQUESTS="${REQUESTS:-50}"

echo "Sending $REQUESTS rounds of traffic (order-service: $ORDER_URL, product-service: $PRODUCT_URL)"

for i in $(seq 1 "$REQUESTS"); do
  product_id=$(( (RANDOM % 3) + 1 ))
  quantity=$(( (RANDOM % 5) + 1 ))

  # ~1 in 5 orders references an unknown product to produce error traces
  if (( RANDOM % 5 == 0 )); then
    product_id=99
  fi

  code=$(curl -s -o /dev/null -w "%{http_code}" \
    -X POST "$ORDER_URL/api/orders" \
    -H "Content-Type: application/json" \
    -d "{\"productId\": $product_id, \"quantity\": $quantity}")
  printf "%s " "$code"

  curl -s -o /dev/null "$PRODUCT_URL/api/products"
  curl -s -o /dev/null "$PRODUCT_URL/api/products/$product_id"

  sleep 0.2
done

echo
echo "Done. Open http://localhost:16686 and Find Traces for service 'order-service'."

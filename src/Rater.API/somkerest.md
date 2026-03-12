# Should return 200 allowed
curl -X POST https://localhost:5001/check \
  -H "Content-Type: application/json" \
  -d '{
    "clientId": "user:abc123",
    "ipAddress": "192.168.1.1",
    "endpoint": "/api/search",
    "httpMethod": "GET"
  }'

# Hit it 6 times fast to trigger login-strict (limit=5)
for i in {1..6}; do
  curl -X POST https://localhost:5001/check \
    -H "Content-Type: application/json" \
    -d '{
      "ipAddress": "192.168.1.1",
      "endpoint": "/api/login",
      "httpMethod": "POST"
    }'
  echo ""
done

# Check status for a client
curl https://localhost:5001/status/user:abc123

# Health check
curl https://localhost:5001/status/health
```

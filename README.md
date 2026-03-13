# Rater

```
┌─────────────────────────────────────────────────────────────┐
│                    Entry Point A                            │
│             RateLimiter.Api                                 │
│         (standalone HTTP service)                           │
│   POST /check   GET /status   GET /health                   │
└────────────────────┬────────────────────────────────────────┘
                     │
                     │  both depend on
                     │
┌────────────────────▼────────────────────────────────────────┐
│                    Entry Point B                            │
│           RateLimiter.Middleware                            │
│      (plug into any ASP.NET Core app)                       │
│   app.UseRateLimiter()  ← one line in their Program.cs      │
└────────────────────┬────────────────────────────────────────┘
                     │
                     │  both depend on
                     │
┌────────────────────▼────────────────────────────────────────┐
│                      Core Library                           │
│                  RateLimiter.Core                           │
│   Algorithms, Storage, Rules, Config, Key Extraction        │
└─────────────────────────────────────────────────────────────┘
```

## Project Structure

```
RateLimiter/
│
├── RateLimiter.sln
│
├── src/
│   │
│   ├── RateLimiter.Core/                   
│   │   ├── RateLimiter.Core.csproj
│   │   │
│   │   ├── Configuration/
│   │   │   ├── RateLimiterConfig.cs
│   │   │   ├── RateLimitRule.cs
│   │   │   └── MatchConfig.cs
│   │   │
│   │   ├── Contracts/
│   │   │   ├── RateLimitRequest.cs
│   │   │   ├── RateLimitDecision.cs
│   │   │   └── StatusResponse.cs
│   │   │
│   │   ├── Algorithms/
│   │   │   ├── IRateLimitAlgorithm.cs
│   │   │   ├── FixedWindowAlgorithm.cs
│   │   │   ├── AlgorithmFactory.cs
│   │   │   ├── TokenBucketAlgorithm.cs
│   │   │   └── SlidingWindowCounterAlgorithm.cs
│   │   │
│   │   │
│   │   ├── KeyExtraction/
│   │   │   ├── IKeyExtractor.cs
│   │   │   ├── IpKeyExtractor.cs
│   │   │   ├── ClientIdKeyExtractor.cs
│   │   │   ├── ApiKeyExtractor.cs
│   │   │   └── CompositeKeyExtractor.cs
│   │   │   └── KeyExtractorFactory.cs
│   │   │
│   │   ├── Rules/
│   │   │   └── RuleResolver.cs
│   │   │
│   │   ├── Storage/
│   │   │   ├── IStorageProvider.cs
│   │   │   ├── InMemoryStorageProvider.cs
│   │   │   └── RedisStorageProvider.cs     
│   │   │
│   │   └── Services/
│   │       └── RateLimiterService.cs       ← orchestrator
│   │
│   │
│   ├── RateLimiter.Middleware/             ← ASP.NET Core middleware package
│   │   ├── RateLimiter.Middleware.csproj   ← depends on Core
│   │   │
│   │   ├── RateLimiterMiddleware.cs        ← intercepts HttpContext
│   │   └── RateLimiterExtensions.cs        ← AddRateLimiter() / UseRateLimiter()
│   │
│   │
│   └── RateLimiter.Api/                   ← standalone HTTP service
│       ├── RateLimiter.Api.csproj         ← depends on Core + Middleware
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       │
│       └── Controllers/
│           ├── CheckController.cs          ← POST /check
│           └── StatusController.cs         ← GET /status/{clientId}
│                                              GET /health
│
│
└── tests/
    ├── RateLimiter.Core.Tests/
    │   ├── RateLimiter.Core.Tests.csproj   ← depends on Core
    │   ├── Algorithms/
    │   │   ├── FixedWindowAlgorithmTests.cs
    │   │   ├── TokenBucketAlgorithmTests.cs
    │   │   └── SlidingWindowCounterTests.cs
    │   ├── Rules/
    │   │   └── RuleResolverTests.cs        ← pending
    │   └── Services/
    │       └── RateLimiterServiceTests.cs  ← pending
    │
    └── RateLimiter.Api.Tests/
        ├── RateLimiter.Api.Tests.csproj    ← depends on Api
        └── Controllers/
            ├── CheckControllerTests.cs     ← pending
            └── StatusControllerTests.cs    ← pending
```

---

## Project Dependencies
```
RateLimiter.Core          ← no dependencies on other projects
       ▲
       │
RateLimiter.Middleware    ← depends on Core
       ▲
       │
RateLimiter.Api           ← depends on Core + Middleware
```

---

## How Middleware Wraps Core

The middleware just translates `HttpContext` → `RateLimitRequest`, calls `RateLimiterService`, then reads `RateLimitDecision`:
```
HttpContext arrives
    │
    ▼
RateLimiterMiddleware
    │  extracts IP, headers, path, method
    │  builds RateLimitRequest (Core contract)
    ▼
RateLimiterService.CheckAsync()    ← Core logic
    │
    ▼
RateLimitDecision
    │
    ├── allowed = true  → call next(context)  → request continues
    └── allowed = false → context.Response.StatusCode = 429, return
```


# Rule resolver

```
"Rules": [
  { "name": "login-strict",   "match": { "endpoint": "/api/login", "httpMethod": "POST" } },
  { "name": "search-per-user","match": { "endpoint": "/api/search" } },
  { "name": "global-default", "match": {} }
]
```

Trace three requests:
```
Request: POST /api/login
  Rule 1: endpoint matches ✅  method matches ✅  → MATCH → "login-strict"
  (stops here, rules 2 and 3 never checked)

Request: GET /api/search
  Rule 1: endpoint no match ❌
  Rule 2: endpoint matches ✅  no method constraint ✅  → MATCH → "search-per-user"

Request: DELETE /api/users
  Rule 1: endpoint no match ❌
  Rule 2: endpoint no match ❌
  Rule 3: empty match = wildcard ✅  → MATCH → "global-default"

Request: GET /api/internal (no rules match if global-default removed)
  Rule 1: ❌  Rule 2: ❌  → returns null → caller allows by default
```

---

## Wildcard Matching Explained
```
Rule endpoint: "api/v1/*"

Matches:
  api/v1/users       ✅
  api/v1/search      ✅
  api/v1/login       ✅

Does not match:
  api/v2/users       ❌
  api/users          ❌
```

Only trailing `/*` wildcards for now!

---

## ResolveAll

`Resolve` (single) is used for rate limiting — first match wins, fast.

`ResolveAll` (multiple) is used for the **status endpoint** — when you ask *"what is the state of client X?"*, you want to see all rules that would apply to them, not just the first one.
```
GET /status/user:abc123

→ ResolveAll finds: "search-per-user", "global-default"
→ Status shows current counter state for both rules
```

---

ref: https://bytebytego.com/courses/system-design-interview/design-a-rate-limiter

--- 
### Algorithms

```
REQUEST STREAM: 10 requests/sec, Limit=100/min

FixedWindow:
  T=00:00  counter=0
  T=00:30  counter=50   → 50 remaining
  T=00:59  counter=99   → 1 remaining
  T=00:59  counter=100  → DENY
  T=01:00  counter=0    ← HARD RESET (boundary attack possible here)

TokenBucket:
  T=00:00  tokens=100   (full bucket)
  T=00:10  tokens=0     (burst of 100 consumed) → DENY
  T=00:16  tokens=1     (refilled 1 token)      → Allow 1
  T=00:22  tokens=1     → Allow 1  (smooth trickle after burst)

SlidingWindow:
  T=00:59  looks back 60s, sums weighted slots
           no hard reset — always a rolling count
           boundary attack impossible ← key advantage

```

## The Full Flow

Trace one request end-to-end through the code:
```
POST /check
{
  "clientId": "user:abc",
  "ipAddress": "1.2.3.4",
  "endpoint": "/api/search",
  "httpMethod": "GET"
}

       │
       ▼
CheckAsync(request)
       │
       ├─ RuleResolver.Resolve(request)
       │    walks rules top-to-bottom
       │    finds "search-per-user" (endpoint matches, no method constraint)
       │    returns RateLimitRule
       │
       ├─ KeyExtractorFactory.Resolve(ClientId)
       │    returns ClientIdKeyExtractor
       │
       ├─ ClientIdKeyExtractor.Extract(request, rule)
       │    returns "rl:client:user:abc:api/search:search-per-user"
       │
       ├─ AlgorithmFactory.Resolve(TokenBucket)
       │    returns TokenBucketAlgorithm
       │
       ├─ TokenBucketAlgorithm.IsAllowedAsync(key, rule, storage)
       │    reads bucket state from InMemoryStorageProvider
       │    calculates refill
       │    consumes 1 token
       │    returns RateLimitDecision { Allowed: true, Remaining: 46 }
       │
       └─ returns decision to caller
```


## Each Test Group Covers
```
FixedWindowAlgorithmTests
  ├── Allow cases
  │   ├── first request allowed
  │   ├── exactly at limit allowed
  │   ├── remaining decrements
  │   └── different keys independent
  ├── Deny cases
  │   ├── over limit denied
  │   ├── denied has zero remaining
  │   ├── denied has RetryAfter set
  │   └── denied has ResetAt in future
  └── Edge cases
      ├── limit of 1
      ├── window expiry resets counter   ← time-based
      └── rule name in decision

TokenBucketAlgorithmTests
  ├── Burst behaviour
  │   ├── first request allowed
  │   ├── full burst up to capacity
  │   ├── beyond capacity denied
  │   └── remaining = 0 after burst
  ├── Refill behaviour
  │   ├── token refills after wait       ← time-based
  │   ├── tokens never exceed capacity   ← overflow guard
  │   └── partial refill proportional    ← time-based
  ├── Retry-After
  │   └── reflects actual refill time
  └── Independence
      └── different keys independent

SlidingWindowCounterAlgorithmTests
  ├── Basic allow / deny
  │   ├── first request allowed
  │   ├── within limit allowed
  │   ├── beyond limit denied
  │   └── remaining decrements
  ├── Sliding behaviour        ← key advantage
  │   ├── old requests outside window don't count
  │   └── boundary attack mitigated
  ├── Window expiry
  │   └── counter resets after full window
  ├── Independence
  │   └── different keys independent
  └── Decision metadata
      ├── ResetAt in future
      └── rule name correct
```

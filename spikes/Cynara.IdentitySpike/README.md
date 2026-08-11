# Cynara Identity + OpenIddict Spike

Disposable proof of concept for Linear issue
[CYN-95](https://linear.app/ailuracode/issue/CYN-95) (parent spike
[CYN-94](https://linear.app/ailuracode/issue/CYN-94)): validate ASP.NET Core
Identity + OpenIddict as the backend identity/token layer while preserving the
existing Cynara capability model. This project is intentionally isolated under
`spikes/` and is NOT part of `Cynara.Api.sln`; production code under `src/` is
untouched.

The full technical evidence and ADR findings are in [FINDINGS.md](./FINDINGS.md).

## What it demonstrates

- ASP.NET Core Identity user store with password credentials (SQLite, disposable).
- OpenIddict token server: password, refresh token, and client credentials
  grants; discovery and JWKS; issuer/audience/signing-key validation; refresh
  token revocation.
- **User -> Membership -> (Hospital, Actor) -> Capabilities** integration with
  the unmodified Cynara Application services (`HospitalContext`,
  `EffectiveCapabilityResolver`, `CapabilityGuard`). The `X-Actor-Id` header
  source is replaced by the authenticated principal; the `X-Hospital-Code`
  header pattern is preserved.
- Hospital isolation: one user, two hospitals, different actor identity and
  different capability sets per hospital, same access token.

## Prerequisites

- .NET SDK 10.0.302 (see repo `global.json`).

## Run

```bash
dotnet run --project spikes/Cynara.IdentitySpike
```

The host listens on `http://localhost:5295` and resets/seeds the SQLite
database on every startup (`data/spike.db`).

Seed data:

| Item | Value |
|---|---|
| Hospitals | `hosp-a` (Hospital Alpha), `hosp-b` (Hospital Beta) |
| User | `doctor@cynara.dev` / `Cynara!Dev123` |
| Memberships | hosp-a -> actor `doctor-alpha`, hosp-b -> actor `doctor-beta` |
| Capabilities | hosp-a/doctor-alpha: `patients.read`, `encounters.write`; hosp-b/doctor-beta: `patients.read` |
| OAuth client | `cynara-spike` / `spike-secret` (confidential) |

## Curl cheat sheet

```bash
BASE=http://localhost:5295

# Discovery and JWKS
curl -s $BASE/.well-known/openid-configuration
curl -s $BASE/.well-known/jwks

# Password grant -> access token
TOKEN=$(curl -s -X POST $BASE/connect/token \
  -d grant_type=password -d username=doctor@cynara.dev \
  -d password=Cynara!Dev123 -d client_id=cynara-spike \
  -d client_secret=spike-secret \
  -d 'scope=openid profile email offline_access cynara_api' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['access_token'])")

# Current user, per hospital
curl -s $BASE/api/me -H "Authorization: Bearer $TOKEN" -H "X-Hospital-Code: hosp-a"
curl -s $BASE/api/me -H "Authorization: Bearer $TOKEN" -H "X-Hospital-Code: hosp-b"

# Capability-protected endpoints (200 vs 403 proves isolation)
curl -s $BASE/api/encounters -H "Authorization: Bearer $TOKEN" -H "X-Hospital-Code: hosp-a"
curl -s $BASE/api/encounters -H "Authorization: Bearer $TOKEN" -H "X-Hospital-Code: hosp-b"
curl -s $BASE/api/patients   -H "Authorization: Bearer $TOKEN" -H "X-Hospital-Code: hosp-b"
```

## Key files

| File | Role |
|---|---|
| `Program.cs` | Host: Identity + OpenIddict server/validation + middleware + endpoints |
| `Data/SpikeDbContext.cs` | SQLite context (Identity + OpenIddict stores + domain tables) |
| `Data/SeedData.cs` | Deterministic seed |
| `Domain/Membership.cs` | Spike entity linking User <-> Hospital <-> Actor |
| `Auth/MembershipResolutionMiddleware.cs` | Principal -> membership -> HospitalContext + actor |
| `Auth/PrincipalCurrentActor.cs` | `ICurrentActor` backed by the authenticated principal |
| `Endpoints/TokenController.cs` | OpenIddict token endpoint (password/refresh/client_credentials) |
| `Endpoints/MeEndpoints.cs` | `GET /api/me` |
| `Endpoints/ProtectedEndpoints.cs` | Capability-guarded demo endpoints |
| `FINDINGS.md` | Technical evidence + implications for the parent ADR |

# Auth0 outage

## Symptom

Nobody can sign in. Existing sessions keep working until their tokens expire, so this
often arrives as a trickle rather than a cliff — a few users at first, then everyone.

`nornis-availability` may fire, because the ping hits `nornis.app`, which redirects to
Auth0 when unauthenticated. `/status` will be **entirely green**: Auth0 is not one of the
five dependency checks, because the API validates JWTs against cached signing keys and
does not call Auth0 per request.

That gap is worth knowing. A green status page does not mean people can log in.

## Setup

| | |
| --- | --- |
| Domain | `auth.nornis.app` (custom domain) |
| API audience | `https://api.nornis.app` |
| Tenant | Shared with Velum — an outage or a misconfiguration here affects both |

The Web host holds `Auth0__ClientId` / `Auth0__ClientSecret` and runs the OIDC login flow;
the API only validates the resulting tokens.

## Diagnose

Separate "Auth0 is down" from "our configuration is wrong" — they look identical to a user
and have completely different remedies.

```bash
# Is the tenant answering? OpenID discovery needs no credentials.
curl -s -o /dev/null -w "discovery [%{http_code}]\n" \
  https://auth.nornis.app/.well-known/openid-configuration

# Are the signing keys served?
curl -s -o /dev/null -w "jwks [%{http_code}]\n" https://auth.nornis.app/.well-known/jwks.json

# Certificate on the custom domain still valid?
curl -sv https://auth.nornis.app/.well-known/openid-configuration 2>&1 \
  | grep -iE "expire date|subject:"
```

| Result | Meaning |
| --- | --- |
| Both endpoints non-200 | Auth0 side. Check <https://status.auth0.com> |
| Both 200, logins still fail | Ours — configuration, secret, or callback URL |
| Certificate expired | The custom domain's cert lapsed; renew in the Auth0 dashboard |

For our side, the login failure itself is logged:

```bash
az containerapp logs show -n ca-nornis-web -g rg-nornis --tail 200 \
  | grep -iE "auth|oidc|token|unauthorized"
```

A recently rotated `Auth0__ClientSecret` that was updated in Auth0 but not on
`ca-nornis-web` produces exactly this, and is the most common self-inflicted version.

## Remedy

**Auth0 is down.** Nothing to do but wait and communicate. Do not change configuration
during a provider outage — you will be debugging your own edits afterwards. Existing
sessions survive; new logins do not.

**Client secret drift.** Update the Web host:

```bash
az containerapp update -n ca-nornis-web -g rg-nornis \
  --set-env-vars "Auth0__ClientSecret=<new secret>" -o none
```

**Callback URL rejected** (`redirect_uri` mismatch in the error): the allowed callback and
logout URLs in the Auth0 application must include `https://nornis.app/signin-oidc` and
`https://nornis.app/welcome`. Fix in the Auth0 dashboard, not in code.

## Verify

Sign in from a private window — an existing session will succeed regardless of whether the
problem is fixed, which makes an ordinary browser useless as a test here.

```bash
curl -s -o /dev/null -w "web [%{http_code}]\n" https://nornis.app/    # 302 to Auth0
```

## Notes

Because the tenant is shared with Velum, changes here reach beyond Nornis. Check whether
Velum is also affected before concluding the cause is Nornis-specific — and before
changing a tenant-level setting to fix a Nornis-specific symptom.

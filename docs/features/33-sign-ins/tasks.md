# Tasks

One PR. Ships as Nornis v3.3.

**Status: built 2026-11-02; waits on the migration and the Auth0 connections.**

Dave's part, outside the repo: apply `AddUserSignIns` to prod (design.md, *Downtime*), and
check that the Nornis web application in Auth0 has every connection he wants to offer
(Discord, Google, Microsoft) switched on, since **Link another sign-in** shows only those.

- [x] 1. Spec: `requirements.md`, `design.md`, this file, row 33 in `docs/features/README.md`.
- [x] 2. Amendments: `domain-model.md` (`UserSignIn`, `SignInLinkCode`, `Auth0SubjectId`
      gone), `security-and-permissions.md` (identity is a set of subjects; linking rules).
- [x] 3. Domain: `UserSignIn`, `SignInLinkCode`, `User.SignIns`; `User.Auth0SubjectId` removed.
- [x] 4. Infrastructure: configurations, migration `AddUserSignIns` (copy, then drop),
      `UserRepository.GetBySubjectAsync` / `CreateAsync(user, subject)`,
      `UserSignInRepository`.
- [x] 5. Application: `SignInService`, `SignInProviders`; Bibliotaph Connect's subject check.
- [x] 6. API: provisioning and the dev bypass through sign-ins, `[SkipUserProvisioning]`,
      `SignInsController`, cache eviction on remove.
- [x] 7. Web: `BearerTokenHandler` keeps an explicit bearer, `/account/link-sign-in`, the OIDC
      events, `SignInsCard` on Profile, the 3.3 change log entry and version.
- [x] 8. Tests per design.md; build, `dotnet test`, `dotnet format --verify-no-changes`.

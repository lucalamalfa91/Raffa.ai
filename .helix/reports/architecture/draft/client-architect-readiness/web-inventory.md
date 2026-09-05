# Web inventory (client-architect-readiness)

- e06–e08 are **WAVE_COVERED**. Do not put screens, tokens, or TS client
  regen in e10.
- `web/public/config.json` is a localhost placeholder. `web.yml` overwrites
  `web/dist/config.json` per environment (API URL + OIDC public client).
- Demo SWA must receive the **demo** API origin and Entra public client after
  `demo-v*` — verify in e10 smoke, do not rebuild the injector.
- CORS SWA↔API is an infra/API concern already in e01/e06 scope
  (`WAVE_COVERED`).
- Day-1 must not be a Swagger page.

`VOTE: APPROVE`

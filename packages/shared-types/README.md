# @mangrove/shared-types

Placeholder for shared DTOs and the OpenAPI-generated TypeScript client (spec §15).

Phase 1 inlines the small set of needed types in `apps/web/src/api.ts`. In Phase 2 this package
will host the OpenAPI-generated client (the backend already exposes Swagger at `/swagger`) so the
web and Android apps can share one typed contract.
